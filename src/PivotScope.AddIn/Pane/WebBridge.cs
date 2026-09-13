using System.Diagnostics;
using System.Text.Json;
using PivotScope.AddIn.Diagnostics;
using PivotScope.AddIn.Interop;
using CubeScope.Core.Ai;
using PivotScope.Core.Adapters;
using PivotScope.Core.Ai;
using PivotScope.Core.Bridge;
using PivotScope.Core.Calculations;
using PivotScope.Core.Filtering;
using PivotScope.Core.Models;
using PivotScope.Core.Provenance;
using PivotScope.Core.Query;

namespace PivotScope.AddIn.Pane;

/// <summary>
/// Registers the methods exposed to the SPA and relays the responses.
/// Strict split: whatever touches Excel goes through ExcelThread, whatever
/// queries SSAS stays off the UI thread.
/// </summary>
internal sealed class WebBridge : IDisposable
{
    private readonly BridgeRouter _router = new();
    private readonly PaneControl _control;
    private readonly SessionProvider _sessions = new();

    /// <summary>
    /// Last connection seen on an OLAP PivotTable. The connection is session
    /// data, not moment-to-moment data: writing a query from the active cell
    /// precisely means having left the PivotTable, and requiring a PivotTable under the
    /// cursor at that point would make the feature impossible to use.
    /// </summary>
    private (string Server, string Catalog, string? Cube)? _lastConnection;

    /// <summary>
    /// Query in flight, if any. Cancelling the token triggers
    /// AdomdCommand.Cancel() in QueryService: the server really stops
    /// working, we do not merely give up waiting.
    /// </summary>
    private CancellationTokenSource? _runningQuery;

    /// <summary>
    /// Calculation library, opened on first use only:
    /// add-in startup must touch neither disk nor network.
    /// </summary>
    private readonly Lazy<CalculationLibrary> _library = new(() => new CalculationLibrary());

    /// <summary>
    /// Tracks the active PivotTable and pushes an event to the SPA. Without it, the pane
    /// shows the state as of the last click on "Actualiser": potentially wrong,
    /// and silently so.
    /// </summary>
    private readonly PivotWatcher _watcher;

