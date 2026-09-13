using System.Text;
using PivotScope.Core.Abstractions;

namespace PivotScope.Core.Filtering;

/// <summary>
/// What was resolved, what was not, and what matched too much.
/// All three matter: a silently ignored key is a wrong filter.
/// </summary>
public sealed record MemberResolution(
    IReadOnlyList<string> UniqueNames,
    IReadOnlyList<string> Unresolved,
    IReadOnlyList<string> Ambiguous);

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
    private const int LevelMemberLimit = 50_000;

    private static readonly char[] Separators = ['\r', '\n', '\t', ';', ','];

    /// <summary>Splits a user paste into values, whatever the separator.</summary>
    public static IReadOnlyList<string> ParseKeys(string pasted) =>
        [.. pasted.Split(Separators, StringSplitOptions.RemoveEmptyEntries |
                                     StringSplitOptions.TrimEntries)];

    public static string BuildUniqueName(string levelUniqueName, string key)
        => $"{levelUniqueName}.&[{key.Trim()}]";

    /// <summary>An MDX unique name already written by the user, not to be wrapped again.</summary>
    private static bool LooksLikeUniqueName(string value)
        => value.StartsWith('[') && value.Contains("].[", StringComparison.Ordinal);

    public async Task<MemberResolution> ResolveAsync(
        string cube,
        string levelUniqueName,
        IEnumerable<string> keys,
        CancellationToken ct = default)
    {
        var distinct = keys
            .Where(k => !string.IsNullOrWhiteSpace(k))
            .Select(k => k.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (distinct.Count == 0) return new MemberResolution([], [], []);

        var resolved = new List<string>();
        var pending = new List<string>();

        // Step 1 — full unique names are taken as is, without going to the server.
        var toProbe = new List<string>();
        foreach (var value in distinct)
        {
            if (LooksLikeUniqueName(value)) resolved.Add(value);
            else toProbe.Add(value);
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
            catch
            {
                // An invalid reference can bring down the whole batch: retry
                // value by value to isolate the faulty ones.
                await ProbeOneByOneAsync(cube, levelUniqueName, batch, resolved, pending, ct)
                    .ConfigureAwait(false);
            }
        }

        if (pending.Count == 0) return new MemberResolution(resolved, [], []);

        // Step 3 — fallback by caption, a single enumeration of the level.
        return await ResolveByCaptionAsync(cube, levelUniqueName, resolved, pending, ct)
            .ConfigureAwait(false);
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
            members = await levelMembers
                .GetLevelMembersAsync(cube, levelUniqueName, LevelMemberLimit, ct)
                .ConfigureAwait(false);
        }
        catch
        {
            // The enumeration is only a convenience: its failure must not erase
            // what the key step has already resolved.
            return new MemberResolution(resolved, pending, []);
        }

        var byCaption = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var member in members)
        {
            if (!byCaption.TryGetValue(member.Caption, out var list))
                byCaption[member.Caption] = list = [];
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

        return new MemberResolution(resolved, unresolved, ambiguous);
    }

    private async Task ProbeOneByOneAsync(
        string cube,
        string levelUniqueName,
        IReadOnlyList<string> batch,
        List<string> resolved,
        List<string> pending,
        CancellationToken ct)
    {
        foreach (var key in batch)
        {
            try
            {
                var one = await ProbeAsync(cube, levelUniqueName, [key], ct).ConfigureAwait(false);
                if (one.Count > 0 && one[0] is not null)
                    resolved.Add(BuildUniqueName(levelUniqueName, key));
                else
                    pending.Add(key);
            }
            catch
            {
                pending.Add(key);
            }
        }
    }

    /// <summary>
    /// One query, one calculated member per value. The caption comes back
    /// non-null if and only if the member exists — observed on a real cube:
    /// StrToMember on a nonexistent member does not throw, it returns null.
    /// </summary>
    private async Task<IReadOnlyList<string?>> ProbeAsync(
        string cube, string levelUniqueName, IReadOnlyList<string> keys, CancellationToken ct)
    {
        var mdx = new StringBuilder("WITH ");
        for (var i = 0; i < keys.Count; i++)
        {
            var unique = BuildUniqueName(levelUniqueName, keys[i]);
            mdx.Append("MEMBER [Measures].[__cap").Append(i).Append("] AS StrToMember(\"")
               .Append(unique).Append("\").Properties(\"MEMBER_CAPTION\") ");
        }

        mdx.Append("SELECT {");
        mdx.AppendJoin(',', Enumerable.Range(0, keys.Count).Select(i => $"[Measures].[__cap{i}]"));
        mdx.Append("} ON 0 FROM [").Append(cube).Append(']');

        var result = await executor.ExecuteAsync(mdx.ToString(), ct).ConfigureAwait(false);
        if (result.Rows.Count == 0) return [];

        // Read by column position, not by name: the CellSet mapping decides
        // the column label, the order of the measures is what counts.
        var row = result.Rows[0];
        var values = new List<string?>(keys.Count);
        for (var i = 0; i < keys.Count && i < result.Columns.Count; i++)
        {
            row.TryGetValue(result.Columns[i].Field, out var value);
            values.Add(value as string);
        }
        return values;
    }

    private static IEnumerable<List<string>> Chunk(List<string> items, int size)
    {
        for (var i = 0; i < items.Count; i += size)
            yield return items.GetRange(i, Math.Min(size, items.Count - i));
    }
}
