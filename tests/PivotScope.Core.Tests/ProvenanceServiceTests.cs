using CubeScope.Core.Models;
using PivotScope.Core.Abstractions;
using PivotScope.Core.Provenance;

namespace PivotScope.Core.Tests;

public class ProvenanceServiceTests
{
    private const string Cube = "Ventes";

    private sealed class FakeScripts(CubeScript? script, Exception? failure = null) : IScriptReader
    {
        public Task<CubeScript> GetScriptAsync(string cube, CancellationToken ct = default)
            => failure is not null
                ? Task.FromException<CubeScript>(failure)
                : Task.FromResult(script!);
    }

    private sealed class FakeMetadata(CubeMeta meta) : ICubeMetadataReader
    {
        public Task<CubeMeta> GetCubeMetaAsync(string cube, CancellationToken ct = default)
            => Task.FromResult(meta);

        public Task<IReadOnlyList<MemberMeta>> GetMembersAsync(
            string cube, string hierarchyUniqueName, int limit = 1000,
            CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<MemberMeta>>([]);
    }

    private static CubeMeta Meta() => new(Cube, [], []);

    private static CubeScript ScriptWith(params ScriptCommand[] commands)
        => new(Cube, string.Join("\n", commands.Select(c => c.Expression)), commands);

    private static ProvenanceService Service(CubeScript? script, Exception? failure = null)
        => new(new FakeScripts(script, failure), new FakeMetadata(Meta()));

    [Fact]
    public async Task DescribeAsync_MesureCalculee_RendExpressionEtLigne()
    {
        var script = ScriptWith(new ScriptCommand(
            "CalculatedMember", "[Measures].[Marge]",
            "CREATE MEMBER [Measures].[Marge] AS [Measures].[A] - [Measures].[B];", 42));

        var result = await Service(script).DescribeAsync(
            Cube, "([Measures].[Marge],[Devise].[Devise].&[EUR])");

        Assert.Equal("[Measures].[Marge]", result.Measure);
        Assert.Contains("[Measures].[A] - [Measures].[B]", result.Expression);
        Assert.Equal(42, result.StartLine);
        Assert.NotNull(result.Dependencies);
        Assert.Null(result.Note);
    }

    [Fact]
    public async Task DescribeAsync_MesurePhysique_LeDitSansErreur()
    {
        // Absente du script : ce n'est pas une panne, c'est la réponse.
        var result = await Service(ScriptWith()).DescribeAsync(
            Cube, "([Measures].[Chiffre d'affaires])");

        Assert.Equal("[Measures].[Chiffre d'affaires]", result.Measure);
        Assert.Null(result.Expression);
        Assert.Null(result.Dependencies);
        Assert.NotNull(result.Note);
        Assert.Contains("physique", result.Note, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DescribeAsync_TupleSansMesure_RendLesCoordonneesSeules()
    {
        var result = await Service(ScriptWith()).DescribeAsync(
            Cube, "([Devise].[Devise].&[EUR])");

        Assert.Null(result.Measure);
        Assert.Equal(["[Devise].[Devise].&[EUR]"], result.Coordinates);
        Assert.NotNull(result.Note);
    }

    [Fact]
    public async Task DescribeAsync_ScriptIllisible_RendUneProvenancePartielle()
    {
        // Le tuple reste affichable même si le script est hors de portée
        // (droits AMO manquants, par exemple).
        var result = await Service(null, new InvalidOperationException("accès refusé"))
            .DescribeAsync(Cube, "([Measures].[Marge])");

        Assert.Equal("[Measures].[Marge]", result.Measure);
        Assert.Null(result.Expression);
        Assert.NotNull(result.Note);
        Assert.Contains("accès refusé", result.Note);
    }

    [Fact]
    public async Task DescribeAsync_ToleLAbsenceDuPrefixeMeasures_DansLeScript()
    {
        // Selon les cubes, la commande peut être nommée avec ou sans préfixe.
        var script = ScriptWith(new ScriptCommand(
            "CalculatedMember", "[Marge]", "CREATE MEMBER [Marge] AS 1;", 7));

        var result = await Service(script).DescribeAsync(Cube, "([Measures].[Marge])");

        Assert.NotNull(result.Expression);
        Assert.Equal(7, result.StartLine);
    }

    [Fact]
    public async Task DescribeAsync_ConserveLeTupleBrut()
    {
        const string tuple = "([Measures].[Marge],[Devise].[Devise].&[EUR])";

        var result = await Service(ScriptWith()).DescribeAsync(Cube, tuple);

        Assert.Equal(tuple, result.Tuple);
    }
}
