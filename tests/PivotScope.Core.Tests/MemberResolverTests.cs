using CubeScope.Core.Models;
using PivotScope.Core.Abstractions;
using PivotScope.Core.Filtering;

namespace PivotScope.Core.Tests;

public class MemberResolverTests
{
    private const string Level = "[Devise].[Devise].[Devise]";

    /// <summary>Stubbed MDX executor: this is what the IMdxExecutor boundary buys.</summary>
    private sealed class FakeExecutor : IMdxExecutor
    {
        public List<string> Queries { get; } = [];
        public Func<string, int, QueryResult>? Responder { get; set; }

        public Task<QueryResult> ExecuteAsync(string mdx, CancellationToken ct = default)
        {
            Queries.Add(mdx);
            if (Responder is null) throw new InvalidOperationException("aucune réponse configurée");
            return Task.FromResult(Responder(mdx, Queries.Count));
        }
    }

    /// <summary>Stubbed level enumerator, with a call counter.</summary>
    private sealed class FakeLevelMembers(params (string Caption, string Unique)[] members)
        : ILevelMemberReader
    {
        public int Calls { get; private set; }

        public Task<IReadOnlyList<LevelMember>> GetLevelMembersAsync(
            string cube, string levelUniqueName, int limit, CancellationToken ct = default)
        {
            Calls++;
            return Task.FromResult<IReadOnlyList<LevelMember>>(
                [.. members.Select(m => new LevelMember(m.Caption, m.Unique))]);
        }
    }

    /// <summary>A result row: one __capN column per probed key.</summary>
    private static QueryResult Captions(params string?[] captions)
    {
        var columns = captions
            .Select((_, i) => new GridColumn($"__cap{i}", $"__cap{i}", false))
            .ToList();

        var row = new Dictionary<string, object?>();
        for (var i = 0; i < captions.Length; i++) row[$"__cap{i}"] = captions[i];

        return new QueryResult(columns, [row], captions.Length, 1, 0);
    }

    [Fact]
    public async Task ResolveAsync_SondeToutesLesCles_EnUneSeuleRequete()
    {
        var exec = new FakeExecutor { Responder = (_, _) => Captions("Euro", "Dollar") };
        var resolver = new MemberResolver(exec);

        var result = await resolver.ResolveAsync("Ventes", Level, ["EUR", "USD"]);

        Assert.Single(exec.Queries);
        Assert.Contains("StrToMember", exec.Queries[0]);
        // Never a DMV: MDSCHEMA_MEMBERS does not support IN and scans the dimension.
        Assert.DoesNotContain("MDSCHEMA_MEMBERS", exec.Queries[0]);
        Assert.Equal(
            [$"{Level}.&[EUR]", $"{Level}.&[USD]"],
            result.UniqueNames);
        Assert.Empty(result.Unresolved);
    }

    [Fact]
    public async Task ResolveAsync_RapporteLesClesNonResolues_SansEchouer()
    {
        // A null caption signals a nonexistent member.
        var exec = new FakeExecutor { Responder = (_, _) => Captions("Euro", null) };
        var resolver = new MemberResolver(exec);

        var result = await resolver.ResolveAsync("Ventes", Level, ["EUR", "XXX"]);

        Assert.Equal([$"{Level}.&[EUR]"], result.UniqueNames);
        Assert.Equal(["XXX"], result.Unresolved);
    }

    [Fact]
    public async Task ResolveAsync_ReplieCleParCle_QuandLaRequeteGroupeeEchoue()
    {
        var exec = new FakeExecutor
        {
            Responder = (_, call) => call == 1
                ? throw new InvalidOperationException("référence périmée")
                : Captions("Euro"),
        };
        var resolver = new MemberResolver(exec);

        var result = await resolver.ResolveAsync("Ventes", Level, ["EUR", "USD"]);

        // 1 failed grouped query, then 1 query per key.
        Assert.Equal(3, exec.Queries.Count);
        Assert.Equal(2, result.UniqueNames.Count);
        Assert.Empty(result.Unresolved);
    }

    [Fact]
    public async Task ResolveAsync_EnRepli_IsoleLaSeuleCleFautive()
    {
        var exec = new FakeExecutor
        {
            Responder = (mdx, call) => call switch
            {
                1 => throw new InvalidOperationException("le paquet entier tombe"),
                _ => mdx.Contains("&[XXX]")
                    ? throw new InvalidOperationException("membre inconnu")
                    : Captions("Euro"),
            },
        };
        var resolver = new MemberResolver(exec);

        var result = await resolver.ResolveAsync("Ventes", Level, ["EUR", "XXX"]);

        Assert.Equal([$"{Level}.&[EUR]"], result.UniqueNames);
        Assert.Equal(["XXX"], result.Unresolved);
    }

