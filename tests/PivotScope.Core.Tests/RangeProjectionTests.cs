using CubeScope.Core.Models;
using PivotScope.Core.Query;

namespace PivotScope.Core.Tests;

public class RangeProjectionTests
{
    private static QueryResult Result() => new(
        [new GridColumn("c0", "Devise", true), new GridColumn("c1", "VL", false)],
        [
            new Dictionary<string, object?> { ["c0"] = "EUR", ["c1"] = 1.5d },
            new Dictionary<string, object?> { ["c0"] = "USD", ["c1"] = null },
        ],
        2, 2, 12);

    [Fact]
    public void ToGrid_AvecEnTetes_LesPlaceSurLaPremiereLigne()
    {
        var grid = RangeProjection.ToGrid(Result(), includeHeaders: true);

        Assert.Equal(3, grid.GetLength(0));
        Assert.Equal(2, grid.GetLength(1));
        Assert.Equal("Devise", grid[0, 0]);
        Assert.Equal("VL", grid[0, 1]);
        Assert.Equal("EUR", grid[1, 0]);
        Assert.Equal(1.5d, grid[1, 1]);
    }

    [Fact]
    public void ToGrid_SansEnTetes_CommenceAuxDonnees()
    {
        var grid = RangeProjection.ToGrid(Result(), includeHeaders: false);

        Assert.Equal(2, grid.GetLength(0));
        Assert.Equal("EUR", grid[0, 0]);
    }

    [Fact]
    public void ToGrid_ConserveLesNull_PlutotQueDesChainesVides()
    {
        // Une cellule vide et un zéro ne veulent pas dire la même chose :
        // écrire "" casserait les formules Excel en aval.
        var grid = RangeProjection.ToGrid(Result(), includeHeaders: false);

        Assert.Null(grid[1, 1]);
    }

    [Fact]
    public void ToGrid_ResultatVide_RendLesEnTetesSeules()
    {
        var empty = new QueryResult([new GridColumn("c0", "Devise", true)], [], 0, 1, 0);

        var grid = RangeProjection.ToGrid(empty, includeHeaders: true);

        Assert.Equal(1, grid.GetLength(0));
        Assert.Equal("Devise", grid[0, 0]);
    }

    [Fact]
    public void ToGrid_AucuneColonne_RendUneGrilleVide()
    {
        var nothing = new QueryResult([], [], 0, 0, 0);

        var grid = RangeProjection.ToGrid(nothing, includeHeaders: true);

        Assert.Empty(grid);
    }

    [Fact]
    public void ToGrid_ColonneAbsenteDUneLigne_DonneUneCelluleVide()
    {
        // Le mapping du CellSet peut ne pas alimenter toutes les colonnes.
        var partial = new QueryResult(
            [new GridColumn("c0", "A", true), new GridColumn("c1", "B", false)],
            [new Dictionary<string, object?> { ["c0"] = "x" }],
            1, 2, 0);

        var grid = RangeProjection.ToGrid(partial, includeHeaders: false);

        Assert.Equal("x", grid[0, 0]);
        Assert.Null(grid[0, 1]);
    }
}
