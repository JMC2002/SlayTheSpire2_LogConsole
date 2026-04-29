using System;
using System.Text;
using Godot;
using JmcLogConsole.Core;
using JmcModLib.Utils;
using MegaCrit.Sts2.Core.Logging;

namespace JmcLogConsole.UI;

public partial class LogConsolePopup : Window
{
    private static readonly Vector2I DefaultMinSize = new(720, 420);

    private RichTextLabel? output;
    private int renderedVersion = -1;
    private bool initialized;
    private bool openedOnce;
    private Vector2I lastAppliedFontWindowSize = new(-1, -1);
    private int lastAppliedLogFontSize = -1;
    private int lastAppliedControlFontSize = -1;

    public override void _Ready()
    {
        // 仅作为 Godot 生命周期兜底；v0.1.4 起，Host 会主动调用 InitializeIfNeeded()。
        InitializeIfNeeded();
    }

    public void InitializeIfNeeded()
    {
        if (initialized)
        {
            return;
        }

        initialized = true;

        Title = "Jmc Log Console - 日志窗口";
        Size = GetConfiguredDefaultWindowSize();
        MinSize = DefaultMinSize;
        ForceNative = true;
        Transient = false;
        TransientToFocused = false;
        PopupWindow = false;
        Exclusive = false;
        Borderless = false;
        Unresizable = false;
        AlwaysOnTop = false;
        Visible = false;

        CloseRequested += ClosePopup;
        WindowInput += OnWindowInput;

        BuildLayout();
        ApplyFontSettingsTree();

        ModLogger.Info($"JmcLogConsoleWindow 初始化完成。ForceNative={ForceNative} Transient={Transient} Size={Size} AutoFont={LogConsoleSettings.AutoScaleFont} AutoWindow={LogConsoleSettings.AutoScaleWindowSize}");
    }

    public override void _Process(double delta)
    {
        if (!Visible || !LogConsoleSettings.AutoScaleFont || Size == lastAppliedFontWindowSize)
        {
            return;
        }

        ApplyFontSettingsTree();
        Render(force: true);
    }

    private void OnWindowInput(InputEvent @event)
    {
        if (LogConsoleHost.TryHandleToggleShortcut(@event))
        {
            return;
        }

        if (@event is InputEventKey { Pressed: true, Echo: false, Keycode: Key.Escape })
        {
            ClosePopup();
        }
    }

    public void Open()
    {
        InitializeIfNeeded();

        Size = Size.X <= 0 || Size.Y <= 0 ? GetConfiguredDefaultWindowSize() : Size;
        ApplyFontSettingsTree();

        if (!Visible)
        {
            if (openedOnce)
            {
                Show();
            }
            else
            {
                PopupCentered(Size);
                openedOnce = true;
            }
        }
        else
        {
            Show();
        }

        Render(force: true);
        GrabFocus();
        RequestAttention();

        ModLogger.Info($"JmcLogConsoleWindow.Open() Visible={Visible} Size={Size} Embedded={IsEmbedded()} WindowId={GetWindowId()} LogFont={lastAppliedLogFontSize} ControlFont={lastAppliedControlFontSize} Screen={GetCurrentScreenSize()} Dpi={GetCurrentScreenDpi()}");
    }

    public void ClosePopup()
    {
        Hide();
        LogConsoleHost.FocusGameWindow();
    }

    public void RenderIfDirty()
    {
        if (!Visible)
        {
            return;
        }

        int currentVersion = LogCaptureService.Version;
        if (currentVersion == renderedVersion)
        {
            return;
        }

        Render(force: true);
    }

    private void BuildLayout()
    {
        var panel = new PanelContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill
        };
        panel.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(panel);

        var margin = new MarginContainer();
        margin.AddThemeConstantOverride("margin_left", 18);
        margin.AddThemeConstantOverride("margin_top", 16);
        margin.AddThemeConstantOverride("margin_right", 18);
        margin.AddThemeConstantOverride("margin_bottom", 16);
        margin.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        panel.AddChild(margin);

