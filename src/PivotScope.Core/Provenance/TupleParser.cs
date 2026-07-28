namespace PivotScope.Core.Provenance;

/// <summary>Un tuple MDX décomposé : la mesure d'un côté, les coordonnées de l'autre.</summary>
public sealed record MdxTuple(string? Measure, IReadOnlyList<string> Coordinates);

/// <summary>
/// Lit la chaîne rendue par <c>PivotCell.MDX</c>, de la forme
/// <c>([Measures].[VL],[Devise].[Devise].&amp;[EUR])</c>.
///
/// Le découpage se fait à profondeur zéro de crochets : un libellé de membre
/// peut contenir une virgule (« [Actions, Europe] »), et découper naïvement sur
/// « , » couperait la coordonnée en deux.
/// </summary>
public static class TupleParser
{
    private const string MeasuresPrefix = "[Measures].";

    public static MdxTuple Parse(string? tuple)
    {
        var text = (tuple ?? string.Empty).Trim();
        if (text.StartsWith('(') && text.EndsWith(')')) text = text[1..^1];
        if (string.IsNullOrWhiteSpace(text)) return new MdxTuple(null, []);

        string? measure = null;
        var coordinates = new List<string>();

        foreach (var part in SplitTopLevel(text))
        {
            var member = part.Trim();
            if (member.Length == 0) continue;

            if (measure is null &&
                member.StartsWith(MeasuresPrefix, StringComparison.OrdinalIgnoreCase))
                measure = member;
            else
                coordinates.Add(member);
        }

        return new MdxTuple(measure, coordinates);
    }

    private static IEnumerable<string> SplitTopLevel(string text)
    {
        var depth = 0;
        var start = 0;

        for (var i = 0; i < text.Length; i++)
        {
            switch (text[i])
            {
                case '[': depth++; break;
                case ']': depth--; break;
                case ',' when depth == 0:
                    yield return text[start..i];
                    start = i + 1;
                    break;
            }
        }

        yield return text[start..];
    }
}
