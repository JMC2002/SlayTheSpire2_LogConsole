using MegaCrit.Sts2.Core.Logging;

namespace JmcLogConsole.Core;

public static class LogCaptureService
{
    private static readonly object Gate = new();
    private static readonly Queue<LogEntry> Entries = new();
    private static bool initialized;
    private static int version;

    public static event Action? Changed;

    public static int Version => Volatile.Read(ref version);

    public static void Initialize()
    {
        if (initialized)
        {
            return;
        }

        Log.LogCallback += OnLogCallback;
        initialized = true;
    }

    public static void Shutdown()
    {
        if (!initialized)
        {
            return;
        }

        Log.LogCallback -= OnLogCallback;
        initialized = false;
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

    private static void OnLogCallback(LogLevel level, string message, int skipFrames)
    {
        // 这个回调在游戏 Logger 锁内触发；这里必须尽量轻量，不能直接操作 Godot UI，也不要再打日志。
        try
        {
            if (!LogConsoleSettings.EnableCapture || level < LogConsoleSettings.MinimumLevel)
            {
                return;
            }

            lock (Gate)
            {
                Entries.Enqueue(new LogEntry(DateTime.Now, level, message ?? string.Empty));
                TrimNoLock();
                Interlocked.Increment(ref version);
            }

            NotifyChanged();
        }
        catch
        {
            // 日志系统内部不再抛出异常，避免影响游戏 Logger。
        }
    }

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
}
