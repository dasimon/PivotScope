using System.Text.Json;
using System.Text.Json.Serialization;

namespace PivotScope.Core.Bridge;

/// <summary>Message entrant depuis la SPA.</summary>
public sealed record BridgeRequest(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("method")] string Method,
    [property: JsonPropertyName("params")] JsonElement? Params);

/// <summary>Réponse sortante vers la SPA. Toujours émise, même en erreur.</summary>
public sealed record BridgeResponse(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("ok")] bool Ok,
    [property: JsonPropertyName("result")] object? Result,
    [property: JsonPropertyName("error")] string? Error);
