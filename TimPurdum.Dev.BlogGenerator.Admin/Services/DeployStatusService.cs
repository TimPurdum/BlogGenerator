namespace TimPurdum.Dev.BlogGenerator.Admin.Services;

/// <summary>
/// Tracks an in-flight deploy after the admin commits a change. Background-polls the GitHub
/// Actions API for the workflow run associated with the commit and exposes state changes via
/// <see cref="Changed"/>. UI components (notably <c>DeployBanner</c>) subscribe to render the
/// status persistently across admin navigation.
/// </summary>
public sealed class DeployStatusService(GitHubApiService api)
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(6);
    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(5);

    public DeployState State { get; private set; } = DeployState.Idle;
    public string? CommitSha { get; private set; }
    public string? ViewLiveUrl { get; private set; }
    public string? RunUrl { get; private set; }
    public string? ErrorMessage { get; private set; }

    public event Action? Changed;

    private CancellationTokenSource? _cts;

    /// <summary>Start tracking the deploy triggered by <paramref name="commitSha"/>. Cancels any prior tracker.</summary>
    public void Track(string commitSha, string? viewLiveUrl)
    {
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        State = DeployState.Pending;
        CommitSha = commitSha;
        ViewLiveUrl = viewLiveUrl;
        RunUrl = null;
        ErrorMessage = null;
        Changed?.Invoke();

        _ = PollAsync(commitSha, _cts.Token);
    }

    /// <summary>Stop tracking and reset the banner. Called by the dismiss button.</summary>
    public void Dismiss()
    {
        _cts?.Cancel();
        State = DeployState.Idle;
        CommitSha = null;
        ViewLiveUrl = null;
        RunUrl = null;
        ErrorMessage = null;
        Changed?.Invoke();
    }

    private async Task PollAsync(string commitSha, CancellationToken ct)
    {
        DateTime started = DateTime.UtcNow;
        while (!ct.IsCancellationRequested && DateTime.UtcNow - started < Timeout)
        {
            try
            {
                WorkflowRun? run = await api.GetLatestWorkflowRunForCommitAsync(commitSha, ct);
                if (run is not null)
                {
                    RunUrl = run.HtmlUrl;
                    DeployState next = (run.Status, run.Conclusion) switch
                    {
                        ("completed", "success")   => DeployState.Success,
                        ("completed", _)           => DeployState.Failure,
                        _                          => DeployState.Running,
                    };
                    if (next != State)
                    {
                        State = next;
                        if (next == DeployState.Failure)
                        {
                            ErrorMessage = $"Build {run.Conclusion ?? "failed"}.";
                        }
                        Changed?.Invoke();
                    }
                    if (next is DeployState.Success or DeployState.Failure)
                    {
                        return;
                    }
                }
            }
            catch
            {
                // Transient network / rate-limit hiccups shouldn't kill the watcher — let the next tick try.
            }
            try { await Task.Delay(PollInterval, ct); }
            catch (TaskCanceledException) { return; }
        }

        if (!ct.IsCancellationRequested && State != DeployState.Success && State != DeployState.Failure)
        {
            State = DeployState.Timeout;
            ErrorMessage = $"Still building after {Timeout.TotalMinutes:N0} minutes — check the Actions log.";
            Changed?.Invoke();
        }
    }
}

public enum DeployState
{
    Idle,
    /// <summary>Commit landed; waiting for the workflow run to register on GitHub.</summary>
    Pending,
    /// <summary>Workflow run is queued or actively building.</summary>
    Running,
    Success,
    Failure,
    /// <summary>Polling gave up after the time budget — user can check the Actions log directly.</summary>
    Timeout
}
