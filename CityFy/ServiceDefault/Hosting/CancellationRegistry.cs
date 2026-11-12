using System.Collections.Concurrent;

namespace ServiceDefault.Hosting;

/// <summary>
/// Running processes of this service instance that can be cancelled by id (admin panel).
/// A process registers when it starts and unregisters when it ends; Cancel trips its token.
/// In-process only: with several instances, cancel must reach the instance running the work
/// (here every service runs a single instance).
/// </summary>
public sealed class CancellationRegistry
{
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _running = new();

    /// <summary>Token linked to <paramref name="parent"/> (shutdown) and to Cancel(id).</summary>
    public CancellationTokenSource Register(string id, CancellationToken parent)
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(parent);
        _running[id] = cts;
        return cts;
    }

    public void Unregister(string id, CancellationTokenSource cts)
    {
        // Only remove our own entry (a re-run may have registered the same id meanwhile)
        _running.TryRemove(new KeyValuePair<string, CancellationTokenSource>(id, cts));
        cts.Dispose();
    }

    /// <summary>True when the process was running here and has been signalled.</summary>
    public bool Cancel(string id)
    {
        if (!_running.TryGetValue(id, out var cts)) return false;
        try
        {
            cts.Cancel();
            return true;
        }
        catch (ObjectDisposedException)
        {
            return false; // finished in the meantime
        }
    }
}
