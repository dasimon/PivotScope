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
/// Groups the SSAS session and the CubeScope services for a given
/// server/catalog pair, and exposes them behind the PivotScope abstractions.
/// This is the only place in the product that knows about CubeScope.Core.
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
    /// Opens a session on the server/catalog pair read from the workbook
    /// connection. Windows integrated security: no credential is handled.
    /// </summary>
    /// <param name="statePath">
    /// SQLite database owned by PivotScope. CubeScope's is not shared: two
    /// processes writing the same database is a problem we have no need to have.
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
    /// The cube's MDX Script, read through AMO. Requires read access to the
    /// definition metadata; the caller treats a failure as a note, not as an
    /// outage.
    /// </summary>
    public Task<CubeScript> GetScriptAsync(string cube, CancellationToken ct = default)
        => _script.GetScriptAsync(cube, ct: ct);

    /// <summary>AI is optional: without a key, the UI degrades instead of failing.</summary>
    public static bool IsAiConfigured => AiService.IsConfigured;

    public Task<string> RunAiAsync(
        AiAction action, string prompt, string lang = "fr", CancellationToken ct = default)
        => _ai.RunAsync(action, prompt, lang, ct);

    /// <summary>
    /// Enumerates the members of a level with their caption and unique name.
    /// Goes through the CellSet rather than QueryResult: the latter is flattened
    /// for a grid and loses the unique names, which are precisely what the
    /// query is for. The result is cached per (cube, level) — a level does not
    /// change during a session.
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

            // Known pitfall: a query with a single axis has no Axes[1].
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
