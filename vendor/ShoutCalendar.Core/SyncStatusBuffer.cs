namespace ShoutCalendar.Core;

public sealed record SyncStatusView(string Status, string RelayStatus, SyncProgress? Progress);

public sealed class SyncStatusBuffer
{
    private string? bookId;
    private long? changedAt;

    public SyncStatusView? Current { get; private set; }

    public SyncStatusView? Update(SyncBook? book, long milliseconds)
    {
        if (book is null)
        {
            this.bookId = null;
            this.changedAt = null;
            return this.Current = null;
        }

        if (this.Current is null || this.bookId != book.BookId)
        {
            this.bookId = book.BookId;
            this.changedAt = null;
            return this.Current = new(book.SyncStatus, book.RelayStatus, book.Progress);
        }

        if (this.Current.Status == book.SyncStatus && this.Current.RelayStatus == book.RelayStatus
            && this.Current.Progress == book.Progress)
            this.changedAt = null;
        else
        {
            this.changedAt ??= milliseconds;
            if (milliseconds - this.changedAt.Value >= 100)
            {
                this.Current = new(book.SyncStatus, book.RelayStatus, book.Progress);
                this.changedAt = null;
            }
        }
        return this.Current;
    }
}
