using System.Collections.Concurrent;

namespace LoupixDeck.Plugin.Systemd;

/// <summary>
/// Waits for the systemd job a command produced. A start, stop, restart or reload call only means
/// that systemd accepted the request; whether it worked is reported later through JobRemoved.
/// <para>
/// JobRemoved can arrive before the method reply carrying the job path does, so results for jobs
/// nobody is waiting for yet are remembered for a short while.
/// </para>
/// </summary>
internal sealed class JobTracker : IDisposable
{
    /// <summary>How long a result is kept for a caller that has not asked for it yet.</summary>
    private static readonly TimeSpan EarlyResultLifetime = TimeSpan.FromSeconds(60);

    /// <summary>systemd's result for a job that finished as intended.</summary>
    public const string DoneResult = "done";

    /// <summary>The result reported when the wait ran out instead of the job finishing.</summary>
    public const string TimedOutResult = "wait-timeout";

    private readonly ConcurrentDictionary<string, TaskCompletionSource<string>> _pending = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, (string Result, DateTimeOffset At)> _early = new(StringComparer.Ordinal);

    /// <summary>Feeds a JobRemoved signal into the tracker.</summary>
    public void Complete(string jobPath, string result)
    {
        if (string.IsNullOrEmpty(jobPath))
        {
            return;
        }

        if (_pending.TryRemove(jobPath, out TaskCompletionSource<string>? waiter))
        {
            waiter.TrySetResult(result);
            return;
        }

        PruneEarlyResults();
        _early[jobPath] = (result, DateTimeOffset.UtcNow);
    }

    /// <summary>
    /// Waits for a job to finish and returns systemd's result string, or
    /// <see cref="TimedOutResult"/> when the wait ran out. A timeout never cancels the job: the
    /// user asked systemd to do something, and stopping it halfway is worse than waiting longer.
    /// </summary>
    public async Task<string> WaitAsync(string jobPath, TimeSpan timeout)
    {
        if (string.IsNullOrEmpty(jobPath))
        {
            return DoneResult;
        }

        if (_early.TryRemove(jobPath, out (string Result, DateTimeOffset At) early))
        {
            return early.Result;
        }

        TaskCompletionSource<string> waiter = _pending.GetOrAdd(
            jobPath,
            _ => new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously));

        // The signal may have landed between the lookup above and the registration.
        if (_early.TryRemove(jobPath, out early))
        {
            _pending.TryRemove(jobPath, out _);
            return early.Result;
        }

        try
        {
            return await waiter.Task.WaitAsync(timeout).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            _pending.TryRemove(jobPath, out _);
            return TimedOutResult;
        }
    }

    /// <summary>Drops every pending wait, for example when the manager went away.</summary>
    public void Reset()
    {
        foreach (string jobPath in _pending.Keys)
        {
            if (_pending.TryRemove(jobPath, out TaskCompletionSource<string>? waiter))
            {
                waiter.TrySetResult(TimedOutResult);
            }
        }

        _early.Clear();
    }

    public void Dispose() => Reset();

    /// <summary>Maps a systemd job result to the outcome a command reports.</summary>
    public static UnitCallOutcome ToOutcome(string result)
    {
        return result switch
        {
            DoneResult or "skipped" or "once" => UnitCallOutcome.Ok,
            TimedOutResult or "timeout" or "canceled" or "collected" => UnitCallOutcome.Unavailable,
            _ => UnitCallOutcome.Failed
        };
    }

    /// <summary>The English text shown for a job result that is not "done".</summary>
    public static string ToEnglishText(string result)
    {
        return result switch
        {
            DoneResult => "Done",
            "canceled" => "Canceled",
            "timeout" => "Timed out",
            "failed" => "Failed",
            "dependency" => "Dependency failed",
            "skipped" => "Skipped",
            "collected" => "Collected",
            "once" => "Done",
            TimedOutResult => "Still running",
            _ => "Failed"
        };
    }

    private void PruneEarlyResults()
    {
        DateTimeOffset cutoff = DateTimeOffset.UtcNow - EarlyResultLifetime;

        foreach ((string jobPath, (string _, DateTimeOffset at)) in _early)
        {
            if (at < cutoff)
            {
                _early.TryRemove(jobPath, out _);
            }
        }
    }
}
