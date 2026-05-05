using JmcLogConsole.Core;

namespace JmcLogConsole.ViewModels;

public sealed class LogViewportModel
{
    private readonly List<LogEntry> filteredEntries = [];
    private LogFilter filter = LogFilter.Create(string.Empty);
    private LogCaptureService.LogBufferInfo bufferInfo;
    private int sourceVersion = -1;
    private long processedSequence;
    private int firstVisibleRow;

    public bool FollowTail { get; private set; } = true;
    public string FilterPattern => filter.Pattern;
    public string? FilterError => filter.Error;
    public bool HasFilter => filter.HasPattern;

    public void Clear()
    {
        filteredEntries.Clear();
        bufferInfo = LogCaptureService.GetInfo();
        sourceVersion = bufferInfo.Version;
        processedSequence = bufferInfo.LastSequence;
        firstVisibleRow = 0;
        FollowTail = true;
    }

    public void SetFilter(string? pattern, LogLineFormatter formatter)
    {
        filter = LogFilter.Create(pattern);
        firstVisibleRow = 0;
        FollowTail = true;
        Rebuild(formatter);
    }

    public void Refresh(LogLineFormatter formatter)
    {
        LogCaptureService.LogBufferInfo latestInfo = LogCaptureService.GetInfo();
        if (latestInfo.Version == sourceVersion)
        {
            bufferInfo = latestInfo;
            return;
        }

        bufferInfo = latestInfo;
        sourceVersion = latestInfo.Version;

        if (!filter.HasPattern)
        {
            processedSequence = latestInfo.LastSequence;
            ClampFirstVisibleRow(GetTotalRowsNoRefresh());
            return;
        }

        if (!filter.IsUsable)
        {
            filteredEntries.Clear();
            processedSequence = latestInfo.LastSequence;
            firstVisibleRow = 0;
            return;
        }

        if (latestInfo.Count == 0)
        {
            filteredEntries.Clear();
            processedSequence = latestInfo.LastSequence;
            firstVisibleRow = 0;
            return;
        }

        filteredEntries.RemoveAll(entry => entry.Sequence < latestInfo.FirstSequence);

        foreach (LogEntry entry in LogCaptureService.GetEntriesAfter(processedSequence))
        {
            processedSequence = Math.Max(processedSequence, entry.Sequence);
            if (filter.Matches(formatter.Format(entry)))
            {
                filteredEntries.Add(entry);
            }

            if (!filter.IsUsable)
            {
                filteredEntries.Clear();
                firstVisibleRow = 0;
                return;
            }
        }

        ClampFirstVisibleRow(GetTotalRowsNoRefresh());
    }

    public LogViewportSnapshot CreateSnapshot(int requestedFirstRow, int requestedRowCount, LogLineFormatter formatter)
    {
        return CreateSnapshot(requestedFirstRow, requestedRowCount, formatter, wrapColumns: 0);
    }

    public LogViewportSnapshot CreateSnapshot(int requestedFirstRow, int requestedRowCount, LogLineFormatter formatter, int wrapColumns)
    {
        Refresh(formatter);

        if (filter.HasPattern && !filter.IsUsable)
        {
            return LogViewportSnapshot.Empty(FollowTail, filter.Error);
        }

        int totalRows = GetTotalRowsForLayout(formatter, wrapColumns);
        if (totalRows <= 0)
        {
            firstVisibleRow = 0;
            return LogViewportSnapshot.Empty(FollowTail, filter.Error);
        }

        int rowCount = Math.Max(1, requestedRowCount);
        int maxFirstRow = Math.Max(0, totalRows - rowCount);
        firstVisibleRow = FollowTail
            ? maxFirstRow
            : Math.Clamp(firstVisibleRow, 0, maxFirstRow);

        IReadOnlyList<LogRenderLine> renderLines = wrapColumns > 0
            ? CreateWrappedRenderLines(firstVisibleRow, rowCount, formatter, wrapColumns)
            : CreateEntryRenderLines(firstVisibleRow, rowCount, formatter);

        int lastRow = renderLines.Count == 0
            ? firstVisibleRow
            : firstVisibleRow + renderLines.Count - 1;
        return new LogViewportSnapshot(
            renderLines,
            bufferInfo.Count,
            HasFilter ? filteredEntries.Count : bufferInfo.Count,
            totalRows,
            firstVisibleRow,
            lastRow,
            FollowTail,
            filter.Error);
    }

