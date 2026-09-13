using System.Text.Json;
using System.Text.Json.Serialization;

namespace PivotScope.Core.Bridge;

/// <summary>Incoming message from the SPA.</summary>
public sealed record BridgeRequest(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("method")] string Method,
    [property: JsonPropertyName("params")] JsonElement? Params);

/// <summary>Outgoing response to the SPA. Always sent, even on error.</summary>
public sealed record BridgeResponse(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("ok")] bool Ok,
    [property: JsonPropertyName("result")] object? Result,
    [property: JsonPropertyName("error")] string? Error);
