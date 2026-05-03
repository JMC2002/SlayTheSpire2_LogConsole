using Godot;
using JmcLogConsole.Core;
using JmcLogConsole.UI;
using JmcModLib.Core;
using JmcModLib.Utils;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Modding;
using ModVersionInfo = JmcLogConsole.Core.VersionInfo;

namespace JmcLogConsole;

[ModInitializer(nameof(Initialize))]
public partial class MainFile : Node
{
    public static void Initialize()
    {
        ModRegistry.Register(true, ModVersionInfo.Name, ModVersionInfo.Name, ModVersionInfo.Version)?
            .RegisterLogger(LogLevel.Info, uIFlags: LogConfigUIFlags.All)
            .RegisterButton(
                "打开或关闭日志窗口",
                LogConsoleHost.Toggle,
                "切换窗口",
                group: "日志窗口",
                storageKey: "button.toggle_window",
                order: 100)
            .RegisterButton(
                "清空日志窗口缓存",
                LogCaptureService.Clear,
                "清空日志",
                group: "日志窗口",
                storageKey: "button.clear_logs",
                order: 110)
            .UseConfig()
            .Done();

        LogCaptureService.Initialize();
        LogConsoleHost.Install();
        ModRegistry.OnUnregistered += OnModUnregistered;

        ModLogger.Info($"{ModVersionInfo.Name} 已启动。Unicode test: 中文 / 日本語 / 한국어 / Ω / ∑ / ✅");
    }

    private static void OnModUnregistered(ModContext context)
    {
        if (context.Assembly != typeof(MainFile).Assembly)
        {
            return;
        }

        ModRegistry.OnUnregistered -= OnModUnregistered;
        LogCaptureService.Shutdown();
        LogConsoleHost.Uninstall();
    }
}
