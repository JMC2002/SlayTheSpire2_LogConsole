using System.Globalization;
using System.Text;
using Godot;
using JmcModLib.Utils;

namespace JmcLogConsole.Core;

internal static class DisplayDiagnostics
{
    private const ulong SnapshotThrottleMs = 5000;

    private static string? lastSnapshot;
    private static ulong lastSnapshotTicks;

    public static void LogDisplaySnapshot(string context, bool force = false)
    {
        if (!LogConsoleSettings.EnableWindowDiagnostics)
        {
            return;
        }

        string snapshot = BuildDisplaySnapshot();
        ulong now = Time.GetTicksMsec();
        if (!force
            && string.Equals(snapshot, lastSnapshot, StringComparison.Ordinal)
            && now - lastSnapshotTicks < SnapshotThrottleMs)
        {
            return;
        }

        lastSnapshot = snapshot;
        lastSnapshotTicks = now;
        ModLogger.Debug($"[LogConsole.DisplayDiag] {context}\n{snapshot}");
    }

    public static void LogWindowState(
        string context,
        Window? window = null,
        Window? root = null,
        int? targetScreen = null,
        string? selectedOption = null)
    {
        if (!LogConsoleSettings.EnableWindowDiagnostics)
        {
            return;
        }

        var builder = new StringBuilder(512);
        builder.Append("[LogConsole.WindowDiag] ")
            .Append(context);

        if (!string.IsNullOrWhiteSpace(selectedOption))
        {
            builder.Append(" option=\"")
                .Append(selectedOption)
                .Append('"');
        }

        if (targetScreen.HasValue)
        {
            builder.Append(" targetScreen=")
                .Append(targetScreen.Value);
        }

        builder.Append('\n')
            .Append("  display=")
            .Append(BuildDisplayServerSummary())
            .Append('\n')
            .Append("  root=")
            .Append(BuildWindowSummary(root))
            .Append('\n')
            .Append("  window=")
            .Append(BuildWindowSummary(window));

        ModLogger.Debug(builder.ToString());
    }

    private static string BuildDisplaySnapshot()
    {
        var builder = new StringBuilder(1024);
        builder.Append(BuildDisplayServerSummary());

        int screenCount = Safe(() => DisplayServer.GetScreenCount(), -1);
        int primaryScreen = Safe(() => DisplayServer.GetPrimaryScreen(), -1);
        for (int screen = 0; screen < screenCount; screen++)
        {
            Vector2I rawSize = Safe(() => DisplayServer.ScreenGetSize(screen), Vector2I.Zero);
            float scale = Safe(() => DisplayServer.ScreenGetScale(screen), 1f);
            Vector2I scaledSize = ScaleDown(rawSize, scale);
            Vector2I position = Safe(() => DisplayServer.ScreenGetPosition(screen), Vector2I.Zero);
            int dpi = Safe(() => DisplayServer.ScreenGetDpi(screen), -1);

            builder.Append('\n')
                .Append("  screen[")
                .Append(screen)
                .Append("] primary=")
                .Append(screen == primaryScreen)
                .Append(" rawSize=")
                .Append(rawSize)
                .Append(" scale=")
                .Append(scale.ToString("0.###", CultureInfo.InvariantCulture))
                .Append(" scaledSize=")
                .Append(scaledSize)
                .Append(" position=")
                .Append(position)
                .Append(" dpi=")
                .Append(dpi);
        }

        return builder.ToString();
    }

    private static string BuildDisplayServerSummary()
    {
        return "name=" + Safe(() => DisplayServer.GetName(), "unknown")
            + " count=" + Safe(() => DisplayServer.GetScreenCount(), -1)
            + " primary=" + Safe(() => DisplayServer.GetPrimaryScreen(), -1)
            + " windowScreen=" + Safe(() => DisplayServer.WindowGetCurrentScreen(), -1)
            + " windowSize=" + Safe(() => DisplayServer.WindowGetSize(), Vector2I.Zero)
            + " windowPosition=" + Safe(() => DisplayServer.WindowGetPosition(), Vector2I.Zero);
    }

    private static string BuildWindowSummary(Window? window)
    {
        if (window == null)
        {
            return "null";
        }

        return "insideTree=" + Safe(window.IsInsideTree, false)
            + " queued=" + Safe(window.IsQueuedForDeletion, false)
            + " visible=" + Safe(() => window.Visible, false)
            + " id=" + Safe(window.GetWindowId, -1)
            + " embedded=" + Safe(window.IsEmbedded, false)
            + " forceNative=" + Safe(() => window.ForceNative, false)
            + " currentScreen=" + Safe(() => window.CurrentScreen, -1)
            + " size=" + Safe(() => window.Size, Vector2I.Zero)
            + " position=" + Safe(() => window.Position, Vector2I.Zero)
            + " guiEmbedSubwindows=" + Safe(() => window.GuiEmbedSubwindows, false);
    }

    private static Vector2I ScaleDown(Vector2I size, float scale)
    {
        if (size.X <= 0 || size.Y <= 0 || scale <= 0f || Math.Abs(scale - 1f) < 0.01f)
        {
            return size;
        }

        int width = (int)MathF.Round(size.X / scale);
        int height = (int)MathF.Round(size.Y / scale);
        return width > 0 && height > 0 ? new Vector2I(width, height) : size;
    }

    private static T Safe<T>(Func<T> getValue, T fallback)
    {
        try
        {
            return getValue();
        }
        catch
        {
            return fallback;
        }
    }
}
