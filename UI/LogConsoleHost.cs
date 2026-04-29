using System;
using Godot;
using JmcLogConsole.Core;
using JmcModLib.Config.UI;
using JmcModLib.Utils;
using MegaCrit.Sts2.Core.Nodes;

namespace JmcLogConsole.UI;

public partial class LogConsoleHost : Node
{
    private const string HostNodeName = "JmcLogConsoleHost";

    private LogConsolePopup? popup;
    private int renderedVersion = -1;
    private bool polledShortcutWasDown;

    private static bool pendingOpen;
    private static bool installScheduled;
    private static bool renderScheduled;
    private static bool hotkeysRegistered;

    public static LogConsoleHost? Instance { get; private set; }

    private static bool LegacyHotkeysEnabled =>
        LogConsoleSettings.EnableHotkey && LogConsoleSettings.EnableLegacyCombos;

    private static JmcKeyBinding EffectiveToggleHotkey =>
        LogConsoleSettings.EnableHotkey ? LogConsoleSettings.ToggleHotkey : default;

    private static JmcKeyBinding LegacyCtrlShiftLHotkey =>
        LegacyHotkeysEnabled ? new JmcKeyBinding(Key.L, JmcKeyModifiers.Ctrl | JmcKeyModifiers.Shift) : default;

    private static JmcKeyBinding LegacyCtrlAltLHotkey =>
        LegacyHotkeysEnabled ? new JmcKeyBinding(Key.L, JmcKeyModifiers.Ctrl | JmcKeyModifiers.Alt) : default;

    public static void Install()
    {
        if (Instance is { } current)
        {
            if (!current.IsQueuedForDeletion() && current.IsInsideTree())
            {
                current.EnsurePopupCreated("Install reuse");
                return;
            }

            ModLogger.Warn("检测到 JmcLogConsoleHost.Instance 不是树内节点，丢弃该 stale instance 并重新调度挂载。");
            Instance = null;
        }

        if (installScheduled)
        {
            return;
        }

        if (Engine.GetMainLoop() is not SceneTree tree || tree.Root == null)
        {
            ModLogger.Warn("无法安装日志窗口：当前 MainLoop 不是 SceneTree，或者 SceneTree.Root 不存在。");
            return;
        }

        LogConsoleHost? existing = tree.Root.GetNodeOrNull<LogConsoleHost>(HostNodeName)
            ?? NGame.Instance?.GetNodeOrNull<LogConsoleHost>(HostNodeName);
        if (existing != null && !existing.IsQueuedForDeletion())
        {
            Instance = existing;
            existing.ConfigureInputProcessing();
            RegisterRuntimeHotkeys();
            existing.EnsurePopupCreated("Install existing");
            return;
        }

        var host = new LogConsoleHost
        {
            Name = HostNodeName,
            ProcessMode = ProcessModeEnum.Always
        };

        installScheduled = true;

        Callable.From(() => DeferredAddHost(tree, host)).CallDeferred();
        ModLogger.Info("JmcLogConsoleHost 已请求托管 Callable 延迟挂载。Window 将在 AddChild 成功后立即创建，不再依赖 _Ready()。");
    }

    private static void DeferredAddHost(SceneTree tree, LogConsoleHost host)
    {
        try
        {
            if (host.IsQueuedForDeletion())
            {
                installScheduled = false;
                return;
            }

            Node? parent = NGame.Instance;
            if (parent == null || !parent.IsInsideTree())
            {
                parent = tree.Root;
            }

            if (parent == null)
            {
                installScheduled = false;
                ModLogger.Warn("JmcLogConsoleHost 延迟挂载失败：parent 为空。");
                return;
            }

            LogConsoleHost? existing = parent.GetNodeOrNull<LogConsoleHost>(HostNodeName);
            if (existing != null && !existing.IsQueuedForDeletion())
            {
                Instance = existing;
                installScheduled = false;
                existing.ConfigureInputProcessing();
                RegisterRuntimeHotkeys();
                existing.EnsurePopupCreated("DeferredAddHost existing");
                ModLogger.Info($"JmcLogConsoleHost 已存在于 parent={parent.GetPath()}，复用现有节点。InsideTree={existing.IsInsideTree()} PopupReady={existing.popup != null}");
                return;
            }

            if (!host.IsInsideTree())
            {
                parent.AddChild(host);
            }

            Instance = host;
            host.ConfigureInputProcessing();
            RegisterRuntimeHotkeys();
            installScheduled = false;
            ModLogger.Info($"JmcLogConsoleHost 已实际 AddChild。Parent={parent.GetPath()} InsideTree={host.IsInsideTree()}，准备直接创建 Window。");

            host.EnsurePopupCreated("DeferredAddHost after AddChild");

            if (LogConsoleSettings.ShowOnStartup || pendingOpen)
            {
                pendingOpen = false;
                Callable.From(host.OpenPopup).CallDeferred();
                ModLogger.Info("JmcLogConsoleHost 已调度启动打开日志窗口。");
            }
        }
        catch (Exception ex)
        {
            installScheduled = false;
            Instance = null;
            ModLogger.Error($"JmcLogConsoleHost 延迟挂载异常：{ex}");
        }
    }

