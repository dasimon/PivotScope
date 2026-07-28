using CubeScope.Core.Models;

namespace PivotScope.Core.Query;

/// <summary>
/// Met un résultat MDX à plat pour Excel.
///
/// Un tableau rectangulaire écrit en une seule affectation à Range.Value2 vaut
/// mille écritures cellule par cellule : sur un crossjoin large, c'est la
/// différence entre instantané et interminable.
/// </summary>
public static class RangeProjection
{
    /// <summary>
    /// Projette le résultat en tableau <c>[ligne, colonne]</c> 0-based.
    /// Les cellules nulles restent nulles : une cellule vide et un zéro ne sont
    /// pas la même chose pour les formules en aval.
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
