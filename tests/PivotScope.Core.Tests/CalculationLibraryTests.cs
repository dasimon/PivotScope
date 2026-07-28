using PivotScope.Core.Calculations;

namespace PivotScope.Core.Tests;

public sealed class CalculationLibraryTests : IDisposable
{
    private readonly string _dbPath =
        Path.Combine(Path.GetTempPath(), $"pivotscope-tests-{Guid.NewGuid():N}.db");

    private CalculationLibrary Library() => new(_dbPath);

    private static CalculationDefinition Marge() => new(
        "Marge", "[Measures].[A] - [Measures].[B]", CalculationKind.Measure,
        DisplayFolder: "Rentabilité", SolveOrder: 5);

    [Fact]
    public async Task SaveAsync_CreeLeSchemaEtRendUnIdentifiant()
    {
        using var library = Library();

        var id = await library.SaveAsync(Marge(), "Ventes");

        Assert.True(id > 0);
    }

    [Fact]
    public async Task ListAsync_RestitueTousLesChamps()
    {
        using var library = Library();
        await library.SaveAsync(Marge(), "Ventes");

        var stored = Assert.Single(await library.ListAsync());

        Assert.Equal("Marge", stored.Definition.Name);
        Assert.Equal("[Measures].[A] - [Measures].[B]", stored.Definition.Expression);
        Assert.Equal(CalculationKind.Measure, stored.Definition.Kind);
        Assert.Equal("Rentabilité", stored.Definition.DisplayFolder);
        Assert.Equal(5, stored.Definition.SolveOrder);
        Assert.Equal("Ventes", stored.Cube);
    }

    [Fact]
    public async Task SaveAsync_ConserveLeFormatDUnMembre()
    {
        using var library = Library();
        var member = new CalculationDefinition(
            "Zone euro", "1", CalculationKind.Member,
            NumberFormat: "#,##0.00", ParentHierarchy: "[Devise].[Devise]");

        await library.SaveAsync(member, null);

        var stored = Assert.Single(await library.ListAsync());
        Assert.Equal("#,##0.00", stored.Definition.NumberFormat);
        Assert.Equal("[Devise].[Devise]", stored.Definition.ParentHierarchy);
        Assert.Null(stored.Cube);
    }

    [Fact]
    public async Task SaveAsync_MemeNomMemeCube_MetAJourAuLieuDeDupliquer()
    {
        using var library = Library();
        var first = await library.SaveAsync(Marge(), "Ventes");

        var second = await library.SaveAsync(
            Marge() with { Expression = "42" }, "Ventes");

        Assert.Equal(first, second);
        var stored = Assert.Single(await library.ListAsync());
        Assert.Equal("42", stored.Definition.Expression);
    }

    [Fact]
    public async Task SaveAsync_MemeNomAutreCube_CreeUneSecondeEntree()
    {
        using var library = Library();
        await library.SaveAsync(Marge(), "Ventes");

        await library.SaveAsync(Marge(), "Analytics");

        Assert.Equal(2, (await library.ListAsync()).Count);
    }

    [Fact]
    public async Task DeleteAsync_SurUnIdentifiantAbsent_NeLevePas()
    {
        using var library = Library();

        await library.DeleteAsync(4242);

        Assert.Empty(await library.ListAsync());
    }

    [Fact]
    public async Task DeleteAsync_RetireLEntree()
    {
        using var library = Library();
        var id = await library.SaveAsync(Marge(), "Ventes");

        await library.DeleteAsync(id);

        Assert.Empty(await library.ListAsync());
    }

    [Fact]
    public async Task ListAsync_UneBaseNeuve_EstVideSansLever()
    {
        using var library = Library();

        Assert.Empty(await library.ListAsync());
    }

    [Fact]
    public async Task Library_SurvitAUneReouverture()
    {
        using (var first = Library()) await first.SaveAsync(Marge(), "Ventes");

        using var second = Library();

        Assert.Single(await second.ListAsync());
    }

    public void Dispose()
    {
        try { if (File.Exists(_dbPath)) File.Delete(_dbPath); } catch { /* verrou résiduel */ }
    }
}
