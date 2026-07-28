using System.Text;
using PivotScope.Core.Abstractions;

namespace PivotScope.Core.Filtering;

/// <summary>
/// Ce qui a été résolu, ce qui ne l'a pas été, et ce qui l'était trop.
/// Les trois comptent : une clé silencieusement ignorée est un filtre faux.
/// </summary>
public sealed record MemberResolution(
    IReadOnlyList<string> UniqueNames,
    IReadOnlyList<string> Unresolved,
    IReadOnlyList<string> Ambiguous);

/// <summary>
/// Traduit une liste de valeurs collées par l'utilisateur en noms uniques de
/// membres MDX. Trois formes acceptées, essayées dans cet ordre :
///
/// 1. un nom unique complet (« [Dim].[Hier].[Niveau].&amp;[X] ») — repris tel quel ;
/// 2. une CLÉ de membre — adressée par « niveau.&amp;[valeur] », direct et sans scan ;
/// 3. un LIBELLÉ — résolu en énumérant les membres du niveau.
///
/// L'étape 3 existe parce que personne n'a les clés techniques sous la main :
/// sur un cube réel, l'utilisateur colle « Aurore » alors que la clé est
/// « PRD014 ». Mesuré : 3 157 membres d'un niveau en 79 ms, et l'énumération
/// n'a lieu que s'il reste des valeurs non résolues.
///
/// Piège hérité de CubeScope : ne JAMAIS passer par $SYSTEM.MDSCHEMA_MEMBERS,
/// qui ne supporte pas IN et parcourt la dimension entière. Tout passe par MDX.
/// </summary>
public sealed class MemberResolver(IMdxExecutor executor, ILevelMemberReader? levelMembers = null)
{
    /// <summary>
    /// Au-delà, la requête de sondage devient longue et une seule clé morte
    /// coûte cher au repli. Valeur empirique, pas une contrainte du serveur.
    /// </summary>
    private const int BatchSize = 100;

    /// <summary>
    /// Plafond de l'énumération d'un niveau. À 79 ms pour 3 157 membres, 50 000
    /// reste sous la seconde ; au-delà, mieux vaut coller des clés.
    /// </summary>
    private const int LevelMemberLimit = 50_000;

    private static readonly char[] Separators = ['\r', '\n', '\t', ';', ','];

    /// <summary>Découpe un collage utilisateur en valeurs, quel qu'en soit le séparateur.</summary>
    public static IReadOnlyList<string> ParseKeys(string pasted) =>
        [.. pasted.Split(Separators, StringSplitOptions.RemoveEmptyEntries |
                                     StringSplitOptions.TrimEntries)];

    public static string BuildUniqueName(string levelUniqueName, string key)
        => $"{levelUniqueName}.&[{key.Trim()}]";

    /// <summary>Un nom unique MDX déjà écrit par l'utilisateur, à ne pas réencadrer.</summary>
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

        // Étape 1 — les noms uniques complets sont repris sans aller au serveur.
        var toProbe = new List<string>();
        foreach (var value in distinct)
        {
            if (LooksLikeUniqueName(value)) resolved.Add(value);
            else toProbe.Add(value);
        }

        // Étape 2 — tentative par clé, en lots.
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
                // Une référence invalide peut faire tomber le lot entier : on
                // repasse valeur par valeur pour isoler les fautives.
                await ProbeOneByOneAsync(cube, levelUniqueName, batch, resolved, pending, ct)
                    .ConfigureAwait(false);
            }
        }

        if (pending.Count == 0) return new MemberResolution(resolved, [], []);

        // Étape 3 — repli par libellé, une seule énumération du niveau.
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
            // L'énumération n'est qu'un confort : son échec ne doit pas effacer
            // ce que l'étape par clé a déjà résolu.
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
    /// Une requête, un membre calculé par valeur. La caption revient non nulle
    /// si et seulement si le membre existe — constaté sur un cube réel :
    /// StrToMember sur un membre inexistant ne lève pas, il renvoie null.
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

        // On lit par position de colonne, pas par nom : le mapping du CellSet
        // décide du libellé de colonne, l'ordre des mesures est ce qui fait foi.
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
