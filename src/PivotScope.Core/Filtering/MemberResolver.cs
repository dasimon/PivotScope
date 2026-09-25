using System.Text;
using PivotScope.Core.Abstractions;
using PivotScope.Core.Query;

namespace PivotScope.Core.Filtering;

/// <summary>
/// What was resolved, what was not, and what matched too much.
/// All three matter: a silently ignored key is a wrong filter.
/// <see cref="LevelTruncated"/> says the caption lookup only saw the first
/// members of the level: a caption found once may exist again further down.
/// </summary>
public sealed record MemberResolution(
    IReadOnlyList<string> UniqueNames,
    IReadOnlyList<string> Unresolved,
    IReadOnlyList<string> Ambiguous,
    bool LevelTruncated = false);

/// <summary>
/// Translates a list of values pasted by the user into MDX member unique
/// names. Three forms are accepted, tried in this order:
///
/// 1. a full unique name ("[Dim].[Hier].[Level].&amp;[X]") — taken as is;
/// 2. a member KEY — addressed as "level.&amp;[value]", direct and without a scan;
/// 3. a CAPTION — resolved by enumerating the members of the level.
///
/// Step 3 exists because nobody has the technical keys at hand: on a real
/// cube, the user pastes "Aurore" while the key is "PRD014". Measured: 3,157
/// members of a level in 79 ms, and the enumeration only happens if some
/// values are still unresolved.
///
/// Pitfall inherited from CubeScope: NEVER go through $SYSTEM.MDSCHEMA_MEMBERS,
/// which does not support IN and walks the entire dimension. Everything goes through MDX.
/// </summary>
public sealed class MemberResolver(IMdxExecutor executor, ILevelMemberReader? levelMembers = null)
{
    /// <summary>
    /// Beyond this, the probe query gets long and a single dead key makes the
    /// fallback expensive. Empirical value, not a server constraint.
    /// </summary>
    private const int BatchSize = 100;

    /// <summary>
    /// Cap on the enumeration of a level. At 79 ms for 3,157 members, 50,000
    /// stays under a second; beyond that, pasting keys is the better option.
    /// </summary>
    internal const int LevelMemberLimit = 50_000;

    private static readonly char[] LineSeparators = ['\r', '\n', '\t'];
    private static readonly char[] InlineSeparators = [';', ','];

    /// <summary>
    /// Splits a user paste into values. A paste from Excel or from a text file
    /// is line- or tab-separated: commas and semicolons then belong to the
    /// values — "Actions, Europe" is ONE caption, and cutting it in two could
    /// resolve "Europe" to another member, a wrong filter without any warning.
    /// Only a single line typed by hand is split on commas and semicolons.
    /// </summary>
    public static IReadOnlyList<string> ParseKeys(string pasted)
    {
        var separators = pasted.IndexOfAny(LineSeparators) >= 0 ? LineSeparators : InlineSeparators;
        return [.. pasted.Split(separators, StringSplitOptions.RemoveEmptyEntries |
                                            StringSplitOptions.TrimEntries)];
    }

