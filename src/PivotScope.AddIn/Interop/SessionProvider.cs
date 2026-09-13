using PivotScope.AddIn.Diagnostics;
using PivotScope.Core.Adapters;

namespace PivotScope.AddIn.Interop;

/// <summary>
/// Keeps one SSAS session open per server/catalog pair, derived from the
/// workbook connection. Lazy opening: as long as the user does not
/// ask for anything that touches the cube, no connection is made.
/// </summary>
public sealed class SessionProvider : IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private CubeScopeSession? _session;
    private string? _key;

    public async Task<CubeScopeSession> GetAsync(
        string server, string catalog, CancellationToken ct = default)
    {
        var key = $"{server}|{catalog}";

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_session is not null && _key == key) return _session;

            _session?.Dispose();
            _session = null;

            FileLog.Write($"Opening an SSAS session: {key}");
            _session = await CubeScopeSession.ConnectAsync(server, catalog, ct: ct)
                .ConfigureAwait(false);
            _key = key;
            return _session;
        }
        finally { _gate.Release(); }
    }

    public void Dispose()
    {
        _session?.Dispose();
        _gate.Dispose();
    }
}
