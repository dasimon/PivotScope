using System.Collections.Concurrent;
using CubeScope.Core.Ai;
using CubeScope.Core.Models;
using CubeScope.Core.Script;
using CubeScope.Core.Ssas;
using CubeScope.Core.State;
using Microsoft.AnalysisServices.AdomdClient;
using PivotScope.Core.Abstractions;

namespace PivotScope.Core.Adapters;

/// <summary>
/// Regroupe la session SSAS et les services CubeScope pour un couple
/// serveur/catalogue donné, et les expose derrière les abstractions PivotScope.
/// C'est le seul endroit du produit qui connaît CubeScope.Core.
/// </summary>
public sealed class CubeScopeSession
    : ICubeMetadataReader, IMdxExecutor, ILevelMemberReader, IScriptReader, IDisposable
{
    private readonly SsasSession _session;
    private readonly StateStore _store;
    private readonly MetadataService _metadata;
    private readonly QueryService _query;
    private readonly ScriptService _script;
    private readonly AiService _ai;

    private readonly ConcurrentDictionary<string, IReadOnlyList<LevelMember>> _levelCache = new();

    private CubeScopeSession(SsasSession session, StateStore store)
    {
        _session = session;
        _store = store;
        _metadata = new MetadataService(session, store);
        _query = new QueryService(session);
        _script = new ScriptService(session);
        _ai = new AiService(_metadata, session);
    }

    public string? Server => _session.Server;
    public string? Catalog => _session.Catalog;

    /// <summary>
    /// Ouvre une session sur le couple serveur/catalogue lu dans la connexion du
    /// classeur. Sécurité intégrée Windows : aucun credential n'est manipulé.
    /// </summary>
    /// <param name="statePath">
    /// Base SQLite propre à PivotScope. On ne partage pas celle de CubeScope :
    /// deux process écrivant la même base est un problème qu'on n'a pas besoin d'avoir.
    /// </param>
    public static async Task<CubeScopeSession> ConnectAsync(
        string server, string catalog, string? statePath = null, CancellationToken ct = default)
    {
        var session = new SsasSession();
        StateStore? store = null;
        try
        {
            await session.ConnectAsync(server, ct: ct).ConfigureAwait(false);
            await session.SetCatalogAsync(catalog, ct).ConfigureAwait(false);
            store = new StateStore(statePath ?? DefaultStatePath);
            return new CubeScopeSession(session, store);
        }
        catch
        {
            store?.Dispose();
            session.Dispose();
            throw;
        }
    }

    public static string DefaultStatePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "PivotScope", "state.db");

    public Task<CubeMeta> GetCubeMetaAsync(string cube, CancellationToken ct = default)
        => _metadata.GetCubeMetaAsync(cube, ct: ct);

    public Task<IReadOnlyList<MemberMeta>> GetMembersAsync(
        string cube, string hierarchyUniqueName, int limit = 1000, CancellationToken ct = default)
        => _metadata.GetMembersAsync(cube, hierarchyUniqueName, limit, ct);

    public Task<QueryResult> ExecuteAsync(string mdx, CancellationToken ct = default)
        => _query.ExecuteAsync(mdx, ct);

    /// <summary>
    /// Le MDX Script du cube, lu par AMO. Exige des droits de lecture des
    /// métadonnées de définition ; l'appelant traite l'échec comme une note,
    /// pas comme une panne.
    /// </summary>
    public Task<CubeScript> GetScriptAsync(string cube, CancellationToken ct = default)
        => _script.GetScriptAsync(cube, ct: ct);

    /// <summary>L'IA est optionnelle : sans clé, l'interface se dégrade au lieu d'échouer.</summary>
    public static bool IsAiConfigured => AiService.IsConfigured;

    public Task<string> RunAiAsync(
        AiAction action, string prompt, string lang = "fr", CancellationToken ct = default)
        => _ai.RunAsync(action, prompt, lang, ct);

    /// <summary>
    /// Énumère les membres d'un niveau avec leur libellé et leur nom unique.
    /// Passe par le CellSet plutôt que par QueryResult : ce dernier est aplati
    /// pour une grille et perd les noms uniques, qui sont justement l'objet de
    /// la requête. Résultat mis en cache par (cube, niveau) — un niveau ne
    /// change pas en cours de session.
    /// </summary>
    public async Task<IReadOnlyList<LevelMember>> GetLevelMembersAsync(
        string cube, string levelUniqueName, int limit, CancellationToken ct = default)
    {
        var key = $"{_session.Server}|{_session.Catalog}|{cube}|{levelUniqueName}|{limit}";
        if (_levelCache.TryGetValue(key, out var cached)) return cached;

        var mdx = $"SELECT {{}} ON 0, HEAD({levelUniqueName}.MEMBERS, {limit}) ON 1 FROM [{cube}]";

        var members = await _session.WithConnectionAsync(conn =>
        {
            using var command = new AdomdCommand(mdx, conn);
            var cellSet = command.ExecuteCellSet();

            // Piège connu : une requête à un seul axe n'a pas d'Axes[1].
            if (cellSet.Axes.Count < 2) return (IReadOnlyList<LevelMember>)[];

            var list = new List<LevelMember>(cellSet.Axes[1].Positions.Count);
            foreach (Position position in cellSet.Axes[1].Positions)
            {
                var member = position.Members[0];
                list.Add(new LevelMember(member.Caption, member.UniqueName));
            }
            return list;
        }, ct).ConfigureAwait(false);

        _levelCache[key] = members;
        return members;
    }

    public void Dispose()
    {
        _store.Dispose();
        _session.Dispose();
    }
}
