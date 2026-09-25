using CubeScope.Core.Models;

namespace PivotScope.Core.Query;

/// <summary>
/// Flattens an MDX result for Excel.
///
/// A rectangular array written in a single assignment to Range.Value2 is worth
/// a thousand cell-by-cell writes: on a wide crossjoin, it is the difference
/// between instant and endless.
/// </summary>
public static class RangeProjection
{
    /// <summary>Size of an Excel sheet (since Excel 2007).</summary>
    public const int MaxRows = 1_048_576;
    public const int MaxColumns = 16_384;

    /// <summary>
    /// Projects the result into a 0-based <c>[row, column]</c> array.
    /// Null cells stay null: an empty cell and a zero are not the same thing
    /// for downstream formulas. Numbers of any CLR type become doubles — the
    /// only numeric type Excel stores — and a cell in error stays a
    /// <see cref="CellError"/>, never text.
    /// </summary>
    public static object?[,] ToGrid(QueryResult result, bool includeHeaders)
    {
        var columns = result.Columns.Count;
        if (columns == 0) return new object?[0, 0];

        var offset = includeHeaders ? 1 : 0;
        var rows = result.Rows.Count + offset;

        // Checked before allocating: otherwise the COM error arrives after the
        // whole query, with a message nobody can act on.
        if (rows > MaxRows || columns > MaxColumns)
            throw new InvalidOperationException(
                $"Le résultat compte {rows:N0} lignes × {columns:N0} colonnes : une feuille " +
                $"Excel est limitée à {MaxRows:N0} × {MaxColumns:N0}. Restreignez la requête.");

        var grid = new object?[rows, columns];

        if (includeHeaders)
            for (var c = 0; c < columns; c++)
                grid[0, c] = result.Columns[c].Header;

        for (var r = 0; r < result.Rows.Count; r++)
        {
            var row = result.Rows[r];
            for (var c = 0; c < columns; c++)
            {
                row.TryGetValue(result.Columns[c].Field, out var value);
                grid[r + offset, c] = Normalize(value);
            }
        }

        return grid;
    }

    /// <summary>Excel-ready value: double for any number, CellError for an error.</summary>
    internal static object? Normalize(object? value) => value switch
    {
        null => null,
        string s when s == CellError.LegacyMarker => new CellError(s),
        double or string or bool or DateTime or CellError => value,
        decimal d => (double)d,
        float f => (double)f,
        int or long or short or byte or sbyte or uint or ulong or ushort
            => Convert.ToDouble(value, System.Globalization.CultureInfo.InvariantCulture),
        _ => value.ToString(),
    };
}