    public static void Uninstall()
    {
        LogCaptureService.Changed -= OnLogCaptureChanged;

        if (Instance is { } current && !current.IsQueuedForDeletion())
        {
            current.QueueFree();
        }

        Instance = null;
        pendingOpen = false;
        installScheduled = false;
        renderScheduled = false;
        hotkeysRegistered = false;
        JmcHotkeyManager.Unregister("ui.toggle_hotkey.runtime", typeof(LogConsoleHost).Assembly);
        JmcHotkeyManager.Unregister("ui.legacy_ctrl_shift_l", typeof(LogConsoleHost).Assembly);
        JmcHotkeyManager.Unregister("ui.legacy_ctrl_alt_l", typeof(LogConsoleHost).Assembly);
    }

    public static void Toggle()
    {
        Install();

        if (Instance is not { } instance || !instance.IsInsideTree())
        {
            pendingOpen = !pendingOpen;
            ModLogger.Info($"日志窗口 Host 尚未 ready，记录 pendingOpen={pendingOpen}。InstanceInsideTree={Instance?.IsInsideTree().ToString() ?? "null"} installScheduled={installScheduled}");
            return;
        }

        instance.EnsurePopupCreated("Toggle");
        instance.TogglePopup();
    }

    public static void ShowWindow()
    {
        Install();

        if (Instance is not { } instance || !instance.IsInsideTree())
        {
            pendingOpen = true;
            ModLogger.Info($"日志窗口 Host 尚未 ready，记录 pendingOpen=true。InstanceInsideTree={Instance?.IsInsideTree().ToString() ?? "null"} installScheduled={installScheduled}");
            return;
        }

        instance.EnsurePopupCreated("ShowWindow");
        instance.OpenPopup();
    }

    public override void _Ready()
    {
        // 这里仅作为诊断和兜底。v0.1.4 起，窗口创建不再依赖 _Ready()。
        Instance = this;
        installScheduled = false;
        ProcessMode = ProcessModeEnum.Always;
        ConfigureInputProcessing();
        RegisterRuntimeHotkeys();
        ModLogger.Info($"JmcLogConsoleHost._Ready() 已执行。Parent={GetParent()?.GetPath().ToString() ?? "null"} PopupReady={popup != null}");
        EnsurePopupCreated("_Ready fallback");
    }

    public override void _ExitTree()
    {
        LogCaptureService.Changed -= OnLogCaptureChanged;

        if (ReferenceEquals(Instance, this))
        {
            Instance = null;
        }

        installScheduled = false;
        base._ExitTree();
    }

    public override void _Process(double delta)
    {
        PollToggleHotkeys();
        RenderIfNeeded();
    }

    public override void _Input(InputEvent @event)
    {
        HandleGlobalInput(@event);
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        HandleGlobalInput(@event);
    }

    public override void _UnhandledKeyInput(InputEvent @event)
    {
        HandleGlobalInput(@event);
    }

    private static void ToggleFromConfiguredHotkey()
    {
        ToggleFromHotkey();
    }

    private static void ToggleFromLegacyCtrlShiftL()
    {
        ToggleFromHotkey();
    }

    private static void ToggleFromLegacyCtrlAltL()
    {
        ToggleFromHotkey();
    }

    private static void RegisterRuntimeHotkeys()
    {
        if (hotkeysRegistered)
        {
            return;
        }

        JmcHotkeyManager.Register(
            "ui.toggle_hotkey.runtime",
            () => EffectiveToggleHotkey,
            ToggleFromConfiguredHotkey,
            assembly: typeof(LogConsoleHost).Assembly);
        JmcHotkeyManager.Register(
            "ui.legacy_ctrl_shift_l",
            () => LegacyCtrlShiftLHotkey,
            ToggleFromLegacyCtrlShiftL,
            assembly: typeof(LogConsoleHost).Assembly);
        JmcHotkeyManager.Register(
            "ui.legacy_ctrl_alt_l",
            () => LegacyCtrlAltLHotkey,
            ToggleFromLegacyCtrlAltL,
            assembly: typeof(LogConsoleHost).Assembly);

        hotkeysRegistered = true;
    }

    private static void ToggleFromHotkey()
    {
        if (!TryConsumeHotkeyDebounce())
        {
            return;
        }

        Toggle();
    }

    public static bool TryHandleToggleShortcut(InputEvent @event)
    {
        if (!EffectiveToggleHotkey.IsPressed(@event)
            && !LegacyCtrlShiftLHotkey.IsPressed(@event)
            && !LegacyCtrlAltLHotkey.IsPressed(@event))
        {
            return false;
        }

        if (!TryConsumeHotkeyDebounce())
        {
            return false;
        }

        Toggle();
        return true;
    }

