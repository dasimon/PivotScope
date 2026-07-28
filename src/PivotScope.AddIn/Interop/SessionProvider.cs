using PivotScope.AddIn.Diagnostics;
using PivotScope.Core.Adapters;

namespace PivotScope.AddIn.Interop;

/// <summary>
/// Garde une session SSAS ouverte par couple serveur/catalogue, dérivée de la
/// connexion du classeur. Ouverture paresseuse : tant que l'utilisateur ne
/// demande rien qui touche au cube, aucune connexion n'est établie.
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

            FileLog.Write($"Ouverture d'une session SSAS : {key}");
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
