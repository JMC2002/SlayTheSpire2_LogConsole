using System;
using Godot;
using JmcLogConsole.Core;
using JmcLogConsole.UI.Controls;
using JmcLogConsole.ViewModels;
using JmcModLib.Utils;
using MegaCrit.Sts2.Core.Logging;

namespace JmcLogConsole.UI;

public partial class LogConsolePopup : Window
{
    private static readonly Vector2I DefaultMinSize = new(720, 420);
    private const int MinZoomedLogFontSize = 8;
    private const int MaxZoomedLogFontSize = 96;
    private static readonly string[] DefaultControlFontFamilies =
    [
        "Segoe UI",
        "Microsoft YaHei UI",
        "Microsoft YaHei",
        "Noto Sans CJK SC",
        "Segoe UI Emoji",
        "Noto Color Emoji"
    ];
    private static readonly string[] DefaultLogFontFamilies =
    [
        "Cascadia Mono",
        "Consolas",
        "Microsoft YaHei UI",
        "Noto Sans CJK SC",
        "Segoe UI Emoji",
        "Noto Color Emoji"
    ];

    private LineEdit? filterInput;
    private Label? filterStatus;
    private VirtualLogView? output;
    private Label? titleLabel;
    private Button? copyButton;
    private Button? clearButton;
    private Button? closeButton;
    private Label? filterLabel;
    private Button? copyFilteredButton;
    private int renderedVersion = -1;
    private bool initialized;
    private bool openedOnce;
    private readonly LogViewportModel viewportModel = new();
    private LogLineFormatter formatter = LogLineFormatter.FromSettings();
    private Godot.Timer? filterDebounceTimer;
    private string filterPattern = string.Empty;
    private string pendingFilterPattern = string.Empty;
    private Vector2I lastAppliedFontWindowSize = new(-1, -1);
    private int lastAppliedFontScreen = -1;
    private int lastAppliedLogFontSize = -1;
    private int lastAppliedControlFontSize = -1;
    private int logFontZoomOffset;
    private string lastAppliedLanguage = string.Empty;
    private bool firstNativeDpiRefreshDone;

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

        Title = T("WINDOW_TITLE", "Jmc Log Console - 日志窗口");
        Size = GetConfiguredDefaultWindowSize();
        MinSize = DefaultMinSize;
        Transient = false;
        TransientToFocused = false;
        PopupWindow = false;
        Exclusive = false;
        Borderless = false;
        Unresizable = false;
        AlwaysOnTop = false;
        ApplyContentScaleReset();
        Visible = false;

        CloseRequested += ClosePopup;
        WindowInput += OnWindowInput;

        BuildLayout();
        ApplyLocalizedText();
        ApplyFontSettingsTree();

