using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.RegularExpressions;
using CubeScope.Core.Ai;
using CubeScope.Core.Models;
using CubeScope.Core.Script;
using CubeScope.Core.Ssas;
using CubeScope.Core.State;
using Microsoft.AnalysisServices.AdomdClient;
using PivotScope.Core.Abstractions;
using PivotScope.Core.Query;

namespace PivotScope.Core.Adapters;

/// <summary>
/// Groups the SSAS session and the CubeScope services for a given
/// server/catalog pair, and exposes them behind the PivotScope abstractions.
/// This is the only place in the product that knows about CubeScope.Core.
/// </summary>
public sealed partial class CubeScopeSession
    : ICubeMetadataReader, IMdxExecutor, ILevelMemberReader, IScriptReader, IDisposable
{
    /// <summary>
    /// How long an enumerated level is trusted. A level does not change during
    /// a query, but it does at the daily processing: a fund added this morning
    /// must be findable by caption without restarting Excel.
    /// </summary>
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromMinutes(15);

    private readonly SsasSession _session;
    private readonly StateStore _store;
    private readonly MetadataService _metadata;
    private readonly ScriptService _script;
    private readonly AiService _ai;

    private readonly ConcurrentDictionary<string, (DateTime Loaded, IReadOnlyList<LevelMember> Members)> _levelCache = new();
    private readonly ConcurrentDictionary<string, (DateTime Loaded, IReadOnlyList<MemberMeta> Members)> _memberCache = new();

    private CubeScopeSession(SsasSession session, StateStore store)
    {
        _session = session;
        _store = store;
        _metadata = new MetadataService(session, store);
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

    /// <summary>
    /// First members of a hierarchy, for completion. In MDX with HEAD, NOT
    /// through CubeScope's MetadataService.GetMembersAsync: that one reads
    /// $SYSTEM.MDSCHEMA_MEMBERS and caps client-side, i.e. after walking the
    /// whole dimension — on a securities hierarchy, the connection stays
    /// locked for as long, and every other pane call waits behind it.
    /// </summary>
    public async Task<IReadOnlyList<MemberMeta>> GetMembersAsync(
        string cube, string hierarchyUniqueName, int limit = 1000, CancellationToken ct = default)
    {
        var key = $"{cube}|{hierarchyUniqueName}|{limit}";
        if (TryCached(_memberCache, key, out var cached)) return cached;

        var mdx = $"SELECT {{}} ON 0, HEAD({hierarchyUniqueName}.MEMBERS, {limit}) ON 1 FROM {Bracket(cube)}";
        var members = await ReadAxisMembersAsync(mdx, (caption, unique) => new MemberMeta(caption, unique), ct)
            .ConfigureAwait(false);

        _memberCache[key] = (DateTime.UtcNow, members);
        return members;
    }

    /// <summary>
    /// Runs a query and keeps the RAW cell values. CubeScope's QueryService
    /// returns the formatted ones ("1 234,56 €", "12,5 %"), made for a grid on
    /// screen: written to a sheet, they stay text — a SUM gives 0 — and Excel
    /// may even re-read them in its own locale. A cell in error becomes a
    /// <see cref="CellError"/>, which a sheet shows as #VALUE!.
    /// </summary>
    public Task<QueryResult> ExecuteAsync(string mdx, CancellationToken ct = default)
        => _session.WithConnectionAsync(conn =>
        {
            using var command = new AdomdCommand(mdx, conn);
            using var registration = ct.Register(() =>
            {
                try { command.Cancel(); } catch { /* already finished */ }
            });

            var watch = Stopwatch.StartNew();
            var cellSet = command.ExecuteCellSet();
            watch.Stop();
            ct.ThrowIfCancellationRequested();

            var axes = cellSet.Axes.Count;
            if (axes > 2)
                throw new NotSupportedException(
                    $"Requête à {axes} axes : une feuille n'en affiche que deux au plus.");

            var columns = axes >= 1 ? ReadAxis(cellSet.Axes[0]) : null;
            var rows = axes == 2 ? ReadAxis(cellSet.Axes[1]) : null;
            return CellSetMapper.Build(
                columns, rows, i => RawValue(cellSet.Cells[i]), cellSet.Cells.Count, watch.ElapsedMilliseconds);
        }, ct);

    /// <summary>
    /// A cell in error throws on every accessor (documented pitfall in
    /// CubeScope): its message is the only thing worth keeping.
    /// </summary>
    private static object? RawValue(Cell cell)
    {
        try { return cell.Value; }
        catch (Exception ex) { return new CellError(ex.Message); }
    }

    /// <summary>
    /// Same reading as CubeScope's CellSetMapper, including its fallback:
    /// Set.Hierarchies resolves schema objects lazily and can fail on a real
    /// cube while the positions are already there.
    /// </summary>
    private static AxisData ReadAxis(Axis axis)
    {
        var positions = axis.Positions.Cast<Position>()
            .Select(p => (IReadOnlyList<string>)[.. p.Members.Cast<Member>().Select(m => m.Caption)])
            .ToList();

        IReadOnlyList<string> hierarchies;
        try
        {
            hierarchies = [.. axis.Set.Hierarchies.Cast<Hierarchy>().Select(h => h.Caption)];
        }
        catch (Exception)
        {
            hierarchies = positions.Count > 0
                ? [.. axis.Positions[0].Members.Cast<Member>().Select(m => HierarchyFromUniqueName(m.UniqueName))]
                : [];
        }

        return new AxisData(hierarchies, positions);
    }

    [GeneratedRegex(@"\[(?:[^\]]|\]\])*\]")]
    private static partial Regex Segment();

    /// <summary>"[Dim].[Hier].&amp;[X]" → "Hier"; for a measure the 2nd segment is the member.</summary>
    private static string HierarchyFromUniqueName(string uniqueName)
    {
        var segments = Segment().Matches(uniqueName)
            .Select(m => m.Value[1..^1].Replace("]]", "]"))
            .ToList();
        if (segments.Count == 0) return uniqueName;
        if (segments[0] == "Measures") return "Measures";
        return segments.Count >= 2 ? segments[1] : segments[0];
    }

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
    /// query is for. Cached per (cube, level) for <see cref="CacheLifetime"/>.
    /// </summary>
    public async Task<IReadOnlyList<LevelMember>> GetLevelMembersAsync(
        string cube, string levelUniqueName, int limit, CancellationToken ct = default)
    {
        var key = $"{cube}|{levelUniqueName}|{limit}";
        if (TryCached(_levelCache, key, out var cached)) return cached;

        var mdx = $"SELECT {{}} ON 0, HEAD({levelUniqueName}.MEMBERS, {limit}) ON 1 FROM {Bracket(cube)}";
        var members = await ReadAxisMembersAsync(mdx, (caption, unique) => new LevelMember(caption, unique), ct)
            .ConfigureAwait(false);

        _levelCache[key] = (DateTime.UtcNow, members);
        return members;
    }

    /// <summary>Members of the rows axis of a query, cancellable on the server.</summary>
    private Task<IReadOnlyList<T>> ReadAxisMembersAsync<T>(
        string mdx, Func<string, string, T> create, CancellationToken ct)
        => _session.WithConnectionAsync(conn =>
        {
            using var command = new AdomdCommand(mdx, conn);
            using var registration = ct.Register(() =>
            {
                try { command.Cancel(); } catch { /* already finished */ }
            });

            var cellSet = command.ExecuteCellSet();
            ct.ThrowIfCancellationRequested();

            // Known pitfall: a query with a single axis has no Axes[1].
            if (cellSet.Axes.Count < 2) return (IReadOnlyList<T>)[];

            var list = new List<T>(cellSet.Axes[1].Positions.Count);
            foreach (Position position in cellSet.Axes[1].Positions)
            {
                var member = position.Members[0];
                list.Add(create(member.Caption, member.UniqueName));
            }
            return list;
        }, ct);

    private static bool TryCached<T>(
        ConcurrentDictionary<string, (DateTime Loaded, IReadOnlyList<T> Members)> cache,
        string key, out IReadOnlyList<T> members)
    {
        if (cache.TryGetValue(key, out var entry) && DateTime.UtcNow - entry.Loaded < CacheLifetime)
        {
            members = entry.Members;
            return true;
        }
        members = [];
        return false;
    }

    private static string Bracket(string name) => $"[{name.Replace("]", "]]")}]";

    public void Dispose()
    {
        _store.Dispose();
        _session.Dispose();
    }
}
