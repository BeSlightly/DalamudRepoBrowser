using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Newtonsoft.Json.Linq;

namespace DalamudRepoBrowser;

internal sealed class RepoManager : IDisposable
{
    private const long CacheTtlMilliseconds = 86400000;

    public const string RepoMasterUrl = "https://raw.githubusercontent.com/BeSlightly/Aetherfeed/refs/heads/main/public/data/plugins.json";
    public const string PriorityReposUrl = "https://raw.githubusercontent.com/BeSlightly/Aetherfeed/refs/heads/main/public/data/priority-repos.json";

    private readonly Configuration config;
    private readonly IDalamudPluginInterface pluginInterface;
    private readonly IPluginLog log;
    private readonly DalamudRepoSettingsAccessor repoSettingsAccessor;
    private readonly HttpClient httpClient;
    private readonly HashSet<string> fetchedRepos = new();
    private readonly object fetchedReposLock = new();

    private IReadOnlyList<RepoInfo> repoList = Array.Empty<RepoInfo>();
    private int fetchId;
    private int sortCountdown;

    public RepoManager(Configuration config, IDalamudPluginInterface pluginInterface, IPluginLog log)
    {
        this.config = config;
        this.pluginInterface = pluginInterface;
        this.log = log;
        repoSettingsAccessor = new DalamudRepoSettingsAccessor(log);

        httpClient = new HttpClient(new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
        });

