using System.Collections.Generic;
using Dalamud.Configuration;

namespace DalamudRepoBrowser;

public class Configuration : IPluginConfiguration
{
    public bool HideEnabledRepos = false;
    public long LastUpdatedRepoList = 0;
    public int MaxPlugins = 20;
    public string RepoMasters = string.Empty;
    public int RepoSort = 0;
    public HashSet<string> SeenRepos = new();

    public bool ShowOutdatedPlugins { get; set; }
    public int Version { get; set; }


    public void Initialize()
    {
        if (Version < 2)
        {
            ShowOutdatedPlugins = true;
            Version = 2;
        }
    }

    public void Save()
    {
        DalamudApi.PluginInterface.SavePluginConfig(this);
    }
}