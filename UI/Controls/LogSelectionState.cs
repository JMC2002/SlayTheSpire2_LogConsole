namespace JmcLogConsole.UI.Controls;

public readonly record struct LogTextPosition(int Row, int Column)
{
    public static int Compare(LogTextPosition left, LogTextPosition right)
    {
        int rowCompare = left.Row.CompareTo(right.Row);
        return rowCompare != 0 ? rowCompare : left.Column.CompareTo(right.Column);
    }
}

public sealed class LogSelectionState
{
    private LogTextPosition? anchor;
    private LogTextPosition? cursor;

    public bool IsDragging { get; private set; }

    public bool HasSelection
    {
        get
        {
            if (anchor == null || cursor == null)
            {
                return false;
            }

            return LogTextPosition.Compare(anchor.Value, cursor.Value) != 0;
        }
    }

    public void Begin(LogTextPosition position)
    {
        anchor = position;
        cursor = position;
        IsDragging = true;
    }

    public void Update(LogTextPosition position)
    {
        if (anchor == null)
        {
            Begin(position);
            return;
        }

        cursor = position;
    }

    public void End(LogTextPosition position)
    {
        Update(position);
        IsDragging = false;
    }

    public bool TryGetOrderedRange(out LogTextPosition start, out LogTextPosition end)
    {
        start = default;
        end = default;

        if (!HasSelection || anchor == null || cursor == null)
        {
            return false;
        }

        LogTextPosition first = anchor.Value;
        LogTextPosition second = cursor.Value;
        if (LogTextPosition.Compare(first, second) <= 0)
        {
            start = first;
            end = second;
        }
        else
        {
            start = second;
            end = first;
        }

        return true;
    }

    public void Clear()
    {
        anchor = null;
        cursor = null;
        IsDragging = false;
    }
}
