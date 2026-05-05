using System.Text;
using Godot;
using MegaCrit.Sts2.Core.Logging;

namespace JmcLogConsole.Core;

public static class LogCaptureService
{
    private static readonly object Gate = new();
    private static readonly Queue<LogEntry> Entries = new();
    private static readonly List<LogEntry> StartupBufferedEntries = new();
    private static bool startupReplayInProgress;
    private static bool initialized;
    private static int version;
    private static long nextSequence;

    public static event Action? Changed;

    public static int Version => Volatile.Read(ref version);

    public static void Initialize()
    {
        if (initialized)
        {
            return;
        }

        StartupLogFileSnapshot? startupLogSnapshot = CreateStartupLogFileSnapshot();
        startupReplayInProgress = startupLogSnapshot.HasValue;

        Log.LogCallback += OnLogCallback;
        initialized = true;

        bool changed = false;
        try
        {
            changed = ImportStartupLogFile(startupLogSnapshot);
        }
        finally
        {
            changed |= FlushStartupBufferedEntries();
        }

        if (changed)
        {
            NotifyChanged();
        }
    }

    public static void Shutdown()
    {
        if (!initialized)
        {
            return;
        }

        Log.LogCallback -= OnLogCallback;
        initialized = false;
        ResetStartupReplay();
        Clear();
        Changed = null;
    }

    public static void Clear()
    {
        lock (Gate)
        {
            Entries.Clear();
            Interlocked.Increment(ref version);
        }

        NotifyChanged();
    }

    public static LogEntry[] Snapshot()
    {
        lock (Gate)
        {
            return Entries.ToArray();
        }
    }

    public static LogBufferInfo GetInfo()
    {
        lock (Gate)
        {
            return CreateInfoNoLock();
        }
    }

    public static LogEntry[] GetRange(int startIndex, int count)
    {
        if (count <= 0)
        {
            return [];
        }

        lock (Gate)
        {
            if (startIndex < 0 || startIndex >= Entries.Count)
            {
                return [];
            }

            int take = Math.Min(count, Entries.Count - startIndex);
            return Entries.Skip(startIndex).Take(take).ToArray();
        }
    }

    public static LogEntry[] GetEntriesAfter(long sequence)
    {
        lock (Gate)
        {
            return Entries.Where(entry => entry.Sequence > sequence).ToArray();
        }
    }

    private static void OnLogCallback(LogLevel level, string message, int skipFrames)
    {
        // 这个回调在游戏 Logger 锁内触发；这里必须尽量轻量，不能直接操作 Godot UI，也不要再打日志。
        try
        {
            LogEntry entry = new(0, DateTime.Now, level, message ?? string.Empty);
            if (AppendCallbackEntryNoNotify(entry))
            {
                NotifyChanged();
            }
        }
        catch
        {
            // 日志系统内部不再抛出异常，避免影响游戏 Logger。
        }
    }

    private static StartupLogFileSnapshot? CreateStartupLogFileSnapshot()
    {
        if (!LogConsoleSettings.EnableCapture || !LogConsoleSettings.ImportStartupLogFile)
        {
            return null;
        }

        try
        {
            FileInfo? logFile = FindCurrentLogFile();
            if (logFile == null)
            {
                return null;
            }

            logFile.Refresh();
            return logFile.Length <= 0
                ? null
                : new StartupLogFileSnapshot(logFile.FullName, logFile.Length, logFile.LastWriteTime);
        }
        catch
        {
            return null;
        }
    }

    private static bool ImportStartupLogFile(StartupLogFileSnapshot? snapshot)
    {
        if (!snapshot.HasValue || !LogConsoleSettings.EnableCapture || !LogConsoleSettings.ImportStartupLogFile)
        {
            return false;
        }

        try
        {
            StartupLogFileSnapshot logSnapshot = snapshot.Value;
            long maxBytes = Math.Clamp(LogConsoleSettings.StartupLogFileTailKilobytes, 64, 8192) * 1024L;
            string text = ReadTailText(logSnapshot.Path, maxBytes, logSnapshot.Length, out bool truncated);
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            DateTime timestamp = logSnapshot.LastWriteTime;
            bool changed = false;

            string header = truncated
                ? $"[LogConsole] 已导入当前游戏日志文件末尾约 {maxBytes / 1024} KB: {logSnapshot.Path}"
                : $"[LogConsole] 已导入当前游戏日志文件: {logSnapshot.Path}";
            changed |= AppendEntryNoNotify(new LogEntry(0, timestamp, LogLevel.Info, header));

            foreach (LogEntry entry in ParseLogFileText(text, timestamp))
            {
                changed |= AppendEntryNoNotify(entry);
            }

            return changed;
        }
        catch
        {
            // 启动日志导入是兜底能力，失败时保持实时 LogCallback 捕获可用。
            return false;
        }
    }

