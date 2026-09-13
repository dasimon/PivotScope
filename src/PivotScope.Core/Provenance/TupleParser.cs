namespace PivotScope.Core.Provenance;

/// <summary>A decomposed MDX tuple: the measure on one side, the coordinates on the other.</summary>
public sealed record MdxTuple(string? Measure, IReadOnlyList<string> Coordinates);

/// <summary>
/// Reads the string returned by <c>PivotCell.MDX</c>, of the form
/// <c>([Measures].[VL],[Devise].[Devise].&amp;[EUR])</c>.
///
/// Splitting happens at bracket depth zero: a member caption can contain a
/// comma ("[Actions, Europe]"), and naively splitting on "," would cut the
/// coordinate in two.
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
