using System.Text.Json;
using System.Text.Json.Serialization;

namespace PivotScope.Core.Bridge;

/// <summary>Outgoing response to the SPA. Always sent, even on error.</summary>
public sealed record BridgeResponse(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("ok")] bool Ok,
    [property: JsonPropertyName("result")] object? Result,
    [property: JsonPropertyName("error")] string? Error);
