using Loom.LanguageServer;
using OmniSharp.Extensions.LanguageServer.Protocol;

namespace Loom.Testing;

public class DebouncerTest
{
    private static readonly DocumentUri First = DocumentUri.From("file:///first.loom");
    private static readonly DocumentUri Second = DocumentUri.From("file:///second.loom");

    [Fact]
    public async Task Schedule_RunsTheWorkAfterTheDelay()
    {
        using var debouncer = new Debouncer(TimeSpan.FromMilliseconds(10));
        var ran = new TaskCompletionSource();

        debouncer.Schedule(
            First,
            _ =>
            {
                ran.TrySetResult();
                return Task.CompletedTask;
            }
        );

        await ran.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Schedule_RunsOnlyTheLastOfABurst()
    {
        using var debouncer = new Debouncer(TimeSpan.FromMilliseconds(30));
        var runs = 0;
        var last = new TaskCompletionSource<int>();

        for (var keystroke = 1; keystroke <= 20; keystroke++)
        {
            var value = keystroke;
            debouncer.Schedule(
                First,
                _ =>
                {
                    Interlocked.Increment(ref runs);
                    last.TrySetResult(value);
                    return Task.CompletedTask;
                }
            );
        }

        Assert.Equal(20, await last.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
        Assert.Equal(1, Volatile.Read(ref runs));
    }

    [Fact]
    public async Task Schedule_KeepsDocumentsApart()
    {
        using var debouncer = new Debouncer(TimeSpan.FromMilliseconds(10));
        var both = new TaskCompletionSource();
        var seen = 0;

        foreach (var uri in new[] { First, Second })
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
        var ran = false;

        debouncer.Schedule(
            First,
            _ =>
            {
                Volatile.Write(ref ran, true);
                return Task.CompletedTask;
            }
        );

        debouncer.Cancel(First);
        await Task.Delay(TimeSpan.FromMilliseconds(200), TestContext.Current.CancellationToken);

        Assert.False(Volatile.Read(ref ran));
    }

    [Fact]
    public async Task Dispose_DropsEverythingStillScheduled()
    {
        var ran = false;
        using (var debouncer = new Debouncer(TimeSpan.FromMilliseconds(50)))
            debouncer.Schedule(
                First,
                _ =>
                {
                    Volatile.Write(ref ran, true);
                    return Task.CompletedTask;
                }
            );

        await Task.Delay(TimeSpan.FromMilliseconds(200), TestContext.Current.CancellationToken);
        Assert.False(Volatile.Read(ref ran));
    }

    [Fact]
    public async Task Schedule_SurvivesWorkThatThrows()
    {
        using var debouncer = new Debouncer(TimeSpan.FromMilliseconds(10));
        var second = new TaskCompletionSource();

        debouncer.Schedule(First, _ => throw new InvalidOperationException("boom"));
        await Task.Delay(TimeSpan.FromMilliseconds(100), TestContext.Current.CancellationToken);

        debouncer.Schedule(
            First,
            _ =>
            {
                second.TrySetResult();
                return Task.CompletedTask;
            }
        );

        await second.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
    }
}
