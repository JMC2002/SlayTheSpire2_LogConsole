using JmcModLib.Config;
using JmcModLib.Config.UI;
using Godot;
using MegaCrit.Sts2.Core.ControllerInput;
using MegaCrit.Sts2.Core.Logging;

namespace JmcLogConsole.Core;

public static class LogConsoleSettings
{
    private const string Group = "日志窗口";
    public const string AntialiasingNone = "none";
    public const string AntialiasingGray = "gray";
    public const string AntialiasingLcd = "lcd";
    public const string HintingNone = "none";
    public const string HintingLight = "light";
    public const string HintingNormal = "normal";
    public const string SubpixelDisabled = "disabled";
    public const string SubpixelOneQuarter = "one_quarter";
    public const string SubpixelOneHalf = "one_half";
    public const string SubpixelAuto = "auto";

    [UIToggle]
    [Config(
        "启用日志捕获",
        group: Group,
        Description = "关闭后不再把新的游戏日志写入日志窗口缓存。",
        Key = "capture.enabled",
        Order = 10)]
    public static bool EnableCapture = true;

    [UIToggle]
    [Config(
        "启动后自动弹出窗口",
        group: Group,
        Description = "MOD 初始化完成后自动显示日志窗口。",
        Key = "ui.show_on_startup",
        Order = 20)]
    public static bool ShowOnStartup = false;

    public static bool ImportStartupLogFile = true;

    public static int StartupLogFileTailKilobytes = 2048;

    public static bool EnableHotkey = true;

    [UIKeybind(allowController: true)]
    [Config(
        "打开/关闭日志窗口热键",
        group: Group,
        Description = "通过 JmcModLib 的按键绑定修改。默认 F10。",
        Key = "ui.toggle_hotkey",
        Order = 31)]
    public static JmcKeyBinding ToggleHotkey = new(Key.F10);

    public static bool EnableLegacyCombos = true;

    public static bool UseNativeExternalWindow = true;

    [UIDropdown]
    [Config(
        "默认打开显示器",
        group: Group,
        Description = "日志窗口第一次打开时所在的显示器。若选择的显示器不存在，会退回到游戏窗口所在显示器。",
        Key = "ui.default_open_screen",
        Order = 34)]
    public static string DefaultOpenScreen = DisplayScreenOptions.FollowGameWindow;

    public static IReadOnlyList<string> GetDefaultOpenScreenOptions()
    {
        DefaultOpenScreen = DisplayScreenOptions.NormalizeOption(DefaultOpenScreen);
        return DisplayScreenOptions.GetOptions();
    }

    public static bool EnableWindowDiagnostics = false;

    [UIDropdown]
    [Config(
        "最低显示等级",
        group: Group,
        Description = "只保存并显示不低于该等级的新日志。注意：这里不能捕获已经被游戏 Logger 过滤掉的日志。",
        Key = "capture.minimum_level",
        Order = 40)]
    public static LogLevel MinimumLevel = LogLevel.Info;

    [UIIntSlider(100, 10000)]
    [Config(
        "最大缓存行数",
        group: Group,
        Description = "日志窗口最多保留最近多少条日志。",
        Key = "capture.max_lines",
        Order = 50)]
    public static int MaxLines = 1000;

    [UIToggle]
    [Config(
        "显示时间戳",
        group: Group,
        Key = "format.show_timestamp",
        Order = 60)]
    public static bool ShowTimestamp = true;

    [UIToggle]
    [Config(
        "显示日志等级",
        group: Group,
        Key = "format.show_level",
        Order = 70)]
    public static bool ShowLevel = true;

    public static string ControlFontFamilies = "Segoe UI;Microsoft YaHei UI;Microsoft YaHei;Noto Sans CJK SC;Segoe UI Emoji;Noto Color Emoji";

    public static string FontFamilies = "Cascadia Mono;Consolas;Microsoft YaHei UI;Microsoft YaHei;Noto Sans CJK SC;Noto Sans Mono CJK SC;Segoe UI Emoji;Noto Color Emoji";


    public static bool AutoScaleFont = true;

    public static float FontScale = 1.0f;

    public static int LogFontSize = 22;

    public static int ControlFontSize = 20;

    public static bool AutoScaleWindowSize = true;

    public static float WindowScale = 1.0f;

    public static int DefaultWindowWidth = 1500;

    public static int DefaultWindowHeight = 950;

    public static int LogLineSpacing = 2;

    public static float LogFontOversamplingScale = 1.0f;

    public static string LogFontAntialiasing = AntialiasingLcd;

    public static string LogFontHinting = HintingLight;

    public static string LogFontSubpixelPositioning = SubpixelOneQuarter;
}
