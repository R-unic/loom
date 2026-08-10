using OmniSharp.Extensions.LanguageServer.Protocol;

namespace Loom.LanguageServer;

/// <summary>
///     Runs the latest of a burst of requests and drops the rest. Typing produces one notification per
///     keystroke, and the work each one asks for describes text the next keystroke has already replaced - so
///     what matters is that the work runs once the typing stops, not that it runs once per character.
/// </summary>
/// <remarks>
///     Only useful for work whose result can wait. Anything a request is blocking on has to be done when it is
///     asked for; see <see cref="DocumentStore.Compile" />, which brings a document up to date on demand.
/// </remarks>
public sealed class Debouncer(TimeSpan delay) : IDisposable
{
    private readonly Dictionary<DocumentUri, CancellationTokenSource> _pending = [];
    private readonly Lock _gate = new();
    private bool _disposed;

    /// <summary>Schedules <paramref name="work" />, cancelling whatever was already scheduled for <paramref name="key" />.</summary>
    public void Schedule(DocumentUri key, Func<CancellationToken, Task> work)
    {
        var scheduled = new CancellationTokenSource();
        lock (_gate)
        {
            if (_disposed)
            {
                scheduled.Dispose();
                return;
            }

            Replace(key, scheduled);
        }

        _ = RunAsync(key, scheduled, work);
    }

    /// <summary>Drops anything scheduled for <paramref name="key" />, for a document that is no longer worth the work.</summary>
    public void Cancel(DocumentUri key)
    {
        lock (_gate)
            Replace(key, null);
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            foreach (var key in _pending.Keys.ToArray())
                Replace(key, null);
        }
    }

    private void Replace(DocumentUri key, CancellationTokenSource? scheduled)
    {
        if (_pending.Remove(key, out var previous))
        {
            previous.Cancel();
            previous.Dispose();
        }

        if (scheduled != null)
            _pending[key] = scheduled;
    }

    private async Task RunAsync(DocumentUri key, CancellationTokenSource scheduled, Func<CancellationToken, Task> work)
    {
        try
        {
            await Task.Delay(delay, scheduled.Token).ConfigureAwait(false);
            await work(scheduled.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // superseded by a later request, which is the point
        }
        catch (Exception)
        {
            // the work reports its own failures; a background task must not take the server down
        }
        finally
        {
            lock (_gate)
                if (_pending.TryGetValue(key, out var current) && current == scheduled)
                {
                    _pending.Remove(key);
                    scheduled.Dispose();
                }
        }
    }
}