    public void SetFirstVisibleRow(int value, int viewportRows)
    {
        SetFirstVisibleRow(value, viewportRows, null, 0);
    }

    public void SetFirstVisibleRow(int value, int viewportRows, LogLineFormatter? formatter, int wrapColumns)
    {
        int totalRows = formatter == null
            ? GetTotalRowsNoRefresh()
            : GetTotalRowsForLayout(formatter, wrapColumns);
        int maxFirstRow = Math.Max(0, totalRows - Math.Max(1, viewportRows));
        firstVisibleRow = Math.Clamp(value, 0, maxFirstRow);
        FollowTail = firstVisibleRow >= maxFirstRow;
    }

    public void ScrollByRows(int deltaRows, int viewportRows)
    {
        SetFirstVisibleRow(firstVisibleRow + deltaRows, viewportRows);
    }

    public void ScrollByRows(int deltaRows, int viewportRows, LogLineFormatter formatter, int wrapColumns)
    {
        SetFirstVisibleRow(firstVisibleRow + deltaRows, viewportRows, formatter, wrapColumns);
    }

    public void ScrollToStart()
    {
        firstVisibleRow = 0;
        FollowTail = false;
    }

    public void ScrollToEnd()
    {
        FollowTail = true;
    }

    public string BuildPlainText(LogLineFormatter formatter, bool filteredOnly)
    {
        Refresh(formatter);

        if (filteredOnly && filter.HasPattern)
        {
            return filter.IsUsable
                ? formatter.BuildPlainText(filteredEntries)
                : string.Empty;
        }

        return formatter.BuildPlainText(LogCaptureService.Snapshot());
    }

    public IReadOnlyList<LogRenderLine> CreateRenderLinesForRows(int startRow, int rowCount, LogLineFormatter formatter)
    {
        return CreateRenderLinesForRows(startRow, rowCount, formatter, wrapColumns: 0);
    }

    public IReadOnlyList<LogRenderLine> CreateRenderLinesForRows(int startRow, int rowCount, LogLineFormatter formatter, int wrapColumns)
    {
        Refresh(formatter);

        if (wrapColumns > 0)
        {
            return CreateWrappedRenderLines(startRow, Math.Max(1, rowCount), formatter, wrapColumns);
        }

        return CreateEntryRenderLines(startRow, rowCount, formatter);
    }

    private IReadOnlyList<LogRenderLine> CreateEntryRenderLines(int startRow, int rowCount, LogLineFormatter formatter)
    {
        IReadOnlyList<LogEntry> entries = GetEntriesForRows(startRow, Math.Max(1, rowCount));
        var renderLines = new List<LogRenderLine>(entries.Count);
        foreach (LogEntry entry in entries)
        {
            renderLines.Add(LogRenderLine.FromEntry(entry, formatter.Format(entry)));
        }

        return renderLines;
    }

    private IReadOnlyList<LogRenderLine> CreateWrappedRenderLines(int startRow, int rowCount, LogLineFormatter formatter, int wrapColumns)
    {
        int safeWrapColumns = Math.Max(1, wrapColumns);
        int endRow = startRow + Math.Max(1, rowCount);
        int visualRow = 0;
        var renderLines = new List<LogRenderLine>(rowCount);

        foreach (LogEntry entry in GetCurrentEntries())
        {
            LogRenderLine line = LogRenderLine.FromEntry(entry, formatter.Format(entry));
            int wrappedCount = GetWrappedLineCount(line.Text, safeWrapColumns);
            if (visualRow + wrappedCount <= startRow)
            {
                visualRow += wrappedCount;
                continue;
            }

            foreach (string wrappedText in WrapText(line.Text, safeWrapColumns))
            {
                if (visualRow >= startRow && visualRow < endRow)
                {
                    renderLines.Add(new LogRenderLine(line.Sequence, wrappedText, line.Level, line.Color));
                }

                visualRow++;
                if (visualRow >= endRow)
                {
                    return renderLines;
                }
            }
        }

        return renderLines;
    }