        var root = new VBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill
        };
        margin.AddChild(root);

        var header = new HBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
        };
        root.AddChild(header);

        var title = new Label
        {
            Text = "Jmc Log Console  日志窗口  中文 / 日本語 / 한국어 / Ω / ✅",
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
        };
        title.AddThemeFontSizeOverride("font_size", LogConsoleSettings.ControlFontSize + 2);
        header.AddChild(title);

        var copyButton = new Button
        {
            Text = "复制全部"
        };
        copyButton.Pressed += CopyPlainText;
        header.AddChild(copyButton);

        var clearButton = new Button
        {
            Text = "清空"
        };
        clearButton.Pressed += () =>
        {
            LogCaptureService.Clear();
            Render(force: true);
        };
        header.AddChild(clearButton);

        var closeButton = new Button
        {
            Text = "关闭 Esc"
        };
        closeButton.Pressed += ClosePopup;
        header.AddChild(closeButton);

        root.AddChild(new HSeparator());

        output = new RichTextLabel
        {
            BbcodeEnabled = false,
            ScrollActive = true,
            ScrollFollowing = true,
            SelectionEnabled = true,
            ContextMenuEnabled = true,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            Text = ""
        };
        ApplyOutputFontSize();
        root.AddChild(output);
    }

    private void Render(bool force)
    {
        if (output == null)
        {
            return;
        }

        LogEntry[] entries = LogCaptureService.Snapshot();
        output.Clear();

        if (entries.Length == 0)
        {
            output.PushColor(new Color(0.54f, 0.57f, 0.60f));
            output.AddText("暂无日志。No logs yet. 支持 Unicode：中文 / 日本語 / 한국어 / Ω / ✅");
            output.Pop();
            renderedVersion = LogCaptureService.Version;
            return;
        }

        foreach (LogEntry entry in entries)
        {
            output.PushColor(ColorFor(entry.Level));
            output.AddText(BuildLine(entry));
            output.Pop();
            output.AddText("\n");
        }

        renderedVersion = LogCaptureService.Version;
        output.ScrollToLine(Math.Max(0, output.GetLineCount() - 1));
    }

    private static string BuildLine(LogEntry entry)
    {
        var builder = new StringBuilder(128 + entry.Message.Length);

        if (LogConsoleSettings.ShowTimestamp)
        {
            builder.Append('[')
                .Append(entry.Time.ToString("HH:mm:ss.fff"))
                .Append("] ");
        }

        if (LogConsoleSettings.ShowLevel)
        {
            builder.Append('[')
                .Append(entry.Level)
                .Append("] ");
        }

        builder.Append(NormalizeNewlines(entry.Message));
        return builder.ToString();
    }

    private static string BuildPlainText(LogEntry[] entries)
    {
        var builder = new StringBuilder(entries.Length * 128);
        foreach (LogEntry entry in entries)
        {
            builder.AppendLine(BuildLine(entry));
        }

        return builder.ToString();
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

    private static string NormalizeNewlines(string value)
    {
        return value.Replace("\r\n", "\n").Replace('\r', '\n');
    }

    private void CopyPlainText()
    {
        DisplayServer.ClipboardSet(BuildPlainText(LogCaptureService.Snapshot()));
    }

    private Vector2I GetConfiguredDefaultWindowSize()
    {
        if (!LogConsoleSettings.AutoScaleWindowSize)
        {
            return new Vector2I(
                Math.Clamp(LogConsoleSettings.DefaultWindowWidth, 800, 2400),
                Math.Clamp(LogConsoleSettings.DefaultWindowHeight, 500, 1600));
        }

        Vector2I screenSize = GetCurrentScreenSize();
        if (screenSize.X <= 0 || screenSize.Y <= 0)
        {
            return new Vector2I(
                Math.Clamp(LogConsoleSettings.DefaultWindowWidth, 800, 2400),
                Math.Clamp(LogConsoleSettings.DefaultWindowHeight, 500, 1600));
        }

        float scale = Math.Clamp(LogConsoleSettings.WindowScale, 0.75f, 1.50f);
        int width = (int)MathF.Round(screenSize.X * 0.46f * scale);
        int height = (int)MathF.Round(screenSize.Y * 0.46f * scale);

        return new Vector2I(
            Math.Clamp(width, 900, 2400),
            Math.Clamp(height, 560, 1600));
    }

    private void ApplyFontSettingsTree()
    {
        Font font = BuildUnicodeFont();
        ApplyFontRecursive(this, font);

        int controlFontSize = GetEffectiveControlFontSize();
        ApplyFontSizeRecursive(this, controlFontSize);
        ApplyOutputFontSize();

        lastAppliedFontWindowSize = Size;
        lastAppliedControlFontSize = controlFontSize;
    }

    private void ApplyOutputFontSize()
    {
        if (output == null)
        {
            return;
        }

        int logFontSize = GetEffectiveLogFontSize();
        output.AddThemeFontSizeOverride("font_size", logFontSize);
        output.AddThemeFontSizeOverride("normal_font_size", logFontSize);
        output.AddThemeFontSizeOverride("mono_font_size", logFontSize);
        output.AddThemeConstantOverride("line_separation", Math.Clamp(LogConsoleSettings.LogLineSpacing, 0, 16));

        lastAppliedLogFontSize = logFontSize;
    }

    private int GetEffectiveLogFontSize()
    {
        if (!LogConsoleSettings.AutoScaleFont)
        {
            return Math.Clamp(LogConsoleSettings.LogFontSize, 12, 48);
        }

        float screenBase = 0f;
        Vector2I screenSize = GetCurrentScreenSize();
        if (screenSize.Y > 0)
        {
            screenBase = screenSize.Y / 84f;
        }

        float windowBase = Size.Y > 0 ? Size.Y / 42f : 0f;
        float baseSize = MathF.Max(16f, MathF.Max(screenBase, windowBase));

        float dpiFactor = GetGentleDpiFactor();
        float userScale = Math.Clamp(LogConsoleSettings.FontScale, 0.75f, 2.0f);

        return Math.Clamp((int)MathF.Round(baseSize * dpiFactor * userScale), 14, 42);
    }

    private int GetEffectiveControlFontSize()
    {
        if (!LogConsoleSettings.AutoScaleFont)
        {
            return Math.Clamp(LogConsoleSettings.ControlFontSize, 12, 48);
        }

        return Math.Clamp(GetEffectiveLogFontSize() - 2, 13, 38);
    }

    private int GetCurrentScreenDpi()
    {
        try
        {
            return DisplayServer.ScreenGetDpi(CurrentScreen);
        }
        catch
        {
            return 96;
        }
    }

    private Vector2I GetCurrentScreenSize()
    {
        try
        {
            Vector2I size = DisplayServer.ScreenGetSize(CurrentScreen);
            if (size.X > 0 && size.Y > 0)
            {
                return size;
            }
        }
        catch
        {
        }

        try
        {
            Vector2 viewportSize = GetViewport()?.GetVisibleRect().Size ?? Vector2.Zero;
            return new Vector2I((int)viewportSize.X, (int)viewportSize.Y);
        }
        catch
        {
            return Vector2I.Zero;
        }
    }

    private float GetGentleDpiFactor()
    {
        int dpi = GetCurrentScreenDpi();
        if (dpi <= 96)
        {
            return 1.0f;
        }

        float dpiScale = Math.Clamp(dpi / 96f, 1.0f, 4.0f);

        // DPI 只做温和加权，避免 200%/300% 缩放时字号被成倍放大。
        return 1.0f + (MathF.Sqrt(dpiScale) - 1.0f) * 0.35f;
    }

    private static Font BuildUnicodeFont()
    {
        string[] names = LogConsoleSettings.FontFamilies
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (names.Length == 0)
        {
            names =
            [
                "Cascadia Mono",
                "Consolas",
                "Microsoft YaHei UI",
                "Noto Sans CJK SC",
                "Segoe UI Emoji",
                "Noto Color Emoji"
            ];
        }

        return new SystemFont
        {
            FontNames = names,
            AllowSystemFallback = true,
            ModulateColorGlyphs = true
        };
    }

    private static void ApplyFontRecursive(Node node, Font font)
    {
        if (node is Control control)
        {
            control.AddThemeFontOverride("font", font);
            control.AddThemeFontOverride("normal_font", font);
            control.AddThemeFontOverride("bold_font", font);
            control.AddThemeFontOverride("italics_font", font);
            control.AddThemeFontOverride("bold_italics_font", font);
            control.AddThemeFontOverride("mono_font", font);
        }

        foreach (Node child in node.GetChildren())
        {
            ApplyFontRecursive(child, font);
        }
    }

    private static void ApplyFontSizeRecursive(Node node, int fontSize)
    {
        if (node is Control control)
        {
            control.AddThemeFontSizeOverride("font_size", fontSize);
            control.AddThemeFontSizeOverride("normal_font_size", fontSize);
            control.AddThemeFontSizeOverride("mono_font_size", fontSize);
            control.AddThemeFontSizeOverride("bold_font_size", fontSize);
            control.AddThemeFontSizeOverride("italics_font_size", fontSize);
            control.AddThemeFontSizeOverride("bold_italics_font_size", fontSize);
        }

        foreach (Node child in node.GetChildren())
        {
            ApplyFontSizeRecursive(child, fontSize);
        }
    }
}