    private static FileInfo? FindCurrentLogFile()
    {
        string? logDirectory = GetLogDirectory();
        if (string.IsNullOrWhiteSpace(logDirectory) || !Directory.Exists(logDirectory))
        {
            return null;
        }

        return Directory.EnumerateFiles(logDirectory, "*.log", SearchOption.TopDirectoryOnly)
            .Select(path => new FileInfo(path))
            .Where(file => file.Exists && file.Length > 0)
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .FirstOrDefault();
    }

    private static string? GetLogDirectory()
    {
        try
        {
            string userPath = ProjectSettings.GlobalizePath("user://");
            if (!string.IsNullOrWhiteSpace(userPath))
            {
                return Path.Combine(userPath, "logs");
            }
        }
        catch
        {
        }

        string appData = System.Environment.GetFolderPath(System.Environment.SpecialFolder.ApplicationData);
        return string.IsNullOrWhiteSpace(appData)
            ? null
            : Path.Combine(appData, "SlayTheSpire2", "logs");
    }

    private static string ReadTailText(string path, long maxBytes, long endOffset, out bool truncated)
    {
        using var stream = new FileStream(path, FileMode.Open, System.IO.FileAccess.Read, FileShare.ReadWrite);
        truncated = false;

        long endPosition = Math.Clamp(endOffset, 0L, stream.Length);
        if (endPosition <= 0)
        {
            return string.Empty;
        }

        long startPosition = 0;
        if (maxBytes > 0 && endPosition > maxBytes)
        {
            startPosition = endPosition - maxBytes;
            truncated = true;
        }

        stream.Seek(startPosition, SeekOrigin.Begin);
        if (truncated)
        {
            SkipPartialUtf8Sequence(stream, endPosition);
        }

        long byteCount = endPosition - stream.Position;
        if (byteCount <= 0)
        {
            return string.Empty;
        }

        var buffer = new byte[byteCount];
        int totalRead = 0;
        while (totalRead < buffer.Length)
        {
            int read = stream.Read(buffer, totalRead, buffer.Length - totalRead);
            if (read <= 0)
            {
                break;
            }

            totalRead += read;
        }

        using var memory = new MemoryStream(buffer, 0, totalRead, writable: false);
        using var reader = new StreamReader(memory, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
        string text = reader.ReadToEnd();

        if (truncated)
        {
            int firstNewline = text.IndexOf('\n');
            if (firstNewline >= 0 && firstNewline + 1 < text.Length)
            {
                text = text[(firstNewline + 1)..];
            }
        }

        return text;
    }

    private static void SkipPartialUtf8Sequence(Stream stream, long endPosition)
    {
        while (stream.Position < endPosition)
        {
            int value = stream.ReadByte();
            if (value == -1)
            {
                return;
            }

            if ((value & 0xC0) != 0x80)
            {
                stream.Seek(-1L, SeekOrigin.Current);
                return;
            }
        }
    }

    private static IEnumerable<LogEntry> ParseLogFileText(string text, DateTime timestamp)
    {
        var entries = new List<LogEntry>();
        using var reader = new StringReader(text);

        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            if (TryParseLogLine(line, timestamp, out LogEntry entry))
            {
                entries.Add(entry);
                continue;
            }

            if (entries.Count > 0 && IsContinuationLine(line))
            {
                LogEntry previous = entries[^1];
                entries[^1] = new LogEntry(previous.Sequence, previous.Time, previous.Level, previous.Message + "\n" + line);
                continue;
            }

            entries.Add(new LogEntry(0, timestamp, LogLevel.Info, line));
        }

        return entries;
    }