    public int GetTotalRows(LogLineFormatter formatter)
    {
        Refresh(formatter);
        return GetTotalRowsNoRefresh();
    }

    private void Rebuild(LogLineFormatter formatter)
    {
        filteredEntries.Clear();
        bufferInfo = LogCaptureService.GetInfo();
        sourceVersion = bufferInfo.Version;
        processedSequence = bufferInfo.LastSequence;

        if (!filter.HasPattern || !filter.IsUsable)
        {
            return;
        }

        foreach (LogEntry entry in LogCaptureService.Snapshot())
        {
            if (filter.Matches(formatter.Format(entry)))
            {
                filteredEntries.Add(entry);
            }

            if (!filter.IsUsable)
            {
                filteredEntries.Clear();
                return;
            }
        }
    }

    private IReadOnlyList<LogEntry> GetEntriesForRows(int startRow, int rowCount)
    {
        if (filter.HasPattern)
        {
            if (!filter.IsUsable || startRow >= filteredEntries.Count)
            {
                return [];
            }

            int take = Math.Min(rowCount, filteredEntries.Count - startRow);
            return filteredEntries.GetRange(startRow, take);
        }

        return LogCaptureService.GetRange(startRow, rowCount);
    }

    private IEnumerable<LogEntry> GetCurrentEntries()
    {
        if (filter.HasPattern)
        {
            return filter.IsUsable ? filteredEntries : [];
        }

        return LogCaptureService.Snapshot();
    }

    private int GetTotalRowsForLayout(LogLineFormatter formatter, int wrapColumns)
    {
        if (wrapColumns <= 0)
        {
            return GetTotalRowsNoRefresh();
        }

        int totalRows = 0;
        int safeWrapColumns = Math.Max(1, wrapColumns);
        foreach (LogEntry entry in GetCurrentEntries())
        {
            string text = LogRenderLine.FromEntry(entry, formatter.Format(entry)).Text;
            totalRows += GetWrappedLineCount(text, safeWrapColumns);
        }

        return totalRows;
    }

    private static int GetWrappedLineCount(string text, int wrapColumns)
    {
        int totalRows = 0;
        foreach (string line in SplitDisplayLines(text))
        {
            totalRows += line.Length == 0 ? 1 : (line.Length + wrapColumns - 1) / wrapColumns;
        }

        return Math.Max(1, totalRows);
    }

    private static IEnumerable<string> WrapText(string text, int wrapColumns)
    {
        foreach (string line in SplitDisplayLines(text))
        {
            if (line.Length == 0)
            {
                yield return string.Empty;
                continue;
            }

            for (int start = 0; start < line.Length; start += wrapColumns)
            {
                int length = Math.Min(wrapColumns, line.Length - start);
                yield return line.Substring(start, length);
            }
        }
    }

    private static IEnumerable<string> SplitDisplayLines(string text)
    {
        if (text.Length == 0)
        {
            yield return string.Empty;
            yield break;
        }

        int start = 0;
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] != '\n')
            {
                continue;
            }

            yield return text[start..i];
            start = i + 1;
        }

        if (start <= text.Length)
        {
            yield return text[start..];
        }
    }

    private int GetTotalRowsNoRefresh()
    {
        if (filter.HasPattern)
        {
            return filter.IsUsable ? filteredEntries.Count : 0;
        }

        return bufferInfo.Count;
    }

    private void ClampFirstVisibleRow(int totalRows)
    {
        if (FollowTail)
        {
            return;
        }

        firstVisibleRow = Math.Clamp(firstVisibleRow, 0, Math.Max(0, totalRows - 1));
    }
}
