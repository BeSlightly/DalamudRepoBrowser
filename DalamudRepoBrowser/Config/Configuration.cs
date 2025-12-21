using System;
using System.Collections.Generic;
using Dalamud.Configuration;

namespace DalamudRepoBrowser;

[Serializable]
public class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 0;
    public bool HideEnabledRepos { get; set; } = false;
    public long LastUpdatedRepoList { get; set; } = 0;
    public long LastUpdatedPriorityRepos { get; set; } = 0;
    public int MaxPlugins { get; set; } = 20;
    public string RepoMasters { get; set; } = string.Empty;
    public int RepoSort { get; set; } = 4;
    public HashSet<string> SeenRepos { get; set; } = new();
    public HashSet<string> PriorityRepos { get; set; } = new();
    public bool HideClosedSourcePlugins { get; set; }
    public bool HideNonEnglishPlugins { get; set; }
    public bool ShowOutdatedPlugins { get; set; }
    public bool UseModernUi { get; set; }
    public bool DismissedModernWarning { get; set; }
    public long LastRemoteRepoListUpdatedUtc { get; set; }

    public void Initialize()
    {
        if (Version < 2)
        {
            ShowOutdatedPlugins = true;
            Version = 2;
        }

        if (Version < 3)
        {
            UseModernUi = false;
            Version = 3;
        }

        if (Version < 4)
        {
            HideNonEnglishPlugins = true;
            Version = 4;
        }

        if (Version < 5)
        {
            HideClosedSourcePlugins = false;
            Version = 5;
        }

        if (Version < 6)
        {
            LastRemoteRepoListUpdatedUtc = 0;
            Version = 6;
        }
    }

    public void Save()
    {
        Plugin.PluginInterface.SavePluginConfig(this);
    }
}
