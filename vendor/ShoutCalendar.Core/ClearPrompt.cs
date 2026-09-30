namespace ShoutCalendar.Core;

public enum ClearTarget
{
    All,
    Accepted,
    Unaccepted,
    SyncAccepted,
    SyncUnaccepted,
    LocalPast,
    SyncPast,
}

public sealed class ClearPrompt
{
    public bool IsOpen { get; private set; }

    public ClearTarget Target { get; private set; }

    public void Ask() => this.Ask(ClearTarget.All);

    public void Ask(ClearTarget target)
    {
        this.Target = target;
        this.IsOpen = true;
    }

    public void AnswerNo() => this.IsOpen = false;

    public string Question(IReadOnlyList<string>? openServers, string? home)
    {
        var action = this.Target switch
        {
            ClearTarget.Accepted => "Clear every accepted event on this computer?",
            ClearTarget.Unaccepted => "Clear every unaccepted invite on this computer?",
            ClearTarget.SyncAccepted => "Clear every accepted shared invite on this computer?",
            ClearTarget.SyncUnaccepted => "Clear every unaccepted shared invite on this computer?",
            ClearTarget.LocalPast => "Delete local events whose end time has passed on this computer?",
            ClearTarget.SyncPast => "Delete shared events whose end time has passed on this computer?",
            _ => "Clear every local event on this computer?",
        };
        if (this.Target is ClearTarget.LocalPast or ClearTarget.SyncPast)
            return action;
        if (openServers is null || openServers.Count == 0)
            return action;
        var names = string.Join(", ", openServers);
        var scope = openServers.Count > 1 && !string.IsNullOrWhiteSpace(home)
            ? $" This clears events on all of those calendars, not only {home}."
            : " This clears events on that calendar.";
        return $"{action} You currently have the calendars for the following servers open: {names}.{scope}";
    }

    public void AnswerYes(CalendarLog log)
    {
        switch (this.Target)
        {
            case ClearTarget.Accepted:
                log.ClearAccepted();
                break;
            case ClearTarget.Unaccepted:
                log.ClearUnaccepted();
                break;
            case ClearTarget.LocalPast:
                log.ClearPast(DateTime.Now);
                break;
            case ClearTarget.SyncAccepted:
            case ClearTarget.SyncUnaccepted:
            case ClearTarget.SyncPast:
                break;
            default:
                log.Clear();
                break;
        }

        this.IsOpen = false;
    }
}