    /// <summary>The key is escaped: a "]" in a key is written "]]" in MDX.</summary>
    public static string BuildUniqueName(string levelUniqueName, string key)
        => $"{levelUniqueName}.&[{key.Trim().Replace("]", "]]")}]";

    /// <summary>An MDX unique name already written by the user, not to be wrapped again.</summary>
    private static bool LooksLikeUniqueName(string value)
        => value.StartsWith('[') && value.Contains("].[", StringComparison.Ordinal);

    /// <summary>
    /// "[Dim].[Hier].[Level]" → "[Dim].[Hier]." — a unique name pasted as is
    /// must belong to the hierarchy of the chosen level, otherwise Excel
    /// answers with an opaque "item not found in the OLAP cube".
    /// </summary>
    private static string HierarchyPrefix(string levelUniqueName)
    {
        var last = levelUniqueName.LastIndexOf("].[", StringComparison.Ordinal);
        return last < 0 ? levelUniqueName : levelUniqueName[..(last + 2)];
    }

    /// <summary>
    /// Failures that say nothing about the keys themselves: the user cancelled,
    /// or the connection is gone. Reporting every key as "unresolved" would
    /// then send the user looking for a problem in their list.
    /// </summary>
    private static bool IsTransportOrCancel(Exception ex)
        => ex is OperationCanceledException
           || ex.GetType().Name.Contains("Connection", StringComparison.Ordinal);

    public async Task<MemberResolution> ResolveAsync(
        string cube,
        string levelUniqueName,
        IEnumerable<string> keys,
        CancellationToken ct = default)
    {
        var distinct = keys
            .Where(k => !string.IsNullOrWhiteSpace(k))
            .Select(k => k.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (distinct.Count == 0) return new MemberResolution([], [], []);

        var resolved = new List<string>();
        var pending = new List<string>();

        // Step 1 — full unique names are taken as is, without going to the
        // server, provided they belong to the hierarchy of the chosen level.
        var hierarchy = HierarchyPrefix(levelUniqueName);
        var foreign = new List<string>();
        var toProbe = new List<string>();
        foreach (var value in distinct)
        {
            if (!LooksLikeUniqueName(value)) toProbe.Add(value);
            else if (value.StartsWith(hierarchy, StringComparison.OrdinalIgnoreCase)) resolved.Add(value);
            else foreign.Add(value);
        }

        // Step 2 — attempt by key, in batches.
        foreach (var batch in Chunk(toProbe, BatchSize))
        {
            try
            {
                var captions = await ProbeAsync(cube, levelUniqueName, batch, ct).ConfigureAwait(false);
                for (var i = 0; i < batch.Count; i++)
                {
                    if (i < captions.Count && captions[i] is not null)
                        resolved.Add(BuildUniqueName(levelUniqueName, batch[i]));
                    else
                        pending.Add(batch[i]);
                }
            }
            catch (Exception ex) when (!IsTransportOrCancel(ex))
            {
                // An invalid reference can bring down the whole batch: retry
                // value by value to isolate the faulty ones.
                await ProbeOneByOneAsync(cube, levelUniqueName, batch, resolved, pending, ct)
                    .ConfigureAwait(false);
            }
        }

        var resolution = pending.Count == 0
            ? new MemberResolution(resolved, [], [])
            // Step 3 — fallback by caption, a single enumeration of the level.
            : await ResolveByCaptionAsync(cube, levelUniqueName, resolved, pending, ct)
                .ConfigureAwait(false);

        // A key and the caption of the same member, or "eur" and "EUR", must
        // not reach VisibleItemsList twice.
        return resolution with
        {
            UniqueNames = [.. resolution.UniqueNames.Distinct(StringComparer.OrdinalIgnoreCase)],
            Unresolved = [.. foreign, .. resolution.Unresolved],
        };
    }

    private async Task<MemberResolution> ResolveByCaptionAsync(
        string cube,
        string levelUniqueName,
        List<string> resolved,
        List<string> pending,
        CancellationToken ct)
    {
        if (levelMembers is null) return new MemberResolution(resolved, pending, []);

        IReadOnlyList<LevelMember> members;
        try
        {
            // One more than the cap: that extra member is what tells a truncated
            // enumeration apart from a level that has exactly the cap.
            members = await levelMembers
                .GetLevelMembersAsync(cube, levelUniqueName, LevelMemberLimit + 1, ct)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (!IsTransportOrCancel(ex))
        {
            // The enumeration is only a convenience: its failure must not erase
            // what the key step has already resolved.
            return new MemberResolution(resolved, pending, []);
        }

        // Past the cap, a caption seen once may well exist a second time
        // further down: the result is flagged, never presented as complete.
        var truncated = members.Count > LevelMemberLimit;

        var byCaption = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var member in members.Take(LevelMemberLimit))
        {
            var caption = member.Caption.Trim();
            if (!byCaption.TryGetValue(caption, out var list))
                byCaption[caption] = list = [];
            list.Add(member.UniqueName);
        }

        var unresolved = new List<string>();
        var ambiguous = new List<string>();

        foreach (var value in pending)
        {
            if (!byCaption.TryGetValue(value, out var matches)) unresolved.Add(value);
            else if (matches.Count == 1) resolved.Add(matches[0]);
            else ambiguous.Add(value);
        }

        return new MemberResolution(resolved, unresolved, ambiguous, truncated);
    }

    private async Task ProbeOneByOneAsync(
        string cube,
        string levelUniqueName,
        IReadOnlyList<string> batch,
        List<string> resolved,
        List<string> pending,
        CancellationToken ct)
    {
        // If nothing at all goes through, the keys are not the problem: surface
        // the server's error rather than reporting every key as unresolved.
        Exception? firstError = null;
        var successes = 0;

        foreach (var key in batch)
        {
            try
            {
                var one = await ProbeAsync(cube, levelUniqueName, [key], ct).ConfigureAwait(false);
                successes++;
                if (one.Count > 0 && one[0] is not null)
                    resolved.Add(BuildUniqueName(levelUniqueName, key));
                else
                    pending.Add(key);
            }
            catch (Exception ex) when (!IsTransportOrCancel(ex))
            {
                firstError ??= ex;
                pending.Add(key);
            }
        }

        if (successes == 0 && firstError is not null && batch.Count > 1)
            throw new InvalidOperationException(
                $"Le serveur a refusé toutes les recherches de membres : {firstError.Message}",
                firstError);
    }

    /// <summary>
    /// One query, one calculated member per value. The caption comes back
    /// non-null if and only if the member exists — observed on a real cube:
    /// StrToMember on a nonexistent member does not throw, it returns null.
    ///
    /// Everything spliced into the query is escaped: the unique name sits in a
    /// single-quoted MDX string ("'" doubled), the cube in brackets ("]"
    /// doubled). Only a string counts as found: a cell in error comes back as
    /// something else — a <see cref="CellError"/>, or CubeScope's "#ERREUR".
    /// </summary>
    private async Task<IReadOnlyList<string?>> ProbeAsync(
        string cube, string levelUniqueName, IReadOnlyList<string> keys, CancellationToken ct)
    {
        var mdx = new StringBuilder("WITH ");
        for (var i = 0; i < keys.Count; i++)
        {
            var unique = BuildUniqueName(levelUniqueName, keys[i]).Replace("'", "''");
            mdx.Append("MEMBER [Measures].[__cap").Append(i).Append("] AS StrToMember('")
               .Append(unique).Append("').Properties('MEMBER_CAPTION') ");
        }

        mdx.Append("SELECT {");
        mdx.AppendJoin(',', Enumerable.Range(0, keys.Count).Select(i => $"[Measures].[__cap{i}]"));
        mdx.Append("} ON 0 FROM [").Append(cube.Replace("]", "]]")).Append(']');

        var result = await executor.ExecuteAsync(mdx.ToString(), ct).ConfigureAwait(false);
        if (result.Rows.Count == 0) return [];

        // Read by column position, not by name: the CellSet mapping decides
        // the column label, the order of the measures is what counts.
        var row = result.Rows[0];
        var values = new List<string?>(keys.Count);
        for (var i = 0; i < keys.Count && i < result.Columns.Count; i++)
        {
            row.TryGetValue(result.Columns[i].Field, out var value);
            values.Add(value is string caption && caption != CellError.LegacyMarker ? caption : null);
        }
        return values;
    }

    private static IEnumerable<List<string>> Chunk(List<string> items, int size)
    {
        for (var i = 0; i < items.Count; i += size)
            yield return items.GetRange(i, Math.Min(size, items.Count - i));
    }
}
