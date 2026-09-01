using System.Runtime.CompilerServices;
using Loom.LanguageServer;
using OmniSharp.Extensions.LanguageServer.Protocol;

namespace Loom.Testing.LanguageServer;

/// <remarks>
///     Flags the debounced work sets are held in a <see cref="StrongBox{T}" /> rather than in a local. The
///     work runs on another thread, so reading one takes a <c>ref</c> - and taking a <c>ref</c> to a local
///     that a lambda has also captured is the shape of the classic modified-closure bug, even where it is
///     deliberate. Boxing the flag says which one this is.
/// </remarks>
public class DebouncerTest
{
    private static readonly DocumentUri _first = DocumentUri.From("file:///first.loom");
    private static readonly DocumentUri _second = DocumentUri.From("file:///second.loom");

    [Fact]
    public async Task Schedule_RunsTheWorkAfterTheDelay()
    {
        using var debouncer = new Debouncer(TimeSpan.FromMilliseconds(10));
        var ran = new TaskCompletionSource();

        debouncer.Schedule(
            _first,
            _ =>
            {
                ran.TrySetResult();
                return Task.CompletedTask;
            }
        );

        await ran.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
    }

    /// <remarks>
    ///     The delay has to outlast the burst by enough that a stalled machine cannot let an early item
    ///     through, or the test fails for a reason that has nothing to do with the debouncer.
    /// </remarks>
    [Fact]
    public async Task Schedule_RunsOnlyTheLastOfABurst()
    {
        using var debouncer = new Debouncer(TimeSpan.FromMilliseconds(500));
        var runs = new StrongBox<int>(0);
        var last = new TaskCompletionSource<int>();

        for (var keystroke = 1; keystroke <= 20; keystroke++)
        {
            var value = keystroke;
            debouncer.Schedule(
                _first,
                _ =>
                {
                    Interlocked.Increment(ref runs.Value);
                    last.TrySetResult(value);
                    return Task.CompletedTask;
                }
            );
        }

        Assert.Equal(20, await last.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
        Assert.Equal(1, Volatile.Read(ref runs.Value));
    }

    [Fact]
    public async Task Schedule_KeepsDocumentsApart()
    {
        using var debouncer = new Debouncer(TimeSpan.FromMilliseconds(10));
        var both = new TaskCompletionSource();
        var seen = 0;

        foreach (var uri in new[] { _first, _second })
            debouncer.Schedule(
                uri,
                _ =>
                {
                    if (Interlocked.Increment(ref seen) == 2)
                        both.TrySetResult();

                    return Task.CompletedTask;
                }
            );

        await both.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Cancel_DropsWorkThatHasNotRunYet()
    {
        using var debouncer = new Debouncer(TimeSpan.FromMilliseconds(50));
        var ran = new StrongBox<bool>(false);

        debouncer.Schedule(
            _first,
            _ =>
            {
                Volatile.Write(ref ran.Value, true);
                return Task.CompletedTask;
            }
        );

        debouncer.Cancel(_first);
        await Task.Delay(TimeSpan.FromMilliseconds(200), TestContext.Current.CancellationToken);

        Assert.False(Volatile.Read(ref ran.Value));
    }

    [Fact]
    public async Task Dispose_DropsEverythingStillScheduled()
    {
        var ran = new StrongBox<bool>(false);
        using (var debouncer = new Debouncer(TimeSpan.FromMilliseconds(50)))
            debouncer.Schedule(
                _first,
                _ =>
                {
                    Volatile.Write(ref ran.Value, true);
                    return Task.CompletedTask;
                }
            );

        await Task.Delay(TimeSpan.FromMilliseconds(200), TestContext.Current.CancellationToken);
        Assert.False(Volatile.Read(ref ran.Value));
    }

    [Fact]
    public async Task Schedule_SurvivesWorkThatThrows()
    {
        using var debouncer = new Debouncer(TimeSpan.FromMilliseconds(10));
        var second = new TaskCompletionSource();

        debouncer.Schedule(_first, _ => throw new InvalidOperationException("boom"));
        await Task.Delay(TimeSpan.FromMilliseconds(100), TestContext.Current.CancellationToken);

        debouncer.Schedule(
            _first,
            _ =>
            {
                second.TrySetResult();
                return Task.CompletedTask;
            }
        );

        await second.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
    }
}