    [Theory]
    [InlineData("EUR", "[D].[H].[L].&[EUR]")]
    [InlineData("  EUR  ", "[D].[H].[L].&[EUR]")]
    [InlineData("A&B", "[D].[H].[L].&[A&B]")]
    [InlineData("FR0000120271", "[D].[H].[L].&[FR0000120271]")]
    public void BuildUniqueName_AjouteLeSegmentDeCle_EtElague(string key, string expected)
        => Assert.Equal(expected, MemberResolver.BuildUniqueName("[D].[H].[L]", key));

    [Fact]
    public async Task ResolveAsync_IgnoreLesLignesVides_EtDedoublonne()
    {
        var exec = new FakeExecutor { Responder = (_, _) => Captions("Euro") };
        var resolver = new MemberResolver(exec);

        var result = await resolver.ResolveAsync("C", Level, ["EUR", "", "   ", "EUR", "\t"]);

        Assert.Single(result.UniqueNames);
        Assert.Single(exec.Queries);
    }

    [Fact]
    public async Task ResolveAsync_SansAucuneCle_NInterrogePasLeServeur()
    {
        var exec = new FakeExecutor();
        var resolver = new MemberResolver(exec);

        var result = await resolver.ResolveAsync("C", Level, ["", "  "]);

        Assert.Empty(result.UniqueNames);
        Assert.Empty(result.Unresolved);
        Assert.Empty(exec.Queries);
    }

    [Fact]
    public async Task ResolveAsync_DecoupeEnLots_QuandLesClesSontNombreuses()
    {
        var keys = Enumerable.Range(0, 250).Select(i => $"K{i}").ToList();
        var exec = new FakeExecutor
        {
            Responder = (mdx, _) =>
            {
                var count = mdx.Split("StrToMember").Length - 1;
                return Captions([.. Enumerable.Repeat<string?>("ok", count)]);
            },
        };
        var resolver = new MemberResolver(exec);

        var result = await resolver.ResolveAsync("C", Level, keys);

        // 250 keys, batches of 100: exactly three queries, no key lost.
        Assert.Equal(3, exec.Queries.Count);
        Assert.Equal(250, result.UniqueNames.Count);
        Assert.Empty(result.Unresolved);
    }

    // --- Fallback by caption -----------------------------------------------
    // Real case that motivated the feature: on the Ventes cube, pasting
    // "Aurore" fails by key (the key is "PRD014") even though it is the
    // caption the user has in front of them.

    [Fact]
    public async Task ResolveAsync_ReplieSurLeLibelle_QuandLaCleNExistePas()
    {
        var exec = new FakeExecutor { Responder = (_, _) => Captions((string?)null) };
        var level = new FakeLevelMembers(("Aurore", $"{Level}.&[PRD014]"));
        var resolver = new MemberResolver(exec, level);

        var result = await resolver.ResolveAsync("Ventes", Level, ["Aurore"]);

        Assert.Equal([$"{Level}.&[PRD014]"], result.UniqueNames);
        Assert.Empty(result.Unresolved);
        Assert.Empty(result.Ambiguous);
    }

    [Fact]
    public async Task ResolveAsync_LaCleGagneSurLeLibelle_EtEviteLEnumeration()
    {
        var exec = new FakeExecutor { Responder = (_, _) => Captions("Euro") };
        var level = new FakeLevelMembers(("EUR", $"{Level}.&[AUTRE]"));
        var resolver = new MemberResolver(exec, level);

        var result = await resolver.ResolveAsync("Ventes", Level, ["EUR"]);

        Assert.Equal([$"{Level}.&[EUR]"], result.UniqueNames);
        // Everything is resolved by key: the level is never enumerated.
        Assert.Equal(0, level.Calls);
    }

    [Fact]
    public async Task ResolveAsync_NEnumereLeNiveauQuUneSeuleFois()
    {
        var exec = new FakeExecutor { Responder = (_, _) => Captions(null, null, null) };
        var level = new FakeLevelMembers(
            ("Aurore", $"{Level}.&[PRD014]"),
            ("BOREAL", $"{Level}.&[PRD007]"));
        var resolver = new MemberResolver(exec, level);

        var result = await resolver.ResolveAsync(
            "Ventes", Level, ["Aurore", "BOREAL", "INCONNU"]);

        Assert.Equal(1, level.Calls);
        Assert.Equal(2, result.UniqueNames.Count);
        Assert.Equal(["INCONNU"], result.Unresolved);
    }

