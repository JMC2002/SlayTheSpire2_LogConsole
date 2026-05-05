using Godot;
using MegaCrit.Sts2.Core.Logging;

namespace JmcLogConsole.ViewModels;

public readonly record struct LogRenderLine(
    long Sequence,
    string Text,
    LogLevel Level,
    Color Color)
{
    public static LogRenderLine FromEntry(JmcLogConsole.Core.LogEntry entry, string text)
    {
        return new LogRenderLine(entry.Sequence, NormalizeDisplayNewlines(text), entry.Level, ColorFor(entry.Level));
    }

    private static string NormalizeDisplayNewlines(string text)
    {
        return text.Replace("\r\n", "\n")
            .Replace('\r', '\n')
            .Replace(" \\r\\n ", "\n")
            .Replace(" \\n ", "\n");
    }

    private static Color ColorFor(LogLevel level)
    {
        return level switch
        {
            LogLevel.Error => new Color(1.00f, 0.42f, 0.42f),
            LogLevel.Warn => new Color(1.00f, 0.82f, 0.40f),
            LogLevel.Info => new Color(0.82f, 0.85f, 0.86f),
            LogLevel.Debug => new Color(0.56f, 0.79f, 0.90f),
            LogLevel.Load => new Color(0.72f, 0.89f, 0.78f),
            LogLevel.VeryDebug => new Color(0.54f, 0.57f, 0.60f),
            _ => new Color(0.82f, 0.85f, 0.86f)
        };
    }
}
