using System.IO;
using PivotScope.AddIn.Diagnostics;
using PivotScope.Core.Adapters;

namespace PivotScope.AddIn.Interop;

/// <summary>
/// Which connection a call uses. An SSAS connection serves one command at a
/// time: a free-form query that runs for minutes on the same connection as
/// the metadata would freeze completion, the explorer and provenance for as long.
/// </summary>
public enum SessionLane
{
    Metadata,
    Query,
}

/// <summary>
/// Keeps the SSAS sessions open per server/catalog/lane, derived from the
/// workbook connection. Lazy opening: as long as the user does not ask for
/// anything that touches the cube, no connection is made.
///
/// Two rules the previous single-session version broke:
/// — a session is never disposed while a call is using it: going from a dev
///   workbook to a prod one used to close the connection under a running query;
/// — a session that fails on a connection error is dropped, so that the next
///   call reconnects: after an SSAS restart, nothing worked until Excel was
///   restarted.
/// </summary>
public sealed class SessionProvider : IDisposable
{
    private sealed class Entry(Task<CubeScopeSession> session)
    {
        public Task<CubeScopeSession> Session { get; } = session;
        public int Users;
        public bool Evicted;
    }

    private readonly Lock _gate = new();
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.OrdinalIgnoreCase);
    private bool _disposed;

    public async Task<T> UseAsync<T>(
        string server, string catalog, SessionLane lane,
        Func<CubeScopeSession, Task<T>> work, CancellationToken ct = default)
    {
        var key = $"{server}|{catalog}|{lane}";
        var entry = Acquire(key, server, catalog);
        try
        {
            CubeScopeSession session;
            try
            {
                session = await entry.Session.WaitAsync(ct).ConfigureAwait(false);
            }
            catch (Exception) when (!ct.IsCancellationRequested)
            {
                // A failed connection attempt is not cached: the next call retries.
                Evict(key, entry);
                throw;
            }

            return await work(session).ConfigureAwait(false);
        }
        catch (Exception ex) when (IsConnectionFailure(ex))
        {
            FileLog.Write($"SSAS session {key} dropped after a connection error.", ex);
            Evict(key, entry);
            throw;
        }
        finally
        {
            Release(entry);
        }
    }

    private Entry Acquire(string key, string server, string catalog)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (!_entries.TryGetValue(key, out var entry))
            {
                FileLog.Write($"Opening an SSAS session: {key}");
                entry = new Entry(Task.Run(() => CubeScopeSession.ConnectAsync(server, catalog)));
                _entries[key] = entry;
            }

            entry.Users++;
            return entry;
        }
    }

    private void Evict(string key, Entry entry)
    {
        lock (_gate)
        {
            entry.Evicted = true;
            if (_entries.TryGetValue(key, out var current) && ReferenceEquals(current, entry))
                _entries.Remove(key);
        }
    }

    private void Release(Entry entry)
    {
        bool dispose;
        lock (_gate)
        {
            entry.Users--;
            dispose = entry.Evicted && entry.Users == 0;
        }
        if (dispose) DisposeQuietly(entry);
    }

    /// <summary>
    /// Failures that mean "this connection is dead", as opposed to "this query
    /// is wrong". A false positive only costs a reconnection.
    /// </summary>
    private static bool IsConnectionFailure(Exception ex)
    {
        for (var e = ex; e is not null; e = e.InnerException)
        {
            if (e is OperationCanceledException) return false;
            if (e is IOException or ObjectDisposedException) return true;
            if (e.GetType().Name == "AdomdConnectionException") return true;

            var message = e.Message;
            if (message.Contains("session", StringComparison.OrdinalIgnoreCase) &&
                (message.Contains("introuvable", StringComparison.OrdinalIgnoreCase) ||
                 message.Contains("not found", StringComparison.OrdinalIgnoreCase) ||
                 message.Contains("cannot be found", StringComparison.OrdinalIgnoreCase) ||
                 message.Contains("expir", StringComparison.OrdinalIgnoreCase)))
                return true;
            if (message.Contains("connexion n'est pas ouverte", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("connection is not open", StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private static void DisposeQuietly(Entry entry)
    {
        if (!entry.Session.IsCompletedSuccessfully) return;
        try { entry.Session.Result.Dispose(); }
        catch (Exception ex) { FileLog.Write("Failed to close an SSAS session.", ex); }
    }

    public void Dispose()
    {
        List<Entry> entries;
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            entries = [.. _entries.Values];
            _entries.Clear();
        }
        foreach (var entry in entries) DisposeQuietly(entry);
    }
}
