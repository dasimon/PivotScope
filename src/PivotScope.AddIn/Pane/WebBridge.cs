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
/// Enregistre les méthodes exposées à la SPA et relaie les réponses.
/// Répartition stricte : ce qui touche Excel passe par ExcelThread, ce qui
/// interroge SSAS reste hors du thread UI.
/// </summary>
internal sealed class WebBridge : IDisposable
{
    private readonly BridgeRouter _router = new();
    private readonly PaneControl _control;
    private readonly SessionProvider _sessions = new();

    /// <summary>
    /// Dernière connexion vue sur un TCD OLAP. La connexion est une donnée de
    /// session, pas de l'instant : écrire une requête à partir de la cellule
    /// active suppose justement d'avoir quitté le TCD, et exiger un TCD sous le
    /// curseur à ce moment-là rendrait la fonction impossible à utiliser.
    /// </summary>
    private (string Server, string Catalog, string? Cube)? _lastConnection;

    /// <summary>
    /// Requête en vol, s'il y en a une. Annuler le jeton déclenche
    /// AdomdCommand.Cancel() dans QueryService : le serveur arrête réellement
    /// de travailler, on n'abandonne pas seulement l'attente.
    /// </summary>
    private CancellationTokenSource? _runningQuery;

    /// <summary>
    /// Bibliothèque de calculs, ouverte à la première utilisation seulement :
    /// le démarrage du complément ne doit toucher ni disque ni réseau.
    /// </summary>
    private readonly Lazy<CalculationLibrary> _library = new(() => new CalculationLibrary());

    /// <summary>
    /// Suit le TCD actif et pousse un événement vers la SPA. Sans lui, le volet
    /// affiche l'état du dernier clic sur « Actualiser » : potentiellement faux,
    /// et silencieusement.
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

        // La configuration de l'IA ne dépend que de l'environnement
        // (ANTHROPIC_API_KEY) : pas besoin de cube ni de session pour le dire.
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

            // Le contexte du TCD est ce que CubeScope ne peut pas fournir : sans
            // lui, l'assistant explique une requête hors sol.
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
                    markdown = await session.RunAiAsync(action, prompt, "fr", cts.Token),
                };
            }
            catch (Exception ex) when (cts.IsCancellationRequested)
            {
                FileLog.Write($"Appel IA annulé ({ex.GetType().Name}).");
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
            // Conservé pour la lecture d'état au chargement du volet : l'écriture
            // passe désormais par comfort.deferLayout.
            return new { enabled = !await ExcelThread.RunAsync(PivotComfort.IsLayoutDeferred) };
        });

        _router.Register("query.cancel", (_, _) =>
        {
            var running = _runningQuery;
            if (running is null) return Task.FromResult<object?>(new { cancelled = false });

            try { running.Cancel(); } catch (ObjectDisposedException) { /* déjà terminée */ }
            return Task.FromResult<object?>(new { cancelled = true });
        });

        _router.Register("query.run", async (p, ct) =>
        {
            // Une requête libre nomme son cube elle-même, et l'utilisateur doit
            // pouvoir sortir du TCD pour choisir où écrire : on s'appuie sur la
            // connexion mémorisée, pas sur le TCD sous le curseur.
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
                // Une annulation n'est pas une panne : le serveur a été arrêté
                // à la demande. On le dit calmement plutôt qu'en bandeau rouge.
                FileLog.Write($"Requête annulée par l'utilisateur ({ex.GetType().Name}).");
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
    /// Serveur, catalogue et cube : ceux du TCD sous le curseur s'il y en a un,
    /// sinon ceux de la dernière connexion connue. Ce repli est ce qui permet de
    /// consulter les métadonnées ou de compléter du MDX en étant sorti du TCD.
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

    /// <summary>Lit une définition de calcul depuis les paramètres du pont.</summary>
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

    /// <summary>Une chaîne vide venue d'un champ de formulaire vaut « non renseigné ».</summary>
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
            // DispatchAsync ne lève pas ; on couvre ici le relais lui-même.
            FileLog.Write("Échec de relais d'une réponse vers la SPA.", ex);
        }
    }

    /// <summary>
    /// Notification poussée, sans identifiant de requête : la SPA la reconnaît
    /// à sa propriété « event » et décide seule quoi recharger.
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
            FileLog.Write("Échec de notification du changement de TCD.", ex);
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
