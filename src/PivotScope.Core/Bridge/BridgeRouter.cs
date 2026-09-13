using System.Text.Json;
using System.Text.Json.Serialization;

namespace PivotScope.Core.Bridge;

/// <summary>
/// Routes the task pane's messages. Absolute rule: never let an exception
/// escape. Every error becomes an ok=false response carrying the original id;
/// otherwise the matching promise stays pending on the SPA side and the UI
/// freezes without showing anything.
/// </summary>
public sealed class BridgeRouter
{
    /// <summary>
    /// Enums go out as STRINGS: by default System.Text.Json writes them as
    /// numbers, and the SPA would end up comparing 2 to "Measure" — a bug that
    /// only shows at runtime, in one case out of three.
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
