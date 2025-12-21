using System;

namespace DalamudRepoBrowser;

public class RepoSettings
{
    private readonly object repoSettingsObject;
    private readonly Type repoSettingsType;

    public RepoSettings(object o)
    {
        repoSettingsObject = o;
        repoSettingsType = o.GetType();
    }

    public string Url
    {
        get => (string?)ReadProperty("Url") ?? string.Empty;
        set => SetProperty("Url", value);
    }

    public bool IsEnabled
    {
        get => (bool?)ReadProperty("IsEnabled") ?? false;
        set => SetProperty("IsEnabled", value);
    }

    private object? ReadProperty(string type)
    {
        var prop = repoSettingsType.GetProperty(type);
        return prop?.GetValue(repoSettingsObject);
    }

    private void SetProperty(string type, object val)
    {
        var prop = repoSettingsType.GetProperty(type);
        prop?.SetValue(repoSettingsObject, val);
        DalamudRepoBrowser.SaveDalamudConfig();
        DalamudRepoBrowser.ReloadPluginMasters();
    }
}