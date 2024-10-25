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
using static Dalamud.Game.Command.IReadOnlyCommandInfo;

// ReSharper disable CheckNamespace
// ReSharper disable UnusedAutoPropertyAccessor.Local

namespace Dalamud
{
    public class DalamudApi
    {
        public static IPluginLog Log { get; private set; } = null!;

        [PluginService]
        //[RequiredVersion("1.0")]
        public static IDalamudPluginInterface PluginInterface { get; private set; }

        [PluginService]
        //[RequiredVersion("1.0")]
        public static IBuddyList BuddyList { get; private set; }

        [PluginService]
        //[RequiredVersion("1.0")]
        public static IChatGui ChatGui { get; private set; }

        // Not referenced.
        //[PluginService]
        //[RequiredVersion("1.0")]
        //public static IChatHandlers ChatHandlers { get; private set; }

        [PluginService]
        //[RequiredVersion("1.0")]
        public static IClientState ClientState { get; private set; }

        [PluginService]
        //[RequiredVersion("1.0")]
        public static ICommandManager CommandManager { get; private set; }

        [PluginService]
        //[RequiredVersion("1.0")]
        public static ICondition Condition { get; private set; }

        [PluginService]
        //[RequiredVersion("1.0")]
        public static IDataManager DataManager { get; private set; }

        [PluginService]
        //[RequiredVersion("1.0")]
        public static IFateTable FateTable { get; private set; }

        [PluginService]
        //[RequiredVersion("1.0")]
        public static IFlyTextGui FlyTextGui { get; private set; }

        [PluginService]
        //[RequiredVersion("1.0")]
        public static IFramework Framework { get; private set; }

        [PluginService]
        //[RequiredVersion("1.0")]
        public static IGameGui GameGui { get; private set; }

        [PluginService]
        //[RequiredVersion("1.0")]
        public static IGameNetwork GameNetwork { get; private set; }

        [PluginService]
        //[RequiredVersion("1.0")]
        public static IJobGauges JobGauges { get; private set; }

        [PluginService]
        //[RequiredVersion("1.0")]
        public static IKeyState KeyState { get; private set; }

        [PluginService]
        //[RequiredVersion("1.0")]
        public static IObjectTable ObjectTable { get; private set; }

        [PluginService]
        //[RequiredVersion("1.0")]
        public static IPartyFinderGui PartyFinderGui { get; private set; }

        [PluginService]
        //[RequiredVersion("1.0")]
        public static IPartyList PartyList { get; private set; }

        [PluginService]
        //[RequiredVersion("1.0")]
        public static ISigScanner SigScanner { get; private set; }

        [PluginService]
        //[RequiredVersion("1.0")]
        public static ITargetManager TargetManager { get; private set; }

        [PluginService]
        //[RequiredVersion("1.0")]
        public static IToastGui ToastGui { get; private set; }

        private static PluginCommandManager<IDalamudPlugin> pluginCommandManager;

        public DalamudApi() { }

        public DalamudApi(IDalamudPlugin plugin) => pluginCommandManager ??= new(plugin);

        public DalamudApi(IDalamudPlugin plugin, IDalamudPluginInterface pluginInterface)
        {
            if (!pluginInterface.Inject(this))
            {
                return;
            }

            pluginCommandManager ??= new(plugin);
        }

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

        public static void Initialize(IDalamudPlugin plugin, IDalamudPluginInterface pluginInterface) => _ = new DalamudApi(plugin, pluginInterface);

        public static void Dispose() => pluginCommandManager?.Dispose();
    }

    #region PluginCommandManager
    public class PluginCommandManager<T> : IDisposable where T : IDalamudPlugin
    {
        private readonly T plugin;
        private readonly (string, CommandInfo)[] pluginCommands;

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

        private IEnumerable<(string, CommandInfo)> GetCommandInfoTuple(MethodInfo method, HandlerDelegate handlerDelegate)
        {
            if (handlerDelegate == null || method == null)
                yield break; // or throw a meaningful exception if handlerDelegate must be provided.

            var command = method.GetCustomAttribute<CommandAttribute>();
            if (command == null)
                yield break; // If no CommandAttribute, skip.

            var aliases = method.GetCustomAttribute<AliasesAttribute>();
            var helpMessage = method.GetCustomAttribute<HelpMessageAttribute>();
            var doNotShowInHelp = method.GetCustomAttribute<DoNotShowInHelpAttribute>();

            var commandInfo = new CommandInfo(handlerDelegate)
            {
                HelpMessage = helpMessage?.HelpMessage ?? string.Empty,
                ShowInHelp = doNotShowInHelp == null,
            };

            // Populate the tuple list
            var commandInfoTuples = new List<(string, CommandInfo)> { (command.Command, commandInfo) };
            if (aliases != null)
                commandInfoTuples.AddRange(aliases.Aliases.Select(alias => (alias, commandInfo)));

            foreach (var tuple in commandInfoTuples)
                yield return tuple;
        }


        public PluginCommandManager(T p)
        {
            plugin = p;
            pluginCommands = plugin.GetType()
    .GetMethods(BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance)
    .Where(method => method.GetCustomAttribute<CommandAttribute>() != null)
    .SelectMany(method => GetCommandInfoTuple(method, handlerDelegate: null)) // or supply a valid handler
    .ToArray();

            AddCommandHandlers();
        }

        public void Dispose()
        {
            RemoveCommandHandlers();
            GC.SuppressFinalize(this);
        }
    }
    #endregion

    #region Attributes
    [AttributeUsage(AttributeTargets.Method)]
    public class AliasesAttribute : Attribute
    {
        public string[] Aliases { get; }

        public AliasesAttribute(params string[] aliases)
        {
            Aliases = aliases;
        }
    }

    [AttributeUsage(AttributeTargets.Method)]
    public class CommandAttribute : Attribute
    {
        public string Command { get; }

        public CommandAttribute(string command)
        {
            Command = command;
        }
    }

    [AttributeUsage(AttributeTargets.Method)]
    public class DoNotShowInHelpAttribute : Attribute
    {
    }

    [AttributeUsage(AttributeTargets.Method)]
    public class HelpMessageAttribute : Attribute
    {
        public string HelpMessage { get; }

        public HelpMessageAttribute(string helpMessage)
        {
            HelpMessage = helpMessage;
        }
    }
    #endregion
}
