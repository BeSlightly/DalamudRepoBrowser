using Dalamud.Game.Command;
using Dalamud.IoC;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;

namespace DalamudRepoBrowser;

public sealed class Plugin : IDalamudPlugin
{
    private const string CommandName = "/xlrepos";

    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] internal static IChatGui ChatGui { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;

    public Configuration Configuration { get; }

    private readonly WindowSystem windowSystem = new("DalamudRepoBrowser");
    private readonly RepoManager repoManager;
    private readonly RepoBrowserWindow mainWindow;

    public Plugin()
    {
        Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        Configuration.Initialize();

        repoManager = new RepoManager(Configuration, PluginInterface, Log);
        mainWindow = new RepoBrowserWindow(repoManager, Configuration);
        windowSystem.AddWindow(mainWindow);

        CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "Opens the repository browser."
        });

        PluginInterface.UiBuilder.Draw += windowSystem.Draw;
        PluginInterface.UiBuilder.OpenConfigUi += OpenSettingsUi;
        PluginInterface.UiBuilder.OpenMainUi += ToggleMainUi;
    }

    public void Dispose()
    {
        Configuration.Save();

        PluginInterface.UiBuilder.Draw -= windowSystem.Draw;
        PluginInterface.UiBuilder.OpenConfigUi -= OpenSettingsUi;
        PluginInterface.UiBuilder.OpenMainUi -= ToggleMainUi;

        windowSystem.RemoveAllWindows();
        mainWindow.Dispose();
        repoManager.Dispose();

        CommandManager.RemoveHandler(CommandName);
    }

    private void OnCommand(string command, string args)
    {
        ToggleMainUi();
    }

    private void ToggleMainUi() => mainWindow.Toggle();

    private void OpenSettingsUi() => mainWindow.OpenSettings();

    public static void PrintEcho(string message)
    {
        ChatGui.Print($"[DalamudRepoBrowser] {message}");
    }

    public static void PrintError(string message)
    {
        ChatGui.PrintError($"[DalamudRepoBrowser] {message}");
    }
}
