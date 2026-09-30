namespace ShoutCalendar.Core;

public sealed record SyncIngestReply(int Added, SyncFillReport? Report);
public sealed record SyncStatusUpdate(string Status, string? PerformanceLine = null, bool ResetDismissed = false, SyncProgress? Progress = null);

public enum SyncPhase { Starting, Waiting, Uploading, Downloading, Applying, Current, CatchingUp, Retry, Upgrade, Paused }

public sealed record SyncProgress(SyncPhase Phase, DateTimeOffset At, string Relay = "", DateTimeOffset? LastSuccess = null,
    DateTimeOffset? NextAttempt = null, int Catalog = 0, int Applied = 0, int Waiting = 0,
    int DownloadBytes = 0, int UploadBytes = 0, string Detail = "")
{
    public string LastSuccessSummary(DateTimeOffset now)
    {
        var relay = this.Relay.Length > 0 ? " · " + this.Relay : "";
        if (this.LastSuccess is not DateTimeOffset at)
            return "No successful check yet" + relay;
        var seconds = Math.Max(0, (long)(now - at).TotalSeconds);
        var (value, unit) = seconds < 60 ? (seconds, "second")
            : seconds < 3600 ? (seconds / 60, "minute") : (seconds / 3600, "hour");
        return $"Last success {at.LocalDateTime:HH:mm:ss}{relay} ({value} {unit}{(value == 1 ? "" : "s")} ago)";
    }

    public bool IsStale(DateTimeOffset now) => this.Phase is SyncPhase.Current or SyncPhase.CatchingUp
        && now - this.At > TimeSpan.FromSeconds(45);

    public string Summary(DateTimeOffset now) => this.IsStale(now) ? "Waiting for a fresh sync check" : this.Phase switch
    {
        SyncPhase.Current => $"Up to date · {this.Applied} applied · {this.Catalog} catalog invites",
        SyncPhase.CatchingUp => $"Catching up · {this.Applied} applied · {this.Waiting} waiting",
        SyncPhase.Retry => $"Connection interrupted · retry in {Math.Max(0, Math.Ceiling(((this.NextAttempt ?? now) - now).TotalSeconds)):0}s · saved events available",
        SyncPhase.Upgrade => "Update required · install the matching Calendar and Sync versions",
        SyncPhase.Waiting => $"Waiting after login · {Math.Max(0, Math.Ceiling(((this.NextAttempt ?? now) - now).TotalSeconds)):0}s",
        SyncPhase.Uploading => "Sharing new or updated invitations…",
        SyncPhase.Downloading => "Checking relay for invitations and changes…",
        SyncPhase.Applying => "Applying invitation updates…",
        SyncPhase.Paused => "Paused · waiting for a playable world",
        _ => "Connecting…",
    };
}
