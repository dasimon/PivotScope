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

        // The bridge must never throw: otherwise the promise on the SPA side stays pending.
        Assert.Contains("\"ok\":false", json);
    }

    [Fact]
    public async Task DispatchAsync_SerialiseLesEnumsEnChaines()
    {
        // Otherwise the SPA compares 2 to "Measure": a bug invisible at build time.
        var router = new BridgeRouter();
        router.Register("kind", (_, _) =>
            Task.FromResult<object?>(new { kind = PivotScope.Core.Calculations.CalculationKind.Measure }));

        var json = await router.DispatchAsync("""{"id":"1","method":"kind"}""", CancellationToken.None);

        Assert.Contains("\"kind\":\"Measure\"", json);
    }

    [Fact]
    public async Task DispatchAsync_IdNumerique_EstRenvoyeTelQuel()
    {
        var router = new BridgeRouter();
        router.Register("ping", (_, _) => Task.FromResult<object?>(true));

        var json = await router.DispatchAsync("""{"id":7,"method":"ping"}""", CancellationToken.None);

        Assert.Contains("\"id\":\"7\"", json);
        Assert.Contains("\"ok\":true", json);
    }

    [Fact]
    public async Task DispatchAsync_MethodeAbsente_GardeLId()
    {
        var router = new BridgeRouter();

        var json = await router.DispatchAsync("""{"id":"9"}""", CancellationToken.None);

        Assert.Contains("\"id\":\"9\"", json);
        Assert.Contains("\"ok\":false", json);
    }

    [Fact]
    public async Task DispatchAsync_DescribeError_TraduitLErreur()
    {
        var router = new BridgeRouter { DescribeError = _ => "feuille protégée" };
        router.Register("boom", (_, _) => throw new InvalidOperationException("0x800A03EC"));

        var json = await router.DispatchAsync("""{"id":"1","method":"boom"}""", CancellationToken.None);

        Assert.Contains("feuille prot", json);
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
