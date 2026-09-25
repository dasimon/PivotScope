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

    /// <summary>
    /// Turns an exception into the text shown in the pane. The host plugs in
    /// what Core cannot know — a raw COM HRESULT ("0x800A03EC") means nothing
    /// to the user, "the sheet is protected" does.
    /// </summary>
    public Func<Exception, string> DescribeError { get; set; } = ex => ex.Message;

    public void Register(string method, Func<JsonElement?, CancellationToken, Task<object?>> handler)
        => _handlers[method] = handler;

    public async Task<string> DispatchAsync(string requestJson, CancellationToken ct)
    {
        // The id is read first and leniently (string or number): if anything
        // else in the message is wrong, the error must still reach the promise
        // that waits for it.
        var id = "0";
        try
        {
            string? method;
            JsonElement? parameters = null;

            using (var document = JsonDocument.Parse(requestJson))
            {
                var root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object)
                    throw new InvalidOperationException("Message invalide.");

                if (root.TryGetProperty("id", out var idElement))
                    id = idElement.ValueKind switch
                    {
                        JsonValueKind.String => idElement.GetString() ?? "0",
                        JsonValueKind.Number => idElement.GetRawText(),
                        _ => "0",
                    };

                method = root.TryGetProperty("method", out var m) && m.ValueKind == JsonValueKind.String
                    ? m.GetString()
                    : null;

                // Cloned: the element must outlive the document.
                if (root.TryGetProperty("params", out var p) && p.ValueKind != JsonValueKind.Null)
                    parameters = p.Clone();
            }

            if (method is null || !_handlers.TryGetValue(method, out var handler))
                return Serialize(new BridgeResponse(id, false, null,
                    $"Méthode inconnue : {method}"));

            var result = await handler(parameters, ct).ConfigureAwait(false);
            return Serialize(new BridgeResponse(id, true, result, null));
        }
        catch (Exception ex)
        {
            string message;
            try { message = DescribeError(ex); } catch { message = ex.Message; }
            return Serialize(new BridgeResponse(id, false, null, message));
        }
    }

    private static string Serialize(BridgeResponse response)
        => JsonSerializer.Serialize(response, Json);
}
