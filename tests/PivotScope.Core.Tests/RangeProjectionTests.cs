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
        // An empty cell and a zero do not mean the same thing:
        // writing "" would break downstream Excel formulas.
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
    public void ToGrid_ConvertitTousLesNombresEnDouble()
    {
        var mixed = new QueryResult(
            [new GridColumn("a", "A", false), new GridColumn("b", "B", false), new GridColumn("c", "C", false)],
            [new Dictionary<string, object?> { ["a"] = 1234.56m, ["b"] = 42, ["c"] = 7L }],
            3, 1, 0);

        var grid = RangeProjection.ToGrid(mixed, includeHeaders: false);

        Assert.Equal(1234.56d, grid[0, 0]);
        Assert.Equal(42d, grid[0, 1]);
        Assert.Equal(7d, grid[0, 2]);
    }

    [Fact]
    public void ToGrid_CelluleEnErreur_ResteUneErreur_PasDuTexte()
    {
        var withErrors = new QueryResult(
            [new GridColumn("a", "A", false), new GridColumn("b", "B", false)],
            [new Dictionary<string, object?> { ["a"] = "#ERREUR", ["b"] = new CellError("division") }],
            2, 1, 0);

        var grid = RangeProjection.ToGrid(withErrors, includeHeaders: false);

        Assert.IsType<CellError>(grid[0, 0]);
        Assert.Equal(new CellError("division"), grid[0, 1]);
    }

    [Fact]
    public void ToGrid_TropDeColonnesPourUneFeuille_EchoueAvantDAllouer()
    {
        var columns = Enumerable.Range(0, RangeProjection.MaxColumns + 1)
            .Select(i => new GridColumn($"c{i}", $"C{i}", false))
            .ToList();
        var wide = new QueryResult(columns, [], 0, 1, 0);

        var ex = Assert.Throws<InvalidOperationException>(
            () => RangeProjection.ToGrid(wide, includeHeaders: true));

        Assert.Contains("16", ex.Message);
    }

    [Fact]
    public void ToGrid_ColonneAbsenteDUneLigne_DonneUneCelluleVide()
    {
        // The CellSet mapping may not populate every column.
        var partial = new QueryResult(
            [new GridColumn("c0", "A", true), new GridColumn("c1", "B", false)],
            [new Dictionary<string, object?> { ["c0"] = "x" }],
            1, 2, 0);

        var grid = RangeProjection.ToGrid(partial, includeHeaders: false);

        Assert.Equal("x", grid[0, 0]);
        Assert.Null(grid[0, 1]);
    }
}
