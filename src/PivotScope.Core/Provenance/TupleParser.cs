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

    /// <summary>
    /// Splits on commas outside identifiers. Inside brackets, "]]" is an
    /// escaped bracket and "[" is plain text: counting brackets as a depth
    /// would go negative on "[Taux]]x]" and stop splitting altogether.
    /// </summary>
    private static IEnumerable<string> SplitTopLevel(string text)
    {
        var inIdentifier = false;
        var start = 0;

        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i];
            if (inIdentifier)
            {
                if (ch != ']') continue;
                if (i + 1 < text.Length && text[i + 1] == ']') { i++; continue; }
                inIdentifier = false;
            }
            else if (ch == '[')
            {
                inIdentifier = true;
            }
            else if (ch == ',')
            {
                yield return text[start..i];
                start = i + 1;
            }
        }

        yield return text[start..];
    }
}
