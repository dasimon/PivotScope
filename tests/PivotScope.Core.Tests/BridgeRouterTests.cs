using PivotScope.Core.Bridge;

namespace PivotScope.Core.Tests;

public class BridgeRouterTests
{
    [Fact]
    public async Task DispatchAsync_InvokesHandler_AndSerializesResult()
    {
        var router = new BridgeRouter();
        router.Register("ping", (_, _) => Task.FromResult<object?>(new { pong = true }));

        var json = await router.DispatchAsync("""{"id":"7","method":"ping"}""", CancellationToken.None);

        Assert.Contains("\"id\":\"7\"", json);
        Assert.Contains("\"ok\":true", json);
        Assert.Contains("\"pong\":true", json);
    }

    [Fact]
    public async Task DispatchAsync_PassesParams_ToHandler()
    {
        var router = new BridgeRouter();
        string? seen = null;
        router.Register("echo", (p, _) =>
        {
            seen = p?.GetProperty("text").GetString();
            return Task.FromResult<object?>(null);
        });

        await router.DispatchAsync(
            """{"id":"1","method":"echo","params":{"text":"EUR"}}""", CancellationToken.None);

        Assert.Equal("EUR", seen);
    }

    [Fact]
    public async Task DispatchAsync_UnknownMethod_ReturnsError_AndKeepsTheId()
    {
        var router = new BridgeRouter();

        var json = await router.DispatchAsync("""{"id":"42","method":"nope"}""", CancellationToken.None);

        Assert.Contains("\"id\":\"42\"", json);
        Assert.Contains("\"ok\":false", json);
        Assert.Contains("nope", json);
    }

    [Fact]
    public async Task DispatchAsync_HandlerThrows_ReturnsErrorWithMessage()
    {
        var router = new BridgeRouter();
        router.Register("boom", (_, _) =>
            throw new InvalidOperationException("cube introuvable"));

        var json = await router.DispatchAsync("""{"id":"2","method":"boom"}""", CancellationToken.None);

        Assert.Contains("\"ok\":false", json);
        Assert.Contains("cube introuvable", json);
    }

    [Fact]
    public async Task DispatchAsync_MalformedJson_ReturnsError_RatherThanThrowing()
    {
        var router = new BridgeRouter();

        var json = await router.DispatchAsync("pas du json", CancellationToken.None);

        // Le pont ne doit jamais lever : sinon la promesse côté SPA reste pendante.
        Assert.Contains("\"ok\":false", json);
    }

    [Fact]
    public async Task DispatchAsync_SerialiseLesEnumsEnChaines()
    {
        // Sinon la SPA compare 2 à « Measure » : un bug invisible au build.
        var router = new BridgeRouter();
        router.Register("kind", (_, _) =>
            Task.FromResult<object?>(new { kind = PivotScope.Core.Calculations.CalculationKind.Measure }));

        var json = await router.DispatchAsync("""{"id":"1","method":"kind"}""", CancellationToken.None);

        Assert.Contains("\"kind\":\"Measure\"", json);
    }

    [Fact]
    public async Task Register_SameMethodTwice_LastHandlerWins()
    {
        var router = new BridgeRouter();
        router.Register("m", (_, _) => Task.FromResult<object?>("premier"));
        router.Register("m", (_, _) => Task.FromResult<object?>("second"));

        var json = await router.DispatchAsync("""{"id":"1","method":"m"}""", CancellationToken.None);

        Assert.Contains("second", json);
        Assert.DoesNotContain("premier", json);
    }
}
