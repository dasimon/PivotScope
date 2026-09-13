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
    /// <summary>
    /// Projects the result into a 0-based <c>[row, column]</c> array.
    /// Null cells stay null: an empty cell and a zero are not the same thing
    /// for downstream formulas.
    /// </summary>
    public static object?[,] ToGrid(QueryResult result, bool includeHeaders)
    {
        var columns = result.Columns.Count;
        if (columns == 0) return new object?[0, 0];

        var offset = includeHeaders ? 1 : 0;
        var grid = new object?[result.Rows.Count + offset, columns];

        if (includeHeaders)
            for (var c = 0; c < columns; c++)
                grid[0, c] = result.Columns[c].Header;

        for (var r = 0; r < result.Rows.Count; r++)
        {
            var row = result.Rows[r];
            for (var c = 0; c < columns; c++)
            {
                row.TryGetValue(result.Columns[c].Field, out var value);
                grid[r + offset, c] = value;
            }
        }

        return grid;
    }
}
