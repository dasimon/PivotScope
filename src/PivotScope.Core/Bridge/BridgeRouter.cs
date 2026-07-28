using System.Text.Json;
using System.Text.Json.Serialization;

namespace PivotScope.Core.Bridge;

/// <summary>
/// Routage des messages du volet. Règle absolue : ne jamais laisser échapper une
/// exception. Toute erreur devient une réponse ok=false portant l'identifiant
/// d'origine, faute de quoi la promesse correspondante reste pendante côté SPA
/// et l'interface se fige sans rien afficher.
/// </summary>
public sealed class BridgeRouter
{
    /// <summary>
    /// Les enums partent en CHAÎNES : par défaut System.Text.Json les rend en
    /// nombres, et la SPA se retrouverait à comparer 2 à « Measure » — un bug
    /// qui ne se voit qu'à l'exécution, dans un cas sur trois.
    /// </summary>
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly Dictionary<string, Func<JsonElement?, CancellationToken, Task<object?>>> _handlers =
        new(StringComparer.Ordinal);

    public void Register(string method, Func<JsonElement?, CancellationToken, Task<object?>> handler)
        => _handlers[method] = handler;

    public async Task<string> DispatchAsync(string requestJson, CancellationToken ct)
    {
        var id = "0";
        try
        {
            var request = JsonSerializer.Deserialize<BridgeRequest>(requestJson, Json)
                          ?? throw new InvalidOperationException("Message vide.");
            id = request.Id;

            if (!_handlers.TryGetValue(request.Method, out var handler))
                return Serialize(new BridgeResponse(id, false, null,
                    $"Méthode inconnue : {request.Method}"));

            var result = await handler(request.Params, ct).ConfigureAwait(false);
            return Serialize(new BridgeResponse(id, true, result, null));
        }
        catch (Exception ex)
        {
            return Serialize(new BridgeResponse(id, false, null, ex.Message));
        }
    }

    private static string Serialize(BridgeResponse response)
        => JsonSerializer.Serialize(response, Json);
}