        ModLogger.Info($"JmcLogConsoleWindow 初始化完成。ForceNative={ForceNative} Transient={Transient} Size={Size} AutoFont={LogConsoleSettings.AutoScaleFont} AutoWindow={LogConsoleSettings.AutoScaleWindowSize}");
        DisplayDiagnostics.LogWindowState("Popup.InitializeIfNeeded", this, GetSceneRoot());
    }

    public override void _Process(double delta)
    {
        string currentLanguage = L10n.CurrentLanguage;
        if (!string.Equals(currentLanguage, lastAppliedLanguage, StringComparison.Ordinal))
        {
            ApplyLocalizedText();
            if (Visible)
            {
                Render(force: false);
            }
        }

        int activeScreen = GetWindowCurrentScreen();
        if (!Visible
            || !LogConsoleSettings.AutoScaleFont
            || (Size == lastAppliedFontWindowSize && activeScreen == lastAppliedFontScreen))
        {
            return;
        }

        ApplyFontSettingsTree();
        Render(force: false);
    }

    private void OnWindowInput(InputEvent @event)
    {
        if (ShouldSuppressWindowShortcuts(@event))
        {
            return;
        }

        if (LogConsoleHost.TryHandleToggleShortcut(@event))
        {
            return;
        }

        if (@event is InputEventKey { Pressed: true, Echo: false, Keycode: Key.C } copyKey
            && (copyKey.CtrlPressed || copyKey.MetaPressed)
            && output?.CopySelectionToClipboard() == true)
        {
            GetViewport()?.SetInputAsHandled();
            return;
        }

        if (@event is InputEventKey { Pressed: true, Echo: false, Keycode: Key.Escape })
        {
            ClosePopup();
        }
    }

    private bool ShouldSuppressWindowShortcuts(InputEvent @event)
    {
        if (@event is not InputEventKey)
        {
            return false;
        }

        if (filterInput == null || !filterInput.IsInsideTree())
        {
            return false;
        }

        Control? focusOwner = GetViewport()?.GuiGetFocusOwner();
        for (Node? node = focusOwner; node != null; node = node.GetParent())
        {
            if (ReferenceEquals(node, filterInput))
            {
                return true;
            }
        }

        return filterInput.HasFocus() || filterInput.IsEditing();
    }

    public void Open()
    {
        InitializeIfNeeded();
        Window? root = GetRoot();
        DisplayDiagnostics.LogDisplaySnapshot("Popup.Open begin", force: true);
        DisplayDiagnostics.LogWindowState(
            "Popup.Open begin",
            this,
            root,
            selectedOption: LogConsoleSettings.DefaultOpenScreen);

        bool firstOpen = !openedOnce;
        int targetScreen = firstOpen ? ApplyConfiguredDefaultScreen() : CurrentScreen;
        DisplayDiagnostics.LogWindowState(
            $"Popup.Open resolved firstOpen={firstOpen} openedOnce={openedOnce}",
            this,
            root,
            targetScreen,
            LogConsoleSettings.DefaultOpenScreen);

        Size = firstOpen || Size.X <= 0 || Size.Y <= 0 ? GetConfiguredDefaultWindowSize() : Size;
        ApplyFontSettingsTree();

        if (!Visible)
        {
            if (openedOnce)
            {
                Show();
            }
            else
            {
                CurrentScreen = targetScreen;
                if (LogConsoleSettings.UseNativeExternalWindow
                    && TryGetCenteredRect(targetScreen, Size, out Rect2I popupRect))
                {
                    Position = popupRect.Position;
                    Size = popupRect.Size;
                    Popup(popupRect);
                }
                else
                {
                    PopupCentered(Size);
                }

                openedOnce = true;
                ApplyConfiguredScreenPlacement(targetScreen, Size, "first show immediate");
                ScheduleConfiguredScreenPlacement(targetScreen, Size);
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
        DisplayDiagnostics.LogWindowState("Popup.Open after show immediate", this, root, targetScreen);
        Callable.From(() =>
        {
            DisplayDiagnostics.LogWindowState(
                "Popup.Open after show deferred",
                this,
                GetRoot(),
                targetScreen);
        }).CallDeferred();
    }

    private void ScheduleConfiguredScreenPlacement(int targetScreen, Vector2I requestedSize)
    {
        if (!LogConsoleSettings.UseNativeExternalWindow || targetScreen < 0)
        {
            return;
        }

        Callable.From(() =>
        {
            ApplyConfiguredScreenPlacement(targetScreen, requestedSize, "first show deferred");
            Callable.From(() =>
            {
                RefreshNativeDpiAfterFirstPlacement(targetScreen, requestedSize);
            }).CallDeferred();
        }).CallDeferred();
    }

    private void RefreshNativeDpiAfterFirstPlacement(int targetScreen, Vector2I requestedSize)
    {
        if (firstNativeDpiRefreshDone
            || !Visible
            || !LogConsoleSettings.UseNativeExternalWindow
            || targetScreen == GetGameWindowScreen())
        {
            ApplyConfiguredScreenPlacement(targetScreen, requestedSize, "first show second deferred");
            return;
        }

        firstNativeDpiRefreshDone = true;
        DisplayDiagnostics.LogWindowState(
            "Popup.RefreshNativeDpiAfterFirstPlacement before hide",
            this,
            GetRoot(),
            targetScreen,
            LogConsoleSettings.DefaultOpenScreen);

        Hide();
        Callable.From(() =>
        {
            if (IsQueuedForDeletion())
            {
                return;
            }

            Vector2I targetSize = GetWindowSizeForScreen(requestedSize, targetScreen);
            CurrentScreen = targetScreen;
            Size = targetSize;
            if (TryGetCenteredRect(targetScreen, targetSize, out Rect2I rect))
            {
                Position = rect.Position;
                Size = rect.Size;
            }

            ApplyContentScaleReset();
            Show();
            ApplyConfiguredScreenPlacement(targetScreen, Size, "first native dpi refresh");
            GrabFocus();
            RequestAttention();

            Callable.From(() =>
            {
                ApplyConfiguredScreenPlacement(targetScreen, Size, "first native dpi refresh deferred");
            }).CallDeferred();
        }).CallDeferred();
    }

    private void ApplyConfiguredScreenPlacement(int targetScreen, Vector2I requestedSize, string reason)
    {
        if (IsQueuedForDeletion() || !LogConsoleSettings.UseNativeExternalWindow)
        {
            return;
        }

        int screenCount = GetScreenCount();
        if (!IsValidScreen(targetScreen, screenCount))
        {
            DisplayDiagnostics.LogWindowState(
                $"Popup.ApplyConfiguredScreenPlacement skipped reason={reason}",
                this,
                GetRoot(),
                targetScreen,
                LogConsoleSettings.DefaultOpenScreen);
            return;
        }

        Vector2I targetSize = GetWindowSizeForScreen(requestedSize, targetScreen);
        Vector2I? targetPosition = null;
        ApplyContentScaleReset();
        CurrentScreen = targetScreen;
        Size = targetSize;
        if (TryGetCenteredPosition(targetScreen, targetSize, out Vector2I position))
        {
            Position = position;
            targetPosition = position;
        }

        ApplyDisplayServerWindowPlacement(targetScreen, targetSize, targetPosition);

        ApplyFontSettingsTree();
        Render(force: false);

        DisplayDiagnostics.LogWindowState(
            $"Popup.ApplyConfiguredScreenPlacement reason={reason}",
            this,
            GetRoot(),
            targetScreen,
            LogConsoleSettings.DefaultOpenScreen);
    }

    private void ApplyContentScaleReset()
    {
        ContentScaleMode = ContentScaleModeEnum.Disabled;
        ContentScaleFactor = 1.0f;
        ContentScaleSize = Vector2I.Zero;
        SetUseFontOversampling(true);
        OversamplingOverride = GetFontOversampling();
        GuiSnapControlsToPixels = true;
    }

    private void ApplyDisplayServerWindowPlacement(int targetScreen, Vector2I targetSize, Vector2I? targetPosition)
    {
        try
        {
            int windowId = GetWindowId();
            if (windowId <= 0)
            {
                return;
            }

            DisplayServer.WindowSetCurrentScreen(targetScreen, windowId);
            DisplayServer.WindowSetSize(targetSize, windowId);
            if (targetPosition.HasValue)
            {
                DisplayServer.WindowSetPosition(targetPosition.Value, windowId);
            }
        }
        catch (Exception ex)
        {
            ModLogger.Warn($"JmcLogConsole 设置原生窗口显示器失败：{ex.Message}");
        }
    }

    private static bool TryGetCenteredRect(int targetScreen, Vector2I windowSize, out Rect2I rect)
    {
        rect = default;
        if (!TryGetCenteredPosition(targetScreen, windowSize, out Vector2I position))
        {
            return false;
        }

        rect = new Rect2I(position, windowSize);
        return true;
    }

    private Vector2I GetWindowSizeForScreen(Vector2I requestedSize, int targetScreen)
    {
        Vector2I size = requestedSize.X > 0 && requestedSize.Y > 0
            ? requestedSize
            : GetConfiguredDefaultWindowSize();

        if (!TryGetRawScreenSize(targetScreen, out Vector2I screenSize))
        {
            return size;
        }

        int maxWidth = Math.Max(480, screenSize.X - 120);
        int maxHeight = Math.Max(360, screenSize.Y - 120);
        int minWidth = Math.Min(DefaultMinSize.X, maxWidth);
        int minHeight = Math.Min(DefaultMinSize.Y, maxHeight);

        return new Vector2I(
            Math.Clamp(size.X, minWidth, maxWidth),
            Math.Clamp(size.Y, minHeight, maxHeight));
    }

    private static bool TryGetCenteredPosition(int targetScreen, Vector2I windowSize, out Vector2I position)
    {
        position = Vector2I.Zero;

        try
        {
            Vector2I screenPosition = DisplayServer.ScreenGetPosition(targetScreen);
            Vector2I screenSize = DisplayServer.ScreenGetSize(targetScreen);
            if (screenSize.X <= 0 || screenSize.Y <= 0 || windowSize.X <= 0 || windowSize.Y <= 0)
            {
                return false;
            }

            position = new Vector2I(
                screenPosition.X + Math.Max(0, (screenSize.X - windowSize.X) / 2),
                screenPosition.Y + Math.Max(0, (screenSize.Y - windowSize.Y) / 2));
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryGetRawScreenSize(int targetScreen, out Vector2I screenSize)
    {
        screenSize = Vector2I.Zero;
        try
        {
            screenSize = DisplayServer.ScreenGetSize(targetScreen);
            return screenSize.X > 0 && screenSize.Y > 0;
        }
        catch
        {
            return false;
        }
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

        Render(force: false);
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

        titleLabel = new Label
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
        };
        titleLabel.AddThemeFontSizeOverride("font_size", LogConsoleSettings.ControlFontSize + 2);
        header.AddChild(titleLabel);

        copyButton = new Button();
        copyButton.Pressed += CopyPlainText;
        header.AddChild(copyButton);

        clearButton = new Button();
        clearButton.Pressed += () =>
        {
            LogCaptureService.Clear();
            viewportModel.Clear();
            Render(force: true);
        };
        header.AddChild(clearButton);

        closeButton = new Button();
        closeButton.Pressed += ClosePopup;
        header.AddChild(closeButton);

        filterDebounceTimer = new Godot.Timer
        {
            OneShot = true,
            WaitTime = 0.15,
            ProcessMode = ProcessModeEnum.Always
        };
        filterDebounceTimer.Timeout += ApplyPendingFilter;
        AddChild(filterDebounceTimer);

        var filterRow = new HBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
        };
        root.AddChild(filterRow);

        filterLabel = new Label();
        filterRow.AddChild(filterLabel);

        filterInput = new LineEdit
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            ClearButtonEnabled = true
        };
        filterInput.TextChanged += OnFilterTextChanged;
        filterRow.AddChild(filterInput);

        copyFilteredButton = new Button();
        copyFilteredButton.Pressed += CopyFilteredPlainText;
        filterRow.AddChild(copyFilteredButton);

        filterStatus = new Label
        {
            Text = string.Empty,
            SizeFlagsHorizontal = Control.SizeFlags.ShrinkEnd
        };
        filterRow.AddChild(filterStatus);

        root.AddChild(new HSeparator());

        output = new VirtualLogView
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(0f, 240f),
            EmptyText = T("NO_LOGS", "暂无日志。"),
            NoMatchesText = T("NO_MATCHES", "没有匹配的日志。")
        };
        root.AddChild(output);
        output.SetModel(viewportModel);
        output.SnapshotChanged += OnViewportSnapshotChanged;
        output.FontZoomRequested += OnOutputFontZoomRequested;
        ApplyOutputFontSettings();
    }

    private void Render(bool force)
    {
        if (output == null)
        {
            return;
        }

        LogLineFormatter currentFormatter = LogLineFormatter.FromSettings();
        bool formatterChanged = currentFormatter.ShowTimestamp != formatter.ShowTimestamp
            || currentFormatter.ShowLevel != formatter.ShowLevel;
        formatter = currentFormatter;
        if (formatterChanged)
        {
            viewportModel.SetFilter(filterPattern, formatter);
        }

        output.SetFormatter(formatter);
        output.Refresh(forceFollowTail: force);
        renderedVersion = LogCaptureService.Version;
    }

    private void OnFilterTextChanged(string value)
    {
        pendingFilterPattern = value ?? string.Empty;
        filterDebounceTimer?.Start();
    }

    private void ApplyPendingFilter()
    {
        filterPattern = pendingFilterPattern;
        formatter = LogLineFormatter.FromSettings();
        viewportModel.SetFilter(filterPattern, formatter);
        Render(force: true);
    }

    private void OnViewportSnapshotChanged(LogViewportSnapshot snapshot)
    {
        UpdateFilterStatus(snapshot);
    }

    private void UpdateFilterStatus(LogViewportSnapshot snapshot)
    {
        if (filterStatus == null)
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(snapshot.FilterError))
        {
            filterStatus.Text = snapshot.FilterError;
            filterStatus.Modulate = new Color(1.00f, 0.42f, 0.42f);
            return;
        }

        if (string.IsNullOrWhiteSpace(filterPattern))
        {
            filterStatus.Text = snapshot.FollowTail
                ? $"{snapshot.TotalCount}"
                : $"{snapshot.FirstRow + 1}-{snapshot.LastRow + 1}/{snapshot.TotalRows}";
            filterStatus.Modulate = new Color(0.72f, 0.75f, 0.76f);
            return;
        }

        string followText = snapshot.FollowTail ? string.Empty : " · " + T("HISTORY_SUFFIX", "历史");
        filterStatus.Text = $"{snapshot.MatchCount}/{snapshot.TotalCount}{followText}";
        filterStatus.Modulate = new Color(0.72f, 0.89f, 0.78f);
    }

    private void ApplyLocalizedText()
    {
        Title = T("WINDOW_TITLE", "Jmc Log Console - 日志窗口");

        if (titleLabel != null)
        {
            titleLabel.Text = T("HEADER_TITLE", "Jmc Log Console  日志窗口");
        }

        if (copyButton != null)
        {
            copyButton.Text = T("COPY_ALL", "复制全部");
        }

        if (clearButton != null)
        {
            clearButton.Text = T("CLEAR", "清空");
        }

        if (closeButton != null)
        {
            closeButton.Text = T("CLOSE_ESC", "关闭 Esc");
        }

        if (filterLabel != null)
        {
            filterLabel.Text = T("FILTER_LABEL", "正则筛选");
        }

        if (filterInput != null)
        {
            filterInput.PlaceholderText = T("FILTER_PLACEHOLDER", "Regex");
        }

        if (copyFilteredButton != null)
        {
            copyFilteredButton.Text = T("COPY_VISIBLE", "复制显示");
        }

        if (output != null)
        {
            output.EmptyText = T("NO_LOGS", "暂无日志。");
            output.NoMatchesText = T("NO_MATCHES", "没有匹配的日志。");
        }

        lastAppliedLanguage = L10n.CurrentLanguage;
    }

    private static string T(string key, string fallback)
    {
        return L10n.Resolve($"EXTENSION.JMCLOGCONSOLE.UI.{key}", fallback);
    }

    private void CopyPlainText()
    {
        DisplayServer.ClipboardSet(formatter.BuildPlainText(LogCaptureService.Snapshot()));
    }

    private void CopyFilteredPlainText()
    {
        if (output?.CopySelectionToClipboard() == true)
        {
            return;
        }

        DisplayServer.ClipboardSet(output?.BuildFilteredPlainText() ?? string.Empty);
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
        Font controlFont = BuildControlFont();
        ApplyFontRecursive(this, controlFont);

        int controlFontSize = GetEffectiveControlFontSize();
        ApplyFontSizeRecursive(this, controlFontSize);
        ApplyOutputFontSettings();

        lastAppliedFontWindowSize = Size;
        lastAppliedFontScreen = GetWindowCurrentScreen();
        lastAppliedControlFontSize = controlFontSize;
    }

    private void ApplyOutputFontSettings()
    {
        if (output == null)
        {
            return;
        }

        Font logFont = BuildLogFont();

        int baseLogFontSize = GetEffectiveLogFontSize();
        int logFontSize = Math.Clamp(baseLogFontSize + logFontZoomOffset, MinZoomedLogFontSize, MaxZoomedLogFontSize);
        output.SetLogFont(logFont, logFontSize, Math.Clamp(LogConsoleSettings.LogLineSpacing, 0, 16));

        lastAppliedLogFontSize = logFontSize;
    }

    private void OnOutputFontZoomRequested(int direction)
    {
        int step = Math.Sign(direction);
        if (step == 0)
        {
            return;
        }

        int baseLogFontSize = GetEffectiveLogFontSize();
        int currentSize = Math.Clamp(baseLogFontSize + logFontZoomOffset, MinZoomedLogFontSize, MaxZoomedLogFontSize);
        int nextSize = Math.Clamp(currentSize + step, MinZoomedLogFontSize, MaxZoomedLogFontSize);
        if (nextSize == currentSize)
        {
            return;
        }

        logFontZoomOffset = nextSize - baseLogFontSize;
        ApplyOutputFontSettings();
        ModLogger.Info($"JmcLogConsoleWindow 日志字体缩放：{currentSize} -> {nextSize}，偏移={logFontZoomOffset}");
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
            return Math.Clamp(LogConsoleSettings.ControlFontSize, 12, 72);
        }

        int logFontSize = GetEffectiveLogFontSize();
        int dpiAdjustedMinimum = (int)MathF.Round(15.5f * GetDpiScale());
        int preferredSize = Math.Max(logFontSize, dpiAdjustedMinimum);

        return Math.Clamp(preferredSize, 13, 72);
    }

    private int GetCurrentScreenDpi()
    {
        try
        {
            return DisplayServer.ScreenGetDpi(GetWindowCurrentScreen());
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
            int screen = GetWindowCurrentScreen();
            Vector2I size = DisplayServer.ScreenGetSize(screen);
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
            Window? root = GetRoot();
            Vector2I size = root?.Size ?? Vector2I.Zero;
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
            Vector2I size = DisplayServer.WindowGetSize();
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
            if (viewportSize.X > 0 && viewportSize.Y > 0)
            {
                return new Vector2I((int)viewportSize.X, (int)viewportSize.Y);
            }
        }
        catch
        {
        }

        try
        {
            return new Vector2I(
                Math.Clamp(LogConsoleSettings.DefaultWindowWidth, 800, 2400),
                Math.Clamp(LogConsoleSettings.DefaultWindowHeight, 500, 1600));
        }
        catch
        {
            return Vector2I.Zero;
        }
    }

    private int GetWindowCurrentScreen()
    {
        try
        {
            int windowId = GetWindowId();
            if (windowId > 0)
            {
                int windowScreen = DisplayServer.WindowGetCurrentScreen(windowId);
                if (windowScreen >= 0)
                {
                    return windowScreen;
                }
            }
        }
        catch
        {
        }

        return CurrentScreen;
    }

    private int ApplyConfiguredDefaultScreen()
    {
        int targetScreen = ResolveConfiguredDefaultScreen();
        if (targetScreen < 0)
        {
            return CurrentScreen;
        }

        CurrentScreen = targetScreen;
        return targetScreen;
    }

    private int ResolveConfiguredDefaultScreen()
    {
        LogConsoleSettings.DefaultOpenScreen = DisplayScreenOptions.NormalizeOption(LogConsoleSettings.DefaultOpenScreen);
        int screenCount = GetScreenCount();
        if (screenCount <= 0)
        {
            DisplayDiagnostics.LogWindowState(
                "ResolveConfiguredDefaultScreen: screenCount<=0",
                this,
                GetRoot(),
                CurrentScreen,
                LogConsoleSettings.DefaultOpenScreen);
            return CurrentScreen;
        }

        bool parsedScreenOption = DisplayScreenOptions.TryParseScreenIndex(LogConsoleSettings.DefaultOpenScreen, out int parsedScreen);
        int requestedScreen = LogConsoleSettings.DefaultOpenScreen switch
        {
            DisplayScreenOptions.PrimaryScreen => GetPrimaryScreen(),
            _ when parsedScreenOption => parsedScreen,
            _ => GetGameWindowScreen()
        };

        int resolvedScreen = IsValidScreen(requestedScreen, screenCount)
            ? requestedScreen
            : GetGameWindowScreen();
        ModLogger.Info(
            $"[LogConsole.WindowDiag] ResolveConfiguredDefaultScreen option=\"{LogConsoleSettings.DefaultOpenScreen}\" screenCount={screenCount} parsed={parsedScreenOption}:{parsedScreen} requested={requestedScreen} resolved={resolvedScreen} primary={GetPrimaryScreen()} gameWindow={GetGameWindowScreen()} current={CurrentScreen}");
        return resolvedScreen;
    }

    private int GetGameWindowScreen()
    {
        int screenCount = GetScreenCount();
        Window? root = GetRoot();
        int rootScreen = root?.CurrentScreen ?? CurrentScreen;

        if (IsValidScreen(rootScreen, screenCount))
        {
            return rootScreen;
        }

        if (IsValidScreen(CurrentScreen, screenCount))
        {
            return CurrentScreen;
        }

        int primaryScreen = GetPrimaryScreen();
        return IsValidScreen(primaryScreen, screenCount) ? primaryScreen : 0;
    }

    private Window? GetRoot()
    {
        try
        {
            if (IsInsideTree())
            {
                return GetTree()?.Root;
            }
        }
        catch
        {
        }

        return GetSceneRoot();
    }

    private static Window? GetSceneRoot()
    {
        try
        {
            return (Engine.GetMainLoop() as SceneTree)?.Root;
        }
        catch
        {
            return null;
        }
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

    private static bool IsValidScreen(int screen, int screenCount)
    {
        return screen >= 0 && screen < screenCount;
    }

    private float GetGentleDpiFactor()
    {
        float dpiScale = GetDpiScale();
        if (dpiScale <= 1.0f)
        {
            return 1.0f;
        }

        // DPI 只做温和加权，避免 200%/300% 缩放时字号被成倍放大。
        return 1.0f + (MathF.Sqrt(dpiScale) - 1.0f) * 0.35f;
    }

    private float GetDpiScale()
    {
        int dpi = GetCurrentScreenDpi();
        if (dpi <= 0)
        {
            return 1.0f;
        }

        return Math.Clamp(dpi / 96f, 1.0f, 4.0f);
    }

    private float GetFontOversampling()
    {
        return GetDpiScale();
    }

    private Font BuildControlFont()
    {
        return BuildSystemFont(
            LogConsoleSettings.ControlFontFamilies,
            DefaultControlFontFamilies,
            GetFontOversampling(),
            TextServer.FontAntialiasing.Lcd,
            TextServer.Hinting.Light,
            TextServer.SubpixelPositioning.OneQuarter,
            500);
    }

    private Font BuildLogFont()
    {
        return BuildSystemFont(
            LogConsoleSettings.FontFamilies,
            DefaultLogFontFamilies,
            GetFontOversampling(),
            TextServer.FontAntialiasing.Gray,
            TextServer.Hinting.Normal,
            TextServer.SubpixelPositioning.Auto,
            400);
    }

    private static Font BuildSystemFont(
        string configuredFamilies,
        string[] defaultFamilies,
        float oversampling,
        TextServer.FontAntialiasing antialiasing,
        TextServer.Hinting hinting,
        TextServer.SubpixelPositioning subpixelPositioning,
        int fontWeight)
    {
        string[] names = configuredFamilies
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (names.Length == 0)
        {
            names = defaultFamilies;
        }

        return new SystemFont
        {
            FontNames = names,
            FontWeight = Math.Clamp(fontWeight, 100, 999),
            Antialiasing = antialiasing,
            Hinting = hinting,
            SubpixelPositioning = subpixelPositioning,
            DisableEmbeddedBitmaps = true,
            KeepRoundingRemainders = true,
            Oversampling = Math.Clamp(oversampling, 1.0f, 4.0f),
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
