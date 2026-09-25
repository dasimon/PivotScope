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

    [Fact]
    public async Task SaveAsync_MemeNomAutreHierarchie_NEcrasePas()
    {
        using var library = Library();
        CalculationDefinition Total(string parent) => new(
            "Total", "1", CalculationKind.Member, ParentHierarchy: parent);

        await library.SaveAsync(Total("[Fonds].[Fonds]"), "Ventes");
        await library.SaveAsync(Total("[Devise].[Devise]"), "Ventes");

        Assert.Equal(2, (await library.ListAsync()).Count);
    }

    [Fact]
    public async Task Migration_DepuisLaV1_ConserveLesCalculs()
    {
        // A v1 database as the previous release left it.
        using (var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={_dbPath}"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = """
                CREATE TABLE Calculation (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT, Name TEXT NOT NULL,
                    Expression TEXT NOT NULL, Kind INTEGER NOT NULL, DisplayFolder TEXT NULL,
                    NumberFormat TEXT NULL, ParentHierarchy TEXT NULL,
                    SolveOrder INTEGER NOT NULL DEFAULT 0, Cube TEXT NULL, SavedUtc TEXT NOT NULL);
                CREATE UNIQUE INDEX UX_Calculation_Name_Cube ON Calculation (Name, IFNULL(Cube, ''));
                INSERT INTO Calculation (Name, Expression, Kind, Cube, SavedUtc)
                    VALUES ('Marge', '1', 2, 'Ventes', '2026-07-27T00:00:00.0000000Z');
                PRAGMA user_version = 1;
                """;
            command.ExecuteNonQuery();
        }
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

        using var library = Library();
        await library.SaveAsync(Marge() with { Expression = "2" }, "Ventes");

        var stored = Assert.Single(await library.ListAsync());
        Assert.Equal("2", stored.Definition.Expression);
    }

    [Fact]
    public void Ouverture_DUneBasePlusRecente_EstRefusee()
    {
        using (var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={_dbPath}"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA user_version = 99;";
            command.ExecuteNonQuery();
        }
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

        var ex = Assert.Throws<InvalidOperationException>(() => Library());

        Assert.Contains("plus récente", ex.Message);
    }

    public void Dispose()
    {
        try { if (File.Exists(_dbPath)) File.Delete(_dbPath); } catch { /* leftover lock */ }
    }
}