        httpClient.DefaultRequestHeaders.Add("Application-Name", "DalamudRepoBrowser");
        httpClient.DefaultRequestHeaders.Add(
            "Application-Version",
            Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                ?.InformationalVersion ?? "1.0.0.0");
    }

    public int CurrentApiLevel => repoSettingsAccessor.CurrentApiLevel;
    public IReadOnlyList<RepoInfo> RepoList => repoList;
    public int SortCountdown => Volatile.Read(ref sortCountdown);

    public void RequestSort()
    {
        Interlocked.Exchange(ref sortCountdown, 1);
    }

    public void Dispose()
    {
        httpClient.Dispose();
    }

    public bool HasRepo(string url) => repoSettingsAccessor.HasRepo(url);

    public bool GetRepoEnabled(string url) => repoSettingsAccessor.GetRepoEnabled(url);

    public void ToggleRepo(string url) => repoSettingsAccessor.ToggleRepo(url);

    public bool TryConsumeSortCountdown()
    {
        if (Volatile.Read(ref sortCountdown) <= 0)
        {
            return false;
        }

        var next = Interlocked.Decrement(ref sortCountdown);
        if (next > 0)
        {
            return false;
        }

        Interlocked.Exchange(ref sortCountdown, 0);
        return true;
    }

    public HashSet<RepoInfo> SortAndUpdateSeen(HashSet<string> prevSeenRepos)
    {
        var currentRepos = RepoList;

        var sorted = config.RepoSort switch
        {
            0 => currentRepos,
            1 => currentRepos.OrderBy(repo => repo.Owner).ToList(),
            2 => currentRepos.OrderBy(repo => repo.Url).ToList(),
            3 => currentRepos.OrderByDescending(repo => repo.Plugins.Count).ToList(),
            4 => currentRepos.OrderByDescending(repo => repo.LastUpdated).ToList(),
            _ => currentRepos
        };

        var enabledRepos = new HashSet<RepoInfo>();
        foreach (var repo in sorted)
        {
            if (GetRepoEnabled(repo.Url) || GetRepoEnabled(repo.RawUrl))
            {
                enabledRepos.Add(repo);
            }

            config.SeenRepos.Add(repo.Url);
        }

        repoList = sorted.OrderBy(repo => prevSeenRepos.Contains(repo.Url)).ToList();
        config.Save();

        return enabledRepos;
    }

    public void FetchRepoMasters()
    {
        log.Debug("Manual refetch triggered - clearing caches and fetching fresh data");

        Interlocked.Increment(ref fetchId);
        repoList = Array.Empty<RepoInfo>();

        lock (fetchedReposLock)
        {
            fetchedRepos.Clear();
        }

        config.LastUpdatedPriorityRepos = 0;
        config.LastUpdatedRepoList = 0;
        config.Save();

        log.Debug("Cache timestamps cleared, forcing fresh fetch from remote sources");

        _ = FetchPriorityReposAsync();
        _ = FetchRepoListAsync(RepoMasterUrl);
    }

    private bool ShouldCheckRepoList()
    {
        try
        {
            var filePath = GetReposFilePath();
            if (!File.Exists(filePath) || config.LastUpdatedRepoList == 0)
            {
                return true;
            }

            var now = DateTimeOffset.UtcNow;
            var lastUpdated = DateTimeOffset.FromUnixTimeMilliseconds(config.LastUpdatedRepoList);

            return now.ToUnixTimeMilliseconds() >= config.LastUpdatedRepoList + CacheTtlMilliseconds
                   || (now.Hour >= 8 && lastUpdated.Hour < 8);
        }
        catch
        {
            return true;
        }
    }

    private bool ShouldCheckPriorityReposList()
    {
        try
        {
            var filePath = GetPriorityReposFilePath();
            if (!File.Exists(filePath) || config.LastUpdatedPriorityRepos == 0)
            {
                return true;
            }

            var now = DateTimeOffset.UtcNow;
            var lastUpdated = DateTimeOffset.FromUnixTimeMilliseconds(config.LastUpdatedPriorityRepos);

            return now.ToUnixTimeMilliseconds() >= config.LastUpdatedPriorityRepos + CacheTtlMilliseconds
                   || (now.Hour >= 8 && lastUpdated.Hour < 8);
        }
        catch
        {
            return true;
        }
    }

    private string GetReposFilePath()
    {
        return Path.Combine(pluginInterface.ConfigDirectory.FullName, "repos.json");
    }

    private string GetPriorityReposFilePath()
    {
        return Path.Combine(pluginInterface.ConfigDirectory.FullName, "priority-repos.json");
    }

    private async Task FetchPriorityReposAsync()
    {
        log.Debug($"Fetching priority repositories from {PriorityReposUrl}");

        try
        {
            string data;
            if (ShouldCheckPriorityReposList())
            {
                log.Debug("Retrieving latest priority repos data from master api.");
                data = await httpClient.GetStringAsync(PriorityReposUrl).ConfigureAwait(false);
                await File.WriteAllTextAsync(GetPriorityReposFilePath(), data).ConfigureAwait(false);
                config.LastUpdatedPriorityRepos = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                config.Save();
            }
            else
            {
                log.Debug("Using cached data for priority repos list.");
                data = await File.ReadAllTextAsync(GetPriorityReposFilePath()).ConfigureAwait(false);
            }

            var priorityRepos = JArray.Parse(data);
            config.PriorityRepos.Clear();

            foreach (var repo in priorityRepos)
            {
                var repoUrl = repo.ToString();
                if (!string.IsNullOrEmpty(repoUrl))
                {
                    config.PriorityRepos.Add(repoUrl);
                }
            }

            config.Save();
            log.Debug($"Loaded {config.PriorityRepos.Count} priority repositories");
        }
        catch (Exception ex)
        {
            log.Error(ex, $"Failed loading priority repositories from {PriorityReposUrl}");
        }
    }

    private async Task FetchRepoListAsync(string repoMaster)
    {
        log.Debug($"Fetching repositories from {repoMaster}");

        var startedFetch = Volatile.Read(ref fetchId);
        try
        {
            string data;
            if (ShouldCheckRepoList())
            {
                log.Debug("Retrieving latest data from repo master api.");
                data = await httpClient.GetStringAsync(repoMaster).ConfigureAwait(false);
                await File.WriteAllTextAsync(GetReposFilePath(), data).ConfigureAwait(false);
                config.LastUpdatedRepoList = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                config.Save();
            }
            else
            {
                log.Debug("Using cached data for repo list.");
                data = await File.ReadAllTextAsync(GetReposFilePath()).ConfigureAwait(false);
            }

            var repos = JArray.Parse(data);

            if (Volatile.Read(ref fetchId) != startedFetch)
            {
                return;
            }

            log.Debug($"Fetched {repos.Count} repositories from {repoMaster}");

            var tempRepoList = new List<RepoInfo>();
            foreach (var json in repos)
            {
                RepoInfo info;

                try
                {
                    var repoUrl = (string?)json["repo_url"] ?? string.Empty;
                    var rawUrl = RepoUrlHelper.GetRawUrl(repoUrl);
                    info = new RepoInfo(json, rawUrl);
                }
                catch (Exception ex)
                {
                    log.Error(ex, $"Failed parsing {(string?)json["repo_url"]}.");
                    continue;
                }

                lock (fetchedReposLock)
                {
                    if (!fetchedRepos.Add(info.Url))
                    {
                        log.Error($"{info.Url} has already been fetched");
                        continue;
                    }
                }

                if (info.Plugins.Count == 0)
                {
                    log.Debug($"{info.Url} contains no usable plugins!");
                    continue;
                }

                tempRepoList.Add(info);
            }

            log.Debug($"Applying plugin deduplication with {config.PriorityRepos.Count} priority repos");
            var deduplicatedRepos = ApplyPluginDeduplication(tempRepoList);
            log.Debug($"Deduplication complete: {tempRepoList.Count} -> {deduplicatedRepos.Count} repos");

            repoList = deduplicatedRepos;
            Interlocked.Exchange(ref sortCountdown, 60);
        }
        catch (Exception ex)
        {
            log.Error(ex, $"Failed loading repositories from {repoMaster}");
        }
    }

    private List<RepoInfo> ApplyPluginDeduplication(List<RepoInfo> repos)
    {
        log.Debug($"Starting deduplication with {repos.Count} repos");

        var pluginGroups = new Dictionary<string, List<(RepoInfo repo, PluginInfo plugin)>>();
        foreach (var repo in repos)
        {
            foreach (var plugin in repo.Plugins)
            {
                var pluginKey = !string.IsNullOrEmpty(plugin.Name) ? plugin.Name : "Unknown";

                if (!pluginGroups.TryGetValue(pluginKey, out var list))
                {
                    list = new List<(RepoInfo repo, PluginInfo plugin)>();
                    pluginGroups[pluginKey] = list;
                }

                list.Add((repo, plugin));
            }
        }

        log.Debug($"Found {pluginGroups.Count} unique plugins across all repos");

        var duplicatePlugins = pluginGroups.Where(pg => pg.Value.Count > 1).ToList();
        log.Debug($"Found {duplicatePlugins.Count} plugins with duplicates that need deduplication");

        var result = new List<RepoInfo>();
        var addedRepos = new HashSet<string>();

        foreach (var pluginGroup in pluginGroups)
        {
            var candidates = pluginGroup.Value;

            if (candidates.Count == 1)
            {
                var (repo, _) = candidates[0];
                if (addedRepos.Add(repo.Url))
                {
                    result.Add(repo);
                }
            }
            else
            {
                var priorityCandidates = candidates.Where(c => config.PriorityRepos.Contains(c.repo.Url)).ToList();

                if (priorityCandidates.Any())
                {
                    var bestCandidate = GetBestCandidate(priorityCandidates);
                    if (addedRepos.Add(bestCandidate.repo.Url))
                    {
                        result.Add(bestCandidate.repo);
                    }
                }
                else
                {
                    foreach (var (repo, _) in candidates)
                    {
                        if (addedRepos.Add(repo.Url))
                        {
                            result.Add(repo);
                        }
                    }
                }
            }
        }

        return result;
    }

    private static (RepoInfo repo, PluginInfo plugin) GetBestCandidate(List<(RepoInfo repo, PluginInfo plugin)> candidates)
    {
        return candidates
            .OrderByDescending(c => c.plugin.ApiLevel)
            .ThenByDescending(c => c.plugin.LastUpdate)
            .First();
    }
}