    [Fact]
    public async Task ResolveAsync_LibelleInsensibleALaCasseEtAuxEspaces()
    {
        var exec = new FakeExecutor { Responder = (_, _) => Captions((string?)null) };
        var level = new FakeLevelMembers(("Aurore", $"{Level}.&[PRD014]"));
        var resolver = new MemberResolver(exec, level);

        var result = await resolver.ResolveAsync("Ventes", Level, ["  aurore  "]);

        Assert.Equal([$"{Level}.&[PRD014]"], result.UniqueNames);
    }

    [Fact]
    public async Task ResolveAsync_LibelleAmbigu_NEstPasResoluAuHasard()
    {
        var exec = new FakeExecutor { Responder = (_, _) => Captions((string?)null) };
        var level = new FakeLevelMembers(
            ("Aurore", $"{Level}.&[PRD014]"),
            ("Aurore", $"{Level}.&[PRD099]"));
        var resolver = new MemberResolver(exec, level);

        var result = await resolver.ResolveAsync("Ventes", Level, ["Aurore"]);

        // Two members share this caption: picking one would be a wrong filter.
        Assert.Empty(result.UniqueNames);
        Assert.Empty(result.Unresolved);
        Assert.Equal(["Aurore"], result.Ambiguous);
    }

    [Fact]
    public async Task ResolveAsync_NomUniqueComplet_EstReprisSansAppelServeur()
    {
        var exec = new FakeExecutor();
        var resolver = new MemberResolver(exec);

        var result = await resolver.ResolveAsync(
            "Ventes", Level, [$"{Level}.&[PRD014]"]);

        Assert.Equal([$"{Level}.&[PRD014]"], result.UniqueNames);
        Assert.Empty(exec.Queries);
    }

    [Fact]
    public async Task ResolveAsync_EchecDeLEnumeration_NEffacePasCeQuiEstDejaResolu()
    {
        // EUR is found by key, Aurore is not: the level enumeration IS attempted
        // (and fails), which is the case this test is about.
        var exec = new FakeExecutor { Responder = (_, _) => Captions("Euro", null) };
        var levels = new ThrowingLevelMembers();
        var resolver = new MemberResolver(exec, levels);

        var result = await resolver.ResolveAsync("Ventes", Level, ["EUR", "Aurore"]);

        Assert.Equal(1, levels.Calls);
        Assert.Equal([$"{Level}.&[EUR]"], result.UniqueNames);
        Assert.Equal(["Aurore"], result.Unresolved);
    }

    private sealed class ThrowingLevelMembers : ILevelMemberReader
    {
        public int Calls { get; private set; }

        public Task<IReadOnlyList<LevelMember>> GetLevelMembersAsync(
            string cube, string levelUniqueName, int limit, CancellationToken ct = default)
        {
            Calls++;
            throw new InvalidOperationException("niveau illisible");
        }
    }

    [Fact]
    public static void ParseKeys_CollageMultiligne_DecoupeSurLignesEtTabulations()
    {
        var parsed = MemberResolver.ParseKeys("EUR\r\nUSD\nGBP\tCHF");

        Assert.Equal(["EUR", "USD", "GBP", "CHF"], parsed);
    }

    [Fact]
    public static void ParseKeys_CollageMultiligne_GardeLesVirgulesDesLibelles()
    {
        // "Actions, Europe" is one caption: splitting it could resolve "Europe"
        // to another member and silently filter on the wrong figure.
        var parsed = MemberResolver.ParseKeys("Actions, Europe\nTaux; court terme");

        Assert.Equal(["Actions, Europe", "Taux; court terme"], parsed);
    }

    [Fact]
    public static void ParseKeys_SaisieSurUneLigne_DecoupeSurVirgulesEtPointsVirgules()
    {
        var parsed = MemberResolver.ParseKeys("EUR, USD;GBP");

        Assert.Equal(["EUR", "USD", "GBP"], parsed);
    }

    [Fact]
    public static void BuildUniqueName_DoubleLesCrochetsFermants()
        => Assert.Equal("[D].[H].[L].&[A]]B]", MemberResolver.BuildUniqueName("[D].[H].[L]", "A]B"));

