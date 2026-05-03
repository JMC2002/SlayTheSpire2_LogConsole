using Godot;

namespace JmcLogConsole.Core;

public static class DisplayScreenOptions
{
    public const string FollowGameWindow = "follow_game_window";
    public const string PrimaryScreen = "primary_screen";
    private const string ScreenPrefix = "screen.";

    public static IReadOnlyList<string> GetOptions()
    {
        var options = new List<string>
        {
            FollowGameWindow,
            PrimaryScreen
        };

        int screenCount = GetScreenCount();
        int primaryScreen = GetPrimaryScreen();
        for (int screen = 0; screen < screenCount; screen++)
        {
            options.Add(FormatScreenOption(screen, primaryScreen));
        }

        DisplayDiagnostics.LogDisplaySnapshot("Build default-open-screen dropdown options: " + string.Join(" | ", options));

        return options;
    }

    public static string NormalizeOption(string? option)
    {
        if (string.IsNullOrWhiteSpace(option))
        {
            return FollowGameWindow;
        }

        if (string.Equals(option, FollowGameWindow, StringComparison.Ordinal)
            || string.Equals(option, "跟随游戏窗口", StringComparison.Ordinal)
            || string.Equals(option, "Follow game window", StringComparison.OrdinalIgnoreCase))
        {
            return FollowGameWindow;
        }

        if (string.Equals(option, PrimaryScreen, StringComparison.Ordinal)
            || string.Equals(option, "主显示器", StringComparison.Ordinal)
            || string.Equals(option, "Primary display", StringComparison.OrdinalIgnoreCase))
        {
            return PrimaryScreen;
        }

        return TryParseScreenIndex(option, out int screen)
            ? FormatScreenOption(screen, GetPrimaryScreen())
            : FollowGameWindow;
    }

    public static bool TryParseScreenIndex(string? option, out int screen)
    {
        screen = -1;
        if (string.IsNullOrWhiteSpace(option))
        {
            return false;
        }

        return TryParseNumberAfterPrefix(option, ScreenPrefix, zeroBased: true, out screen)
            || TryParseNumberAfterPrefix(option, "screen:", zeroBased: true, out screen)
            || TryParseNumberAfterPrefix(option, "显示器 ", zeroBased: false, out screen)
            || TryParseNumberAfterPrefix(option, "Display ", zeroBased: false, out screen)
            || TryParseMonitorIndex(option, out screen);
    }

    private static string FormatScreenOption(int screen, int primaryScreen)
    {
        return screen == primaryScreen
            ? $"{ScreenPrefix}{screen}.primary"
            : $"{ScreenPrefix}{screen}";
    }

    private static bool TryParseNumberAfterPrefix(string option, string prefix, bool zeroBased, out int screen)
    {
        screen = -1;
        if (!option.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        int start = prefix.Length;
        int end = start;
        while (end < option.Length && char.IsDigit(option[end]))
        {
            end++;
        }

        if (end == start || !int.TryParse(option[start..end], out int number))
        {
            return false;
        }

        screen = zeroBased ? number : number - 1;
        return screen >= 0;
    }

    private static bool TryParseMonitorIndex(string option, out int screen)
    {
        screen = -1;
        const string prefix = "Monitor (";
        if (!option.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        int closeIndex = option.IndexOf(')', prefix.Length);
        return closeIndex > prefix.Length
            && int.TryParse(option[prefix.Length..closeIndex], out screen)
            && screen >= 0;
    }

    private static int GetScreenCount()
    {
        try
        {
            return DisplayServer.GetScreenCount();
        }
        catch
        {
            return 0;
        }
    }

    private static int GetPrimaryScreen()
    {
        try
        {
            return DisplayServer.GetPrimaryScreen();
        }
        catch
        {
            return 0;
        }
    }

}