    private static bool TryParseLogLine(string line, DateTime timestamp, out LogEntry entry)
    {
        entry = default;

        if (line.StartsWith('['))
        {
            int closeIndex = line.IndexOf(']');
            if (closeIndex > 1
                && TryParseLevelToken(line[1..closeIndex], out LogLevel bracketLevel))
            {
                string message = line[(closeIndex + 1)..].TrimStart();
                entry = new LogEntry(0, timestamp, bracketLevel, message);
                return true;
            }
        }

        int colonIndex = line.IndexOf(':');
        if (colonIndex > 0
            && TryParseLevelToken(line[..colonIndex], out LogLevel colonLevel))
        {
            string message = line[(colonIndex + 1)..].TrimStart();
            entry = new LogEntry(0, timestamp, colonLevel, message);
            return true;
        }

        return false;
    }

    private static bool TryParseLevelToken(string token, out LogLevel level)
    {
        switch (token.Trim().Replace("_", string.Empty).Replace(" ", string.Empty).ToUpperInvariant())
        {
            case "VERYDEBUG":
                level = LogLevel.VeryDebug;
                return true;
            case "LOAD":
                level = LogLevel.Load;
                return true;
            case "DEBUG":
                level = LogLevel.Debug;
                return true;
            case "INFO":
                level = LogLevel.Info;
                return true;
            case "WARN":
            case "WARNING":
                level = LogLevel.Warn;
                return true;
            case "ERROR":
                level = LogLevel.Error;
                return true;
            default:
                level = LogLevel.Info;
                return false;
        }
    }

    private static bool IsContinuationLine(string line)
    {
        return line.StartsWith(' ') || line.StartsWith('\t');
    }

    private static bool AppendCallbackEntryNoNotify(LogEntry entry)
    {
        if (!ShouldCapture(entry.Level))
        {
            return false;
        }

        lock (Gate)
        {
            if (startupReplayInProgress)
            {
                StartupBufferedEntries.Add(entry);
                return false;
            }

            Entries.Enqueue(AssignSequenceNoLock(entry));
            TrimNoLock();
            Interlocked.Increment(ref version);
        }

        return true;
    }

    private static bool AppendEntryNoNotify(LogEntry entry)
    {
        if (!ShouldCapture(entry.Level))
        {
            return false;
        }

        lock (Gate)
        {
            Entries.Enqueue(AssignSequenceNoLock(entry));
            TrimNoLock();
            Interlocked.Increment(ref version);
        }

        return true;
    }

    private static bool FlushStartupBufferedEntries()
    {
        lock (Gate)
        {
            startupReplayInProgress = false;
            if (StartupBufferedEntries.Count == 0)
            {
                return false;
            }

            foreach (LogEntry entry in StartupBufferedEntries)
            {
                Entries.Enqueue(AssignSequenceNoLock(entry));
            }

            StartupBufferedEntries.Clear();
            TrimNoLock();
            Interlocked.Increment(ref version);
            return true;
        }
    }

    private static void ResetStartupReplay()
    {
        lock (Gate)
        {
            startupReplayInProgress = false;
            StartupBufferedEntries.Clear();
        }
    }

    private static bool ShouldCapture(LogLevel level)
    {
        return LogConsoleSettings.EnableCapture && level >= LogConsoleSettings.MinimumLevel;
    }

    private readonly record struct StartupLogFileSnapshot(
        string Path,
        long Length,
        DateTime LastWriteTime);

    private static void NotifyChanged()
    {
        try
        {
            Changed?.Invoke();
        }
        catch
        {
            // 不能让 UI 刷新异常反向影响日志系统。
        }
    }

    private static void TrimNoLock()
    {
        int maxLines = Math.Clamp(LogConsoleSettings.MaxLines, 1, 100000);
        while (Entries.Count > maxLines)
        {
            Entries.Dequeue();
        }
    }

    private static LogEntry AssignSequenceNoLock(LogEntry entry)
    {
        return entry.Sequence > 0
            ? entry
            : entry with { Sequence = ++nextSequence };
    }

    private static LogBufferInfo CreateInfoNoLock()
    {
        return Entries.Count == 0
            ? new LogBufferInfo(0, 0, 0, version)
            : new LogBufferInfo(Entries.Count, Entries.Peek().Sequence, Entries.Last().Sequence, version);
    }

    public readonly record struct LogBufferInfo(
        int Count,
        long FirstSequence,
        long LastSequence,
        int Version);
}
