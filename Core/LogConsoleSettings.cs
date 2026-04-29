using JmcModLib.Config;
using JmcModLib.Config.UI;
using Godot;
using MegaCrit.Sts2.Core.ControllerInput;
using MegaCrit.Sts2.Core.Logging;

namespace JmcLogConsole.Core;

public static class LogConsoleSettings
{
    private const string Group = "日志窗口";

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
    public static bool ShowOnStartup = true;

    [UIToggle]
    [Config(
        "启用日志窗口快捷键",
        group: Group,
        Description = "开启后响应 JmcModLib 按键绑定，以及可选的 Ctrl+Shift+L / Ctrl+Alt+L 兼容快捷键。",
        Key = "ui.enable_hotkey",
        Order = 30)]
    public static bool EnableHotkey = true;

    [UIKeybind(allowController: true)]
    [Config(
        "打开/关闭日志窗口热键",
        group: Group,
        Description = "通过 JmcModLib 的按键绑定修改。默认 F10；另外仍保留 Ctrl+Shift+L / Ctrl+Alt+L 作为兼容快捷键。",
        Key = "ui.toggle_hotkey",
        Order = 31)]
    public static JmcKeyBinding ToggleHotkey = new(Key.F10);

    [UIToggle]
    [Config(
        "保留 Ctrl+Shift+L / Ctrl+Alt+L",
        group: Group,
        Description = "开启后，即使没有把热键设置为 L，也会额外响应 Ctrl+Shift+L 与 Ctrl+Alt+L。",
        Key = "ui.enable_legacy_combos",
        Order = 32)]
    public static bool EnableLegacyCombos = true;

    [UIToggle]
    [Config(
        "使用可跨屏拖动的原生窗口",
        group: Group,
        Description = "开启后会尝试关闭 Root Viewport 的子窗口嵌入，让日志窗口成为真正的系统窗口。若游戏其他弹窗异常，可关闭此项。",
        Key = "ui.use_native_external_window",
        Order = 33)]
    public static bool UseNativeExternalWindow = true;

    [UIDropdown]
    [Config(
        "最低显示等级",
        group: Group,
        Description = "只保存并显示不低于该等级的新日志。注意：这里不能捕获已经被游戏 Logger 过滤掉的日志。",
        Key = "capture.minimum_level",
        Order = 40)]
    public static LogLevel MinimumLevel = LogLevel.VeryDebug;

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

    [UIInput(256)]
    [Config(
        "Unicode 字体族候选",
        group: Group,
        Description = "用分号分隔。Godot 会使用第一个可用字体，并允许系统字体 fallback。",
        Key = "ui.font_families",
        Order = 80)]
    public static string FontFamilies = "Cascadia Mono;Consolas;Microsoft YaHei UI;Microsoft YaHei;Noto Sans CJK SC;Noto Sans Mono CJK SC;Segoe UI Emoji;Noto Color Emoji";


    [UIToggle]
    [Config(
        "自动适配字体大小",
        group: Group,
        Description = "开启后根据日志窗口大小、当前屏幕分辨率和 DPI 自动计算日志与界面字号。关闭后使用下面的手动字号。",
        Key = "ui.auto_scale_font",
        Order = 81)]
    public static bool AutoScaleFont = true;

    [UIFloatSlider(0.75f, 2.0f, decimalPlaces: 2)]
    [Config(
        "自动字体缩放倍率",
        group: Group,
        Description = "在自动适配字号的基础上再整体缩放。4K 下觉得偏小可调到 1.10 或 1.20。",
        Key = "ui.font_scale",
        Order = 82)]
    public static float FontScale = 1.0f;

    [UIIntSlider(12, 48)]
    [Config(
        "手动日志字体大小",
        group: Group,
        Description = "关闭自动适配字体大小后使用。",
        Key = "ui.log_font_size",
        Order = 83)]
    public static int LogFontSize = 22;

    [UIIntSlider(12, 48)]
    [Config(
        "手动界面字体大小",
        group: Group,
        Description = "关闭自动适配字体大小后使用。",
        Key = "ui.control_font_size",
        Order = 84)]
    public static int ControlFontSize = 20;

    [UIToggle]
    [Config(
        "自动适配默认窗口大小",
        group: Group,
        Description = "开启后根据当前屏幕大小自动决定日志窗口第一次打开的尺寸。关闭后使用下面的手动宽高。",
        Key = "ui.auto_scale_window_size",
        Order = 85)]
    public static bool AutoScaleWindowSize = true;

    [UIFloatSlider(0.75f, 1.50f, decimalPlaces: 2)]
    [Config(
        "自动窗口尺寸倍率",
        group: Group,
        Description = "在自动窗口尺寸基础上整体缩放。",
        Key = "ui.window_scale",
        Order = 86)]
    public static float WindowScale = 1.0f;

    [UIIntSlider(800, 2400)]
    [Config(
        "手动默认窗口宽度",
        group: Group,
        Description = "关闭自动适配默认窗口大小后使用。拖动调整后，本次游戏运行期间会保留当前大小。",
        Key = "ui.default_window_width",
        Order = 87)]
    public static int DefaultWindowWidth = 1500;

    [UIIntSlider(500, 1600)]
    [Config(
        "手动默认窗口高度",
        group: Group,
        Description = "关闭自动适配默认窗口大小后使用。拖动调整后，本次游戏运行期间会保留当前大小。",
        Key = "ui.default_window_height",
        Order = 88)]
    public static int DefaultWindowHeight = 950;

    [UIIntSlider(0, 16)]
    [Config(
        "日志行间距",
        group: Group,
        Description = "日志正文的额外行间距。",
        Key = "ui.log_line_spacing",
        Order = 89)]
    public static int LogLineSpacing = 2;
}
