global using Dalamud;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Dalamud.Game;
using Dalamud.Game.ClientState.Objects;
using Dalamud.Game.Command;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;

// ReSharper disable CheckNamespace
// ReSharper disable UnusedAutoPropertyAccessor.Local

namespace Dalamud;

public class DalamudApi
{
    private static PluginCommandManager<IDalamudPlugin> pluginCommandManager = null!;

    public DalamudApi()
    {
    }

    public DalamudApi(IDalamudPlugin plugin)
    {
        pluginCommandManager ??= new PluginCommandManager<IDalamudPlugin>(plugin);
    }

    public DalamudApi(IDalamudPlugin plugin, IDalamudPluginInterface pluginInterface)
    {
        if (!pluginInterface.Inject(this))
        {
            PluginLog.Error("Failed loading DalamudApi!");
            return;
        }

        pluginCommandManager ??= new PluginCommandManager<IDalamudPlugin>(plugin);
    }

    [PluginService] public static IDalamudPluginInterface PluginInterface { get; private set; } = null!;

    [PluginService] public static IBuddyList BuddyList { get; private set; } = null!;

    [PluginService] public static IChatGui ChatGui { get; private set; } = null!;

    [PluginService] public static IPluginLog PluginLog { get; } = null!;

    [PluginService] public static IClientState ClientState { get; private set; } = null!;

    [PluginService] public static ICommandManager CommandManager { get; } = null!;

    [PluginService] public static ICondition Condition { get; private set; } = null!;

    [PluginService] public static IDataManager DataManager { get; private set; } = null!;

    [PluginService] public static IFateTable FateTable { get; private set; } = null!;

    [PluginService] public static IFlyTextGui FlyTextGui { get; private set; } = null!;

    [PluginService] public static IFramework Framework { get; private set; } = null!;

    [PluginService] public static IGameGui GameGui { get; private set; } = null!;

    [PluginService] public static IJobGauges JobGauges { get; private set; } = null!;

    [PluginService] public static IKeyState KeyState { get; private set; } = null!;

    [PluginService] public static IObjectTable ObjectTable { get; private set; } = null!;

    [PluginService] public static IPartyFinderGui PartyFinderGui { get; private set; } = null!;

    [PluginService] public static IPartyList PartyList { get; private set; } = null!;

    [PluginService] public static ISigScanner SigScanner { get; private set; } = null!;

    [PluginService] public static ITargetManager TargetManager { get; private set; } = null!;

    [PluginService] public static IToastGui ToastGui { get; private set; } = null!;

    public static DalamudApi operator +(DalamudApi container, object o)
    {
        foreach (var f in typeof(DalamudApi).GetProperties())
        {
            if (f.PropertyType != o.GetType()) continue;
            if (f.GetValue(container) != null) break;
            f.SetValue(container, o);
            return container;
        }

        throw new InvalidOperationException();
    }

    public static void Initialize(IDalamudPlugin plugin, IDalamudPluginInterface pluginInterface)
    {
        _ = new DalamudApi(plugin, pluginInterface);
    }

    public static void Dispose()
    {
        pluginCommandManager?.Dispose();
    }
}

#region PluginCommandManager

public class PluginCommandManager<T> : IDisposable where T : IDalamudPlugin
{
    private readonly T plugin;
    private readonly (string, CommandInfo)[] pluginCommands;

    public PluginCommandManager(T p)
    {
        plugin = p;
        pluginCommands = plugin.GetType().GetMethods(BindingFlags.NonPublic | BindingFlags.Public |
                                                     BindingFlags.Static | BindingFlags.Instance)
            .Where(method => method.GetCustomAttribute<CommandAttribute>() != null)
            .SelectMany(GetCommandInfoTuple)
            .ToArray();

        AddCommandHandlers();
    }

    public void Dispose()
    {
        RemoveCommandHandlers();
        GC.SuppressFinalize(this);
    }

    private void AddCommandHandlers()
    {
        foreach (var (command, commandInfo) in pluginCommands)
            DalamudApi.CommandManager.AddHandler(command, commandInfo);
    }

    private void RemoveCommandHandlers()
    {
        foreach (var (command, _) in pluginCommands)
            DalamudApi.CommandManager.RemoveHandler(command);
    }

    private IEnumerable<(string, CommandInfo)> GetCommandInfoTuple(MethodInfo method)
    {
        var handlerDelegate =
            (IReadOnlyCommandInfo.HandlerDelegate)Delegate.CreateDelegate(typeof(IReadOnlyCommandInfo.HandlerDelegate),
                plugin, method);

        var command = handlerDelegate.Method.GetCustomAttribute<CommandAttribute>();
        var aliases = handlerDelegate.Method.GetCustomAttribute<AliasesAttribute>();
        var helpMessage = handlerDelegate.Method.GetCustomAttribute<HelpMessageAttribute>();
        var doNotShowInHelp = handlerDelegate.Method.GetCustomAttribute<DoNotShowInHelpAttribute>();

        var commandInfo = new CommandInfo(handlerDelegate)
        {
            HelpMessage = helpMessage?.HelpMessage ?? string.Empty,
            ShowInHelp = doNotShowInHelp == null
        };

        var commandInfoTuples = new List<(string, CommandInfo)>();
        if (command?.Command is { } cmd)
            commandInfoTuples.Add((cmd, commandInfo));

        if (aliases != null)
            commandInfoTuples.AddRange(aliases.Aliases.Select(alias => (alias, commandInfo)));

        return commandInfoTuples;
    }
}

#endregion

#region Attributes

[AttributeUsage(AttributeTargets.Method)]
public class AliasesAttribute : Attribute
{
    public AliasesAttribute(params string[] aliases)
    {
        Aliases = aliases;
    }

    public string[] Aliases { get; }
}

[AttributeUsage(AttributeTargets.Method)]
public class CommandAttribute : Attribute
{
    public CommandAttribute(string command)
    {
        Command = command;
    }

    public string Command { get; }
}

[AttributeUsage(AttributeTargets.Method)]
public class DoNotShowInHelpAttribute : Attribute
{
}

[AttributeUsage(AttributeTargets.Method)]
public class HelpMessageAttribute : Attribute
{
    public HelpMessageAttribute(string helpMessage)
    {
        HelpMessage = helpMessage;
    }

    public string HelpMessage { get; }
}

#endregion