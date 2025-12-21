using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace DalamudRepoBrowser;

public sealed class RepoInfo
{
    public string Owner { get; }
    public string FullName { get; }
    public long LastUpdated { get; }
    public byte ApiLevel { get; }
    public string Url { get; }
    public string RawUrl { get; }
    public string GitRepoUrl { get; }
    public bool IsDefaultBranch { get; }
    public string BranchName { get; }
    public IReadOnlyList<PluginInfo> Plugins { get; }

    public RepoInfo(JToken json, string rawUrl)
    {
        var plugins = new List<PluginInfo>();
        if (json["plugins"] is JArray pluginsArray)
        {
            foreach (var plugin in pluginsArray)
            {
                plugins.Add(new PluginInfo(plugin));
            }
        }

        Plugins = plugins;
        Owner = (string?)json["repo_developer_name"] ?? string.Empty;
        var repoName = (string?)json["repo_name"] ?? string.Empty;
        FullName = !string.IsNullOrEmpty(Owner) && !string.IsNullOrEmpty(repoName)
            ? $"{Owner}/{repoName}"
            : repoName;

        LastUpdated = Plugins.Count > 0 ? Plugins.Max(p => p.LastUpdate) : 0;
        ApiLevel = Plugins.Count > 0 ? Plugins[0].ApiLevel : (byte)0;

        Url = (string?)json["repo_url"] ?? string.Empty;
        RawUrl = rawUrl;
        GitRepoUrl = (string?)json["repo_source_url"] ?? string.Empty;
        IsDefaultBranch = true;
        BranchName = string.Empty;
    }

    public RepoInfo(RepoInfo source, IReadOnlyList<PluginInfo> plugins)
    {
        Plugins = plugins;
        Owner = source.Owner;
        FullName = source.FullName;
        Url = source.Url;
        RawUrl = source.RawUrl;
        GitRepoUrl = source.GitRepoUrl;
        IsDefaultBranch = source.IsDefaultBranch;
        BranchName = source.BranchName;
        LastUpdated = Plugins.Count > 0 ? Plugins.Max(p => p.LastUpdate) : 0;
        ApiLevel = Plugins.Count > 0 ? Plugins.Max(p => p.ApiLevel) : (byte)0;
    }
}