    [Fact]
    public async Task ResolveAsync_EchappeLesApostrophesEtLeNomDuCube()
    {
        var exec = new FakeExecutor { Responder = (_, _) => Captions("x") };
        var resolver = new MemberResolver(exec);

        await resolver.ResolveAsync("Cube]X", Level, ["L'Oréal"]);

        Assert.Contains("&[L''Oréal]", exec.Queries[0]);
        Assert.EndsWith("FROM [Cube]]X]", exec.Queries[0]);
    }

    [Theory]
    [InlineData("#ERREUR")]
    public async Task ResolveAsync_CelluleEnErreur_NEstPasUnMembreTrouve(string marker)
    {
        var exec = new FakeExecutor { Responder = (_, _) => Captions(marker) };
        var resolver = new MemberResolver(exec);

        var result = await resolver.ResolveAsync("C", Level, ["[EUR]x"]);

        Assert.Empty(result.UniqueNames);
        Assert.Equal(["[EUR]x"], result.Unresolved);
    }

    [Fact]
    public async Task ResolveAsync_CelluleCellError_NEstPasUnMembreTrouve()
    {
        var exec = new FakeExecutor
        {
            Responder = (_, _) => new QueryResult(
                [new GridColumn("v0", "__cap0", false)],
                [new Dictionary<string, object?> { ["v0"] = new Query.CellError("type de clé") }],
                1, 1, 0),
        };
        var resolver = new MemberResolver(exec);

        var result = await resolver.ResolveAsync("C", Level, ["Aurore"]);

        Assert.Empty(result.UniqueNames);
        Assert.Equal(["Aurore"], result.Unresolved);
    }

    [Fact]
    public async Task ResolveAsync_NomUniqueDUneAutreHierarchie_EstRefuse()
    {
        var exec = new FakeExecutor();
        var resolver = new MemberResolver(exec);

        var result = await resolver.ResolveAsync(
            "C", Level, ["[Fonds].[Fonds].&[F1]", "[Devise].[Devise].&[EUR]"]);

        Assert.Equal(["[Devise].[Devise].&[EUR]"], result.UniqueNames);
        Assert.Equal(["[Fonds].[Fonds].&[F1]"], result.Unresolved);
    }

    [Fact]
    public async Task ResolveAsync_CleEtLibelleDuMemeMembre_NeSontPasDoubles()
    {
        // "EUR" by key, "Euro" by caption: both designate the same member.
        var exec = new FakeExecutor { Responder = (_, _) => Captions("Euro", null) };
        var level = new FakeLevelMembers(("Euro", $"{Level}.&[EUR]"));
        var resolver = new MemberResolver(exec, level);

        var result = await resolver.ResolveAsync("C", Level, ["EUR", "Euro"]);

        Assert.Equal([$"{Level}.&[EUR]"], result.UniqueNames);
    }

    [Fact]
    public async Task ResolveAsync_NiveauTronque_EstSignale()
    {
        var exec = new FakeExecutor { Responder = (_, _) => Captions((string?)null) };
        var many = Enumerable.Range(0, MemberResolver.LevelMemberLimit + 1)
            .Select(i => ($"M{i}", $"{Level}.&[{i}]"))
            .ToArray();
        var resolver = new MemberResolver(exec, new FakeLevelMembers(many));

        var result = await resolver.ResolveAsync("C", Level, ["M3"]);

        Assert.True(result.LevelTruncated);
        Assert.Equal([$"{Level}.&[3]"], result.UniqueNames);
    }

    [Fact]
    public async Task ResolveAsync_QuandToutEchoue_RemonteLErreurDuServeur()
    {
        // Every query fails: the keys are not the problem, the server is.
        var exec = new FakeExecutor { Responder = (_, _) => throw new InvalidOperationException("cube absent") };
        var resolver = new MemberResolver(exec);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => resolver.ResolveAsync("C", Level, ["EUR", "USD"]));

        Assert.Contains("cube absent", ex.Message);
    }

    [Fact]
    public async Task ResolveAsync_Annulation_NEstPasTransformeeEnClesNonResolues()
    {
        var exec = new FakeExecutor { Responder = (_, _) => throw new OperationCanceledException() };
        var resolver = new MemberResolver(exec);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => resolver.ResolveAsync("C", Level, ["EUR", "USD"]));
        Assert.Single(exec.Queries);
    }
}