    public static void FocusGameWindow()
    {
        try
        {
            Window? root = Instance?.GetTree()?.Root
                ?? (Engine.GetMainLoop() as SceneTree)?.Root;
            if (root == null || root.IsQueuedForDeletion())
            {
                return;
            }

            Callable.From(() =>
            {
                try
                {
                    root.GrabFocus();
                }
                catch
                {
                    // Best-effort focus handoff after closing the native log window.
                }
            }).CallDeferred();
        }
        catch
        {
        }
    }

    private void HandleGlobalInput(InputEvent @event)
    {
        if (ShouldIgnoreHostHotkeyFallback(@event))
        {
            return;
        }

        if (TryHandleToggleShortcut(@event))
        {
            GetViewport()?.SetInputAsHandled();
        }
    }

    private void PollToggleHotkeys()
    {
        if (ShouldIgnoreHostHotkeyFallback(null))
        {
            polledShortcutWasDown = false;
            return;
        }

        bool matched = EffectiveToggleHotkey.IsDown()
            || LegacyCtrlShiftLHotkey.IsDown()
            || LegacyCtrlAltLHotkey.IsDown();

        if (!matched)
        {
            polledShortcutWasDown = false;
            return;
        }

        if (polledShortcutWasDown)
        {
            return;
        }

        polledShortcutWasDown = true;
        ToggleFromHotkey();
    }

    private bool ShouldIgnoreHostHotkeyFallback(InputEvent? @event)
    {
        if (@event != null && @event is not InputEventKey)
        {
            return false;
        }

        Control? focusOwner = GetViewport()?.GuiGetFocusOwner();
        if (focusOwner is LineEdit lineEdit && lineEdit.IsEditing())
        {
            return true;
        }

        if (focusOwner is TextEdit textEdit && textEdit.HasFocus())
        {
            return true;
        }

        for (Node? node = focusOwner; node != null; node = node.GetParent())
        {
            if (node.GetType().Name.Contains("JmcKeybind", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static ulong lastHotkeyTicks;

    private static bool TryConsumeHotkeyDebounce()
    {
        ulong now = Time.GetTicksMsec();
        if (now - lastHotkeyTicks < 150)
        {
            return false;
        }

        lastHotkeyTicks = now;
        return true;
    }

    private void ConfigureInputProcessing()
    {
        SetProcessInput(true);
        SetProcessUnhandledInput(true);
        SetProcessUnhandledKeyInput(true);
    }

    private void ApplyWindowEmbeddingMode()
    {
        if (!LogConsoleSettings.UseNativeExternalWindow)
        {
            return;
        }

        Window? root = GetTree()?.Root;
        if (root == null || !root.GuiEmbedSubwindows)
        {
            return;
        }

        root.GuiEmbedSubwindows = false;
        ModLogger.Info("JmcLogConsole 已关闭 Root.GuiEmbedSubwindows，后续 Window 应尝试作为原生系统窗口创建。");
    }

    private void EnsurePopupCreated(string reason)
    {
        if (popup != null && !popup.IsQueuedForDeletion())
        {
            return;
        }

        ApplyWindowEmbeddingMode();

        popup = new LogConsolePopup
        {
            Name = "JmcLogConsoleWindow",
            ProcessMode = ProcessModeEnum.Always
        };

        popup.InitializeIfNeeded();
        AddChild(popup);

        renderedVersion = -1;
        LogCaptureService.Changed -= OnLogCaptureChanged;
        LogCaptureService.Changed += OnLogCaptureChanged;

        ModLogger.Info($"JmcLogConsoleWindow 已由 Host 直接创建。Reason={reason} HostInsideTree={IsInsideTree()} PopupInsideTree={popup.IsInsideTree()} PopupVisible={popup.Visible}");
    }

    private static void OnLogCaptureChanged()
    {
        LogConsoleHost? instance = Instance;
        if (instance == null || !instance.IsInsideTree() || instance.popup == null || !instance.popup.Visible)
        {
            return;
        }

        if (renderScheduled)
        {
            return;
        }

        renderScheduled = true;
        Callable.From(instance.RenderDeferred).CallDeferred();
    }

    private void RenderDeferred()
    {
        renderScheduled = false;
        RenderIfNeeded();
    }

    private void RenderIfNeeded()
    {
        if (popup?.Visible != true)
        {
            return;
        }

        int currentVersion = LogCaptureService.Version;
        if (currentVersion == renderedVersion)
        {
            return;
        }

        popup.RenderIfDirty();
        renderedVersion = currentVersion;
    }

    private void TogglePopup()
    {
        EnsurePopupCreated("TogglePopup");

        if (popup == null)
        {
            pendingOpen = !pendingOpen;
            return;
        }

        if (popup.Visible)
        {
            popup.ClosePopup();
        }
        else
        {
            popup.Open();
            renderedVersion = LogCaptureService.Version;
        }
    }

    private void OpenPopup()
    {
        EnsurePopupCreated("OpenPopup");

        if (popup == null)
        {
            pendingOpen = true;
            return;
        }

        popup.Open();
        renderedVersion = LogCaptureService.Version;
    }
}