    internal WebBridge(PaneControl control)
    {
        _control = control;
        _control.MessageReceived += OnMessage;
        _watcher = new PivotWatcher(NotifyPivotChanged);

        _router.Register("pivot.context", async (_, _) =>
        {
            var context = await ExcelThread.RunAsync(PivotTableInspector.Capture);
            Remember(context);
            return context;
        });

        _router.Register("cube.meta", async (p, ct) =>
        {
            var context = await ExcelThread.RunAsync(PivotTableInspector.Capture);
            var (server, catalog, cube) = RequireCube(context, p);
            var session = await _sessions.GetAsync(server, catalog, ct);
            return await session.GetCubeMetaAsync(cube, ct);
        });

        _router.Register("cube.members", async (p, ct) =>
        {
            var context = await ExcelThread.RunAsync(PivotTableInspector.Capture);
            var (server, catalog, cube) = RequireCube(context, p);
            var hierarchy = Required(p, "hierarchy");
            var session = await _sessions.GetAsync(server, catalog, ct);
            return await session.GetMembersAsync(cube, hierarchy, ct: ct);
        });

        // The AI configuration depends only on the environment
        // (ANTHROPIC_API_KEY): no need for a cube or a session to tell.
        _router.Register("ai.status", (_, _) =>
            Task.FromResult<object?>(new { configured = CubeScopeSession.IsAiConfigured }));

        _router.Register("ai.run", async (p, ct) =>
        {
            var context = await ExcelThread.RunAsync(PivotTableInspector.Capture);
            var (server, catalog, _) = RequireCube(context, p);

            var action = Enum.TryParse<AiAction>(Optional(p, "action"), true, out var a)
                ? a
                : AiAction.Expliquer;
            var mdx = Required(p, "mdx");

            // The PivotTable context is what CubeScope cannot provide: without
            // it, the assistant explains a query out of context.
            var pivotContext = PivotAiContext.Describe(context);
            var prompt = pivotContext.Length > 0 ? $"{pivotContext}\n{mdx}" : mdx;

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var previous = Interlocked.Exchange(ref _runningQuery, cts);
            previous?.Dispose();

            try
            {
                var session = await _sessions.GetAsync(server, catalog, cts.Token);
                return new
                {
                    cancelled = false,
                    markdown = await session.RunAiAsync(
                        action, prompt, Optional(p, "lang") ?? "fr", cts.Token),
                };
            }
            catch (Exception ex) when (cts.IsCancellationRequested)
            {
                FileLog.Write($"AI call canceled ({ex.GetType().Name}).");
                return new { cancelled = true, markdown = string.Empty };
            }
            finally
            {
                Interlocked.CompareExchange(ref _runningQuery, null, cts);
            }
        });

        _router.Register("cell.provenance", async (p, ct) =>
        {
            var context = await ExcelThread.RunAsync(PivotTableInspector.Capture);
            var (server, catalog, cube) = RequireCube(context, p);

            var tuple = await ExcelThread.RunAsync(PivotCellReader.ReadTuple);
            var session = await _sessions.GetAsync(server, catalog, ct);

            return await new ProvenanceService(session, session)
                .DescribeAsync(cube, tuple, ct);
        });

        _router.Register("calc.list", async (_, _) =>
            await ExcelThread.RunAsync(CalculationApplier.List));

        _router.Register("calc.apply", async (p, _) =>
        {
            var definition = ReadDefinition(p);
            var addToPivot = Flag(p, "addToPivot", true);

            var uniqueName = await ExcelThread.RunAsync(
                () => CalculationApplier.Apply(definition, addToPivot));

            return new
            {
                uniqueName,
                calculations = await ExcelThread.RunAsync(CalculationApplier.List),
            };
        });

        _router.Register("calc.delete", async (p, _) =>
        {
            var uniqueName = Required(p, "uniqueName");
            await ExcelThread.RunAsync(() => CalculationApplier.Delete(uniqueName));
            return await ExcelThread.RunAsync(CalculationApplier.List);
        });

        _router.Register("library.list", async (_, ct) =>
            await _library.Value.ListAsync(ct));

        _router.Register("library.save", async (p, ct) =>
        {
            var definition = ReadDefinition(p);
            var context = await ExcelThread.RunAsync(PivotTableInspector.Capture);
            Remember(context);
            await _library.Value.SaveAsync(definition, context.Cube ?? _lastConnection?.Cube, ct);
            return await _library.Value.ListAsync(ct);
        });

        _router.Register("library.delete", async (p, ct) =>
        {
            var id = RequiredInt(p, "id");
            await _library.Value.DeleteAsync(id, ct);
            return await _library.Value.ListAsync(ct);
        });

        _router.Register("comfort.fields", async (_, _) =>
            await ExcelThread.RunAsync(PivotComfort.ListFields));

        _router.Register("comfort.setFieldVisibility", async (p, _) =>
        {
            var field = Required(p, "cubeField");
            var visible = Flag(p, "visible", true);
            await ExcelThread.RunAsync(() => PivotComfort.SetFieldVisibility(field, visible));
            return await ExcelThread.RunAsync(PivotComfort.ListFields);
        });

        _router.Register("comfort.refreshNow", async (_, _) =>
        {
            await ExcelThread.RunAsync(PivotComfort.RefreshNow);
            PivotScopeRibbon.Invalidate();
            return new { refreshed = true };
        });

        _router.Register("comfort.deferLayout", async (p, _) =>
        {
            var deferred = Flag(p, "deferred", false);
            await ExcelThread.RunAsync(() => PivotComfort.SetDeferLayout(deferred));
            return new { deferred };
        });

        _router.Register("comfort.levels", async (p, _) =>
        {
            var field = Required(p, "cubeField");
            return await ExcelThread.RunAsync(() => PivotComfort.ListLevels(field));
        });

        _router.Register("comfort.setLevels", async (p, _) =>
        {
            var field = Required(p, "cubeField");
            var levels = RequiredStrings(p, "levels");
            return await ExcelThread.RunAsync(
                () => PivotComfort.SetLevelVisibility(field, levels));
        });

        _router.Register("comfort.showAllFields", async (_, _) =>
        {
            var restored = await ExcelThread.RunAsync(PivotComfort.ShowAllFields);
            var fields = await ExcelThread.RunAsync(PivotComfort.ListFields);
            return new { restored, fields };
        });

        _router.Register("comfort.autoRefresh", async (_, _) =>
        {
            // Kept for reading the state when the pane loads: writing
            // now goes through comfort.deferLayout.
            return new { enabled = !await ExcelThread.RunAsync(PivotComfort.IsLayoutDeferred) };
        });

        _router.Register("query.cancel", (_, _) =>
        {
            var running = _runningQuery;
            if (running is null) return Task.FromResult<object?>(new { cancelled = false });

            try { running.Cancel(); } catch (ObjectDisposedException) { /* already finished */ }
            return Task.FromResult<object?>(new { cancelled = true });
        });

        _router.Register("query.run", async (p, ct) =>
        {
            // A free-form query names its cube itself, and the user must
            // be able to leave the PivotTable to choose where to write: rely on the
            // remembered connection, not on the PivotTable under the cursor.
            var (server, catalog, _) = RememberedConnection();

            var mdx = Required(p, "mdx");
            var newSheet = Flag(p, "newSheet", true);
            var includeHeaders = Flag(p, "includeHeaders", true);

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var previous = Interlocked.Exchange(ref _runningQuery, cts);
            previous?.Dispose();

            try
            {
                var started = Stopwatch.GetTimestamp();
                var session = await _sessions.GetAsync(server, catalog, cts.Token);
                var result = await session.ExecuteAsync(mdx, cts.Token);
                var grid = RangeProjection.ToGrid(result, includeHeaders);

                var address = await ExcelThread.RunAsync(() => SheetWriter.Write(grid, newSheet));

                return new
                {
                    cancelled = false,
                    address,
                    rows = grid.GetLength(0),
                    columns = grid.GetLength(1),
                    durationMs = (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds,
                };
            }
            catch (Exception ex) when (cts.IsCancellationRequested)
            {
                // A cancellation is not a failure: the server was stopped
                // on request. Say so calmly rather than with a red banner.
                FileLog.Write($"Query canceled by the user ({ex.GetType().Name}).");
                return new
                {
                    cancelled = true,
                    address = string.Empty,
                    rows = 0,
                    columns = 0,
                    durationMs = 0L,
                };
            }
            finally
            {
                Interlocked.CompareExchange(ref _runningQuery, null, cts);
            }
        });

        _router.Register("pivot.filterList", async (p, ct) =>
        {
            var context = await ExcelThread.RunAsync(PivotTableInspector.Capture);
            var (server, catalog, cube) = RequireCube(context, p);

            var cubeField = Required(p, "cubeField");
            var level = Required(p, "level");
            var keys = MemberResolver.ParseKeys(Required(p, "keys"));

            var session = await _sessions.GetAsync(server, catalog, ct);
            var resolution = await new MemberResolver(session, session)
                .ResolveAsync(cube, level, keys, ct);

            await ExcelThread.RunAsync(() =>
                PivotFilterApplier.Apply(cubeField, level, resolution.UniqueNames));

            return new
            {
                applied = resolution.UniqueNames.Count,
                unresolved = resolution.Unresolved,
                ambiguous = resolution.Ambiguous,
            };
        });
    }

    internal BridgeRouter Router => _router;

    /// <summary>
    /// Server, catalog and cube: those of the PivotTable under the cursor if there is one,
    /// otherwise those of the last known connection. This fallback is what makes it possible
    /// to browse metadata or complete MDX after leaving the PivotTable.
    /// </summary>
    private (string Server, string Catalog, string Cube) RequireCube(
        PivotContext context, JsonElement? p)
    {
        Remember(context);

        var (server, catalog, knownCube) =
            context is { HasPivot: true, IsOlap: true, Server: not null, Catalog: not null }
                ? (context.Server, context.Catalog, context.Cube)
                : RememberedConnection();

        var cube = knownCube ?? Optional(p, "cube")
            ?? throw new InvalidOperationException(
                "Cube indéterminé : placez le curseur dans le tableau croisé dynamique.");

        return (server, catalog, cube);
    }

    private void Remember(PivotContext context)
    {
        if (context is { HasPivot: true, IsOlap: true, Server: not null, Catalog: not null })
            _lastConnection = (context.Server, context.Catalog, context.Cube);
    }

    private (string Server, string Catalog, string? Cube) RememberedConnection()
        => _lastConnection ?? throw new InvalidOperationException(
            "Aucune connexion connue. Placez une fois le curseur dans un tableau " +
            "croisé dynamique OLAP pour que PivotScope découvre le serveur et le " +
            "catalogue, puis revenez ici.");

    /// <summary>Reads a calculation definition from the bridge parameters.</summary>
    private static CalculationDefinition ReadDefinition(JsonElement? p) => new(
        Required(p, "name"),
        Required(p, "expression"),
        Enum.TryParse<CalculationKind>(Optional(p, "kind"), ignoreCase: true, out var kind)
            ? kind
            : CalculationKind.Measure,
        Blank(Optional(p, "displayFolder")),
        Blank(Optional(p, "numberFormat")),
        Blank(Optional(p, "parentHierarchy")),
        OptionalInt(p, "solveOrder") ?? 0);

    /// <summary>An empty string from a form field means "not provided".</summary>
    private static string? Blank(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static IReadOnlyList<string> RequiredStrings(JsonElement? p, string name)
    {
        if (p?.ValueKind != JsonValueKind.Object ||
            !p.Value.TryGetProperty(name, out var array) ||
            array.ValueKind != JsonValueKind.Array)
            throw new InvalidOperationException($"Paramètre manquant : {name}");

        return [.. array.EnumerateArray()
            .Select(e => e.GetString())
            .Where(s => !string.IsNullOrEmpty(s))
            .Select(s => s!)];
    }

    private static int RequiredInt(JsonElement? p, string name)
        => OptionalInt(p, name) ?? throw new InvalidOperationException(
            $"Paramètre manquant : {name}");

    private static int? OptionalInt(JsonElement? p, string name)
        => p?.ValueKind == JsonValueKind.Object && p.Value.TryGetProperty(name, out var v)
           && v.ValueKind == JsonValueKind.Number
            ? v.GetInt32()
            : null;

    private static bool Flag(JsonElement? p, string name, bool fallback)
        => p?.ValueKind == JsonValueKind.Object && p.Value.TryGetProperty(name, out var v)
           && v.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? v.GetBoolean()
            : fallback;

    private static string Required(JsonElement? p, string name) =>
        Optional(p, name) ?? throw new InvalidOperationException($"Paramètre manquant : {name}");

    private static string? Optional(JsonElement? p, string name) =>
        p?.ValueKind == JsonValueKind.Object && p.Value.TryGetProperty(name, out var v)
            ? v.GetString()
            : null;

    private async void OnMessage(object? sender, string json)
    {
        try
        {
            var response = await _router.DispatchAsync(json, CancellationToken.None);
            _control.PostToWeb(response);
        }
        catch (Exception ex)
        {
            // DispatchAsync does not throw; this covers the relay itself.
            FileLog.Write("Failed to relay a response to the SPA.", ex);
        }
    }

    /// <summary>
    /// Pushed notification, with no request id: the SPA recognizes it
    /// by its `event` property and decides on its own what to reload.
    /// </summary>
    private void NotifyPivotChanged(bool pivotChanged)
    {
        try
        {
            _control.PostToWeb(
                $$"""{"event":"pivotChanged","pivotChanged":{{(pivotChanged ? "true" : "false")}}}""");
        }
        catch (Exception ex)
        {
            FileLog.Write("Failed to notify the PivotTable change.", ex);
        }
    }

    public void Dispose()
    {
        _watcher.Dispose();
        _control.MessageReceived -= OnMessage;
        _sessions.Dispose();
        if (_library.IsValueCreated) _library.Value.Dispose();
    }
}
