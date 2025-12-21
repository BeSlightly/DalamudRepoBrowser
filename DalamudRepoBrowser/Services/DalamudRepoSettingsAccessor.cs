using System;
using System.Collections;
using System.Reflection;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;

namespace DalamudRepoBrowser;

internal sealed class DalamudRepoSettingsAccessor
{
    private readonly IPluginLog log;
    private bool initialized;

    private Type? serviceType;
    private Type? thirdPartyRepoSettingsType;
    private object? dalamudPluginManager;
    private object? dalamudConfig;
    private PropertyInfo? dalamudRepoSettingsProperty;
    private MethodInfo? pluginReload;
    private MethodInfo? configSave;

    public DalamudRepoSettingsAccessor(IPluginLog log)
    {
        this.log = log;
        CurrentApiLevel = typeof(IDalamudPluginInterface).Assembly.GetName().Version?.Major ?? 0;
    }

    public int CurrentApiLevel { get; }

    public bool HasRepo(string url)
    {
        return GetRepoSettings(url) != null;
    }

    public bool GetRepoEnabled(string url)
    {
        return GetRepoSettings(url) is { IsEnabled: true };
    }

    public void ToggleRepo(string url)
    {
        try
        {
            var repo = GetRepoSettings(url);
            if (repo != null)
            {
                repo.IsEnabled = !repo.IsEnabled;
                SaveAndReload();
                return;
            }

            AddRepo(url);
        }
        catch (Exception ex)
        {
            log.Error(ex, "Failed toggling repository.");
        }
    }

    private RepoSettings? GetRepoSettings(string url)
    {
        var repoSettings = GetRepoSettingsList();
        if (repoSettings == null)
        {
            return null;
        }

        foreach (var obj in repoSettings)
        {
            var settings = new RepoSettings(obj);
            if (settings.Url == url)
            {
                return settings;
            }
        }

        return null;
    }

    private IEnumerable? GetRepoSettingsList()
    {
        if (!EnsureInitialized())
        {
            return null;
        }

        return (IEnumerable?)dalamudRepoSettingsProperty?.GetValue(dalamudConfig);
    }

    private void AddRepo(string url)
    {
        if (!EnsureInitialized())
        {
            return;
        }

        var repoSettings = GetRepoSettingsList();
        if (repoSettings == null)
        {
            return;
        }

        var add = repoSettings.GetType().GetMethod("Add", BindingFlags.Instance | BindingFlags.Public);
        if (add == null || thirdPartyRepoSettingsType == null)
        {
            return;
        }

        var obj = Activator.CreateInstance(thirdPartyRepoSettingsType);
        if (obj == null)
        {
            return;
        }

        _ = new RepoSettings(obj)
        {
            Url = url,
            IsEnabled = true
        };

        add.Invoke(repoSettings, new[] { obj });
        SaveAndReload();
    }

    private bool EnsureInitialized()
    {
        if (initialized)
        {
            return true;
        }

        try
        {
            var dalamudAssembly = typeof(IDalamudPluginInterface).Assembly;
            serviceType = dalamudAssembly.GetType("Dalamud.Service`1");
            thirdPartyRepoSettingsType = dalamudAssembly.GetType("Dalamud.Configuration.ThirdPartyRepoSettings");

            if (serviceType == null || thirdPartyRepoSettingsType == null)
            {
                log.Error("Failed to locate Dalamud service types.");
                return false;
            }

            dalamudPluginManager = GetService("Dalamud.Plugin.Internal.PluginManager");
            dalamudConfig = GetService("Dalamud.Configuration.Internal.DalamudConfiguration");

            if (dalamudPluginManager == null || dalamudConfig == null)
            {
                log.Error("Failed to locate Dalamud services.");
                return false;
            }

            dalamudRepoSettingsProperty = dalamudConfig.GetType()
                .GetProperty("ThirdRepoList", BindingFlags.Instance | BindingFlags.Public);

            pluginReload = dalamudPluginManager.GetType()
                .GetMethod("SetPluginReposFromConfigAsync", BindingFlags.Instance | BindingFlags.Public);

            configSave = dalamudConfig.GetType()
                .GetMethod("QueueSave", BindingFlags.Instance | BindingFlags.Public);

            if (dalamudRepoSettingsProperty == null || pluginReload == null || configSave == null)
            {
                log.Error("Failed to bind Dalamud configuration accessors.");
                return false;
            }

            initialized = true;
        }
        catch (Exception ex)
        {
            log.Error(ex, "Failed to initialize Dalamud repo settings accessor.");
        }

        return initialized;
    }

    private object? GetService(string typeName)
    {
        if (serviceType == null)
        {
            return null;
        }

        var getType = serviceType.Assembly.GetType(typeName);
        if (getType == null)
        {
            return null;
        }

        var getService = serviceType.MakeGenericType(getType).GetMethod("Get", BindingFlags.Public | BindingFlags.Static);
        return getService?.Invoke(null, null);
    }

    private void SaveAndReload()
    {
        configSave?.Invoke(dalamudConfig, null);
        pluginReload?.Invoke(dalamudPluginManager, new object[] { true });
    }
}
