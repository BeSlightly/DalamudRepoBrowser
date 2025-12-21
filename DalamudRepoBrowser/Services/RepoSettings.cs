using System;

namespace DalamudRepoBrowser;

internal sealed class RepoSettings
{
    private readonly object repoSettingsObject;
    private readonly Type repoSettingsType;

    public RepoSettings(object repoSettingsObject)
    {
        this.repoSettingsObject = repoSettingsObject;
        repoSettingsType = repoSettingsObject.GetType();
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

    private object? ReadProperty(string name)
    {
        var prop = repoSettingsType.GetProperty(name);
        return prop?.GetValue(repoSettingsObject);
    }

    private void SetProperty(string name, object value)
    {
        var prop = repoSettingsType.GetProperty(name);
        prop?.SetValue(repoSettingsObject, value);
    }
}
