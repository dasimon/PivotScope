using CubeScope.Core.Models;
using PivotScope.Core.Abstractions;
using PivotScope.Core.Filtering;

namespace PivotScope.Core.Tests;

public class MemberResolverTests
{
    private const string Level = "[Devise].[Devise].[Devise]";

    /// <summary>Exécuteur MDX bouchonné : c'est ce que la frontière IMdxExecutor achète.</summary>
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

    /// <summary>Énumérateur de niveau bouchonné, avec compteur d'appels.</summary>
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

    /// <summary>Une ligne de résultat : une colonne __capN par clé sondée.</summary>
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
        // Jamais de DMV : MDSCHEMA_MEMBERS ne supporte pas IN et scanne la dimension.
        Assert.DoesNotContain("MDSCHEMA_MEMBERS", exec.Queries[0]);
        Assert.Equal(
            [$"{Level}.&[EUR]", $"{Level}.&[USD]"],
            result.UniqueNames);
        Assert.Empty(result.Unresolved);
    }

    [Fact]
    public async Task ResolveAsync_RapporteLesClesNonResolues_SansEchouer()
    {
        // Une caption nulle signale un membre inexistant.
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

        // 1 requête groupée en échec, puis 1 requête par clé.
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

        // 250 clés > taille de lot : plusieurs requêtes, aucune clé perdue.
        Assert.True(exec.Queries.Count > 1);
        Assert.Equal(250, result.UniqueNames.Count);
        Assert.Empty(result.Unresolved);
    }

    // --- Repli par libellé -------------------------------------------------
    // Cas réel qui a motivé la fonction : sur le cube Ventes, coller
    // « Aurore » échoue par clé (la clé est « PRD014 ») alors que c'est le
    // libellé qu'a l'utilisateur sous les yeux.

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
        // Tout est résolu par clé : le niveau n'est jamais énuméré.
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

        // Deux membres portent ce libellé : choisir serait un filtre faux.
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
        var exec = new FakeExecutor { Responder = (mdx, _) => mdx.Contains("&[EUR]")
            ? Captions("Euro")
            : Captions((string?)null) };

        var resolver = new MemberResolver(exec, new ThrowingLevelMembers());

        var result = await resolver.ResolveAsync("Ventes", Level, ["EUR"]);

        Assert.Single(result.UniqueNames);
    }

    private sealed class ThrowingLevelMembers : ILevelMemberReader
    {
        public Task<IReadOnlyList<LevelMember>> GetLevelMembersAsync(
            string cube, string levelUniqueName, int limit, CancellationToken ct = default)
            => throw new InvalidOperationException("niveau illisible");
    }

    [Fact]
    public static void ParseKeys_AccepteLesSeparateursCourantsDUnCollage()
    {
        var parsed = MemberResolver.ParseKeys("EUR\r\nUSD\nGBP\tCHF; JPY,SEK");

        Assert.Equal(["EUR", "USD", "GBP", "CHF", "JPY", "SEK"], parsed);
    }
}
