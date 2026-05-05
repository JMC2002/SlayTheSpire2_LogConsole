using System.Text;

namespace JmcLogConsole.Core;

public sealed class LogLineFormatter
{
    public bool ShowTimestamp { get; init; } = true;
    public bool ShowLevel { get; init; } = true;

    public static LogLineFormatter FromSettings()
    {
        return new LogLineFormatter
        {
            ShowTimestamp = LogConsoleSettings.ShowTimestamp,
            ShowLevel = LogConsoleSettings.ShowLevel
        };
    }

    public string Format(LogEntry entry)
    {
        var builder = new StringBuilder(128 + entry.Message.Length);

        if (ShowTimestamp)
        {
            builder.Append('[')
                .Append(entry.Time.ToString("HH:mm:ss.fff"))
                .Append("] ");
        }

        if (ShowLevel)
        {
            builder.Append('[')
                .Append(entry.Level)
                .Append("] ");
        }

        builder.Append(NormalizeNewlines(entry.Message));
        return builder.ToString();
    }

    public string BuildPlainText(IEnumerable<LogEntry> entries)
    {
        var builder = new StringBuilder();
        foreach (LogEntry entry in entries)
        {
            builder.AppendLine(Format(entry));
        }

        return builder.ToString();
    }

    public static string NormalizeNewlines(string value)
    {
        return value.Replace("\r\n", "\n").Replace('\r', '\n');
    }
}
