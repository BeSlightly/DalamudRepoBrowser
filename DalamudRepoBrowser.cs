using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Dalamud.Plugin;
using Newtonsoft.Json.Linq;

namespace DalamudRepoBrowser;

public struct RepoInfo
{
    public readonly string owner;
    public readonly string fullName;
    public readonly long lastUpdated;
    public readonly byte apiLevel;
    public readonly string url;
    public readonly string rawURL;
    public readonly string gitRepoURL;
    public readonly bool isDefaultBranch;
    public readonly string branchName;
    public readonly List<PluginInfo> plugins = new();

    public RepoInfo(JToken json)
    {
        if (json["plugins"] is JArray pluginsArray)
            foreach (var plugin in pluginsArray)
                plugins.Add(new PluginInfo(plugin));

        owner = (string?)json["repo_developer_name"] ?? string.Empty;
        var repoName = (string?)json["repo_name"] ?? string.Empty;
        fullName = !string.IsNullOrEmpty(owner) && !string.IsNullOrEmpty(repoName) ? $"{owner}/{repoName}" : repoName;

        lastUpdated = plugins.Any() ? plugins.Max(p => p.lastUpdate) : 0;
        apiLevel = plugins.Any() ? plugins.First().apiLevel : (byte)0;

        url = (string?)json["repo_url"] ?? string.Empty;
        rawURL = DalamudRepoBrowser.GetRawURL(url);
        gitRepoURL = (string?)json["repo_source_url"] ?? string.Empty;

        isDefaultBranch = true;
        branchName = string.Empty;
    }
}

public struct PluginInfo
{
    public readonly string name;
    public readonly string description;
    public readonly string punchline;
    public readonly string repoURL;
    public readonly byte apiLevel;
    public readonly long lastUpdate;
    public readonly List<string> tags;
    public readonly List<string> categoryTags;

    public PluginInfo(JToken json)
    {
        name = (string?)json["Name"] ?? string.Empty;
        description = (string?)json["Description"] ?? string.Empty;
        repoURL = (string?)json["RepoUrl"] ?? string.Empty;
        apiLevel = (byte?)json["DalamudApiLevel"] ?? 0;
        lastUpdate = (long?)json["LastUpdate"] ?? 0;

        punchline = string.Empty;
        tags = new List<string>();
        categoryTags = new List<string>();
    }
}

public class DalamudRepoBrowser : IDalamudPlugin
{
    public static int currentAPILevel;
    private static PropertyInfo? dalamudRepoSettingsProperty;
    private static IEnumerable? dalamudRepoSettings;

    public static readonly string repoMaster =
        @"https://github.com/BeSlightly/Aetherfeed/raw/refs/heads/main/docs/config.json";
    
    public static readonly string priorityReposMaster =
        @"https://raw.githubusercontent.com/BeSlightly/Aetherfeed/refs/heads/main/docs/priority-repos.json";

    public static List<RepoInfo> repoList = new();
    public static HashSet<string> fetchedRepos = new();
    public static int sortList;
    public static HashSet<string> prevSeenRepos = new();
    public static Regex githubRegex = new("github");
    public static Regex rawRegex = new("\\/raw");

    private static readonly HttpClient httpClient = new(new HttpClientHandler
        { AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate });

    private static int fetch;

    private static Assembly dalamudAssembly = null!;
    private static Type dalamudServiceType = null!;
    private static Type thirdPartyRepoSettingsType = null!;
    private static object dalamudPluginManager = null!;
    private static object dalamudConfig = null!;
    private static MethodInfo pluginReload = null!;
    private static MethodInfo configSave = null!;

    public DalamudRepoBrowser(IDalamudPluginInterface pluginInterface)
    {
        Plugin = this;
        DalamudApi.Initialize(this, pluginInterface);

        httpClient.DefaultRequestHeaders.Add("Application-Name", "DalamudRepoBrowser");
        httpClient.DefaultRequestHeaders.Add("Application-Version",
            Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                ?.InformationalVersion ?? "1.0.0.0");

        Config = (Configuration?)DalamudApi.PluginInterface.GetPluginConfig() ?? new Configuration();
        Config.Initialize();

        DalamudApi.PluginInterface.UiBuilder.OpenConfigUi += ToggleConfig;

        try
        {
            ReflectRepos();
            DalamudApi.PluginInterface.UiBuilder.Draw += PluginUI.Draw;
            prevSeenRepos = Config.SeenRepos.ToHashSet();
        }
        catch (Exception e)
        {
            DalamudApi.PluginLog.Error(e, "Failed to load.");
        }
    }

    public string Name => "DalamudRepoBrowser";
    public static DalamudRepoBrowser Plugin { get; private set; } = null!;
    public static Configuration Config { get; private set; } = null!;

    public static IEnumerable? DalamudRepoSettings
    {
        get => dalamudRepoSettings ??= (IEnumerable?)dalamudRepoSettingsProperty?.GetValue(dalamudConfig);
        set => dalamudRepoSettings = value;
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }


    private static object? GetService(string type)
    {
        var getType = dalamudAssembly.GetType(type);
        if (getType == null) return null;
        var getService = dalamudServiceType.MakeGenericType(getType).GetMethod("Get");
        return getService?.Invoke(null, null);
    }

    private static void ReflectRepos()
    {
        dalamudAssembly = Assembly.GetAssembly(typeof(IDalamudPluginInterface))!;
        dalamudServiceType = dalamudAssembly?.GetType("Dalamud.Service`1")!;
        thirdPartyRepoSettingsType = dalamudAssembly?.GetType("Dalamud.Configuration.ThirdPartyRepoSettings")!;
        if (dalamudServiceType == null || thirdPartyRepoSettingsType == null)
            throw new NullReferenceException($"\nDS: {dalamudServiceType}\n3PRS: {thirdPartyRepoSettingsType}");

        dalamudPluginManager = GetService("Dalamud.Plugin.Internal.PluginManager")!;
        dalamudConfig = GetService("Dalamud.Configuration.Internal.DalamudConfiguration")!;

        currentAPILevel = typeof(IDalamudPluginInterface).Assembly.GetName().Version!.Major;

        dalamudRepoSettingsProperty = dalamudConfig?.GetType()
            .GetProperty("ThirdRepoList", BindingFlags.Instance | BindingFlags.Public);

        pluginReload = dalamudPluginManager?.GetType()
            .GetMethod("SetPluginReposFromConfigAsync", BindingFlags.Instance | BindingFlags.Public)!;

        configSave = dalamudConfig?.GetType()
            .GetMethod("QueueSave", BindingFlags.Instance | BindingFlags.Public)!;

        if (dalamudPluginManager == null || dalamudConfig == null || dalamudRepoSettingsProperty == null ||
            pluginReload == null || configSave == null)
            throw new NullReferenceException(
                $"\nDPM: {dalamudPluginManager}\nDC: {dalamudConfig}\nDRS: {dalamudRepoSettingsProperty}\nPR: {pluginReload}\nCS: {configSave}");
    }

    public static void AddRepo(string url)
    {
        var add = DalamudRepoSettings?.GetType()
            .GetMethod("Add", BindingFlags.Instance | BindingFlags.Public);
        if (add == null) return;

        var obj = Activator.CreateInstance(thirdPartyRepoSettingsType);
        if (obj == null) return;

        _ = new RepoSettings(obj)
        {
            Url = url,
            IsEnabled = true
        };

        add.Invoke(DalamudRepoSettings, new[] { obj });
        SaveDalamudConfig();
        ReloadPluginMasters();
    }

    public static string GetRawURL(string url)
    {
        return url.StartsWith("https://raw.githubusercontent.com")
            ? url
            : githubRegex.Replace(rawRegex.Replace(url, "", 1), "raw.githubusercontent", 1);
    }

    public static RepoSettings? GetRepoSettings(string url)
    {
        if (DalamudRepoSettings == null) return null;
        return (from object obj in DalamudRepoSettings select new RepoSettings(obj)).FirstOrDefault(repoSettings =>
            repoSettings.Url == url);
    }

    public static bool HasRepo(string url)
    {
        return GetRepoSettings(url) != null;
    }

    public static void ToggleRepo(string url)
    {
        try
        {
            var repo = GetRepoSettings(url);
            if (repo != null)
                repo.IsEnabled ^= true;
            else
                AddRepo(url);
        }
        catch
        {
            AddRepo(url);
        }
    }

    public static void FetchRepoMasters()
    {
        lock (repoList)
        {
            fetch++;
            repoList.Clear();
        }

        lock (fetchedRepos)
        {
            fetchedRepos.Clear();
        }

        FetchPriorityReposAsync();
        FetchRepoListAsync(repoMaster);
    }

    private static bool ShouldCheckRepoList()
    {
        try
        {
            return !File.Exists(GetReposFilePath())
                   || Config.LastUpdatedRepoList == 0
                   || new DateTimeOffset(DateTime.UtcNow).ToUnixTimeMilliseconds() >=
                   Config.LastUpdatedRepoList + 86400000
                   || (DateTime.UtcNow.Hour >= 8 &&
                       DateTimeOffset.FromUnixTimeSeconds(Config.LastUpdatedRepoList).Hour < 8);
        }
        catch
        {
            return true;
        }
    }

    public static string GetReposFilePath()
    {
        return DalamudApi.PluginInterface.ConfigDirectory.FullName + "/repos.json";
    }

    public static string GetPriorityReposFilePath()
    {
        return DalamudApi.PluginInterface.ConfigDirectory.FullName + "/priority-repos.json";
    }

    private static bool ShouldCheckPriorityReposList()
    {
        try
        {
            return !File.Exists(GetPriorityReposFilePath())
                   || Config.LastUpdatedPriorityRepos == 0
                   || new DateTimeOffset(DateTime.UtcNow).ToUnixTimeMilliseconds() >=
                   Config.LastUpdatedPriorityRepos + 86400000
                   || (DateTime.UtcNow.Hour >= 8 &&
                       DateTimeOffset.FromUnixTimeSeconds(Config.LastUpdatedPriorityRepos).Hour < 8);
        }
        catch
        {
            return true;
        }
    }

    public static void FetchPriorityReposAsync()
    {
        DalamudApi.PluginLog.Information($"Fetching priority repositories from {priorityReposMaster}");

        Task.Run(() =>
        {
            try
            {
                string data;
                if (ShouldCheckPriorityReposList())
                {
                    DalamudApi.PluginLog.Information("Retrieving latest priority repos data from master api.");
                    data = httpClient.GetStringAsync(priorityReposMaster).Result;
                    File.WriteAllText(GetPriorityReposFilePath(), data);
                    Config.LastUpdatedPriorityRepos = new DateTimeOffset(DateTime.UtcNow).ToUnixTimeMilliseconds();
                    Config.Save();
                }
                else
                {
                    DalamudApi.PluginLog.Information("Using cached data for priority repos list.");
                    data = File.ReadAllText(GetPriorityReposFilePath());
                }

                var priorityRepos = JArray.Parse(data);
                Config.PriorityRepos.Clear();
                
                foreach (var repo in priorityRepos)
                {
                    var repoUrl = repo.ToString();
                    if (!string.IsNullOrEmpty(repoUrl))
                    {
                        Config.PriorityRepos.Add(repoUrl);
                    }
                }
                
                Config.Save();
                DalamudApi.PluginLog.Information($"Loaded {Config.PriorityRepos.Count} priority repositories");
            }
            catch (Exception e)
            {
                DalamudApi.PluginLog.Error(e, $"Failed loading priority repositories from {priorityReposMaster}");
            }
        });
    }

    public static void FetchRepoListAsync(string repoMaster)
    {
        DalamudApi.PluginLog.Information($"Fetching repositories from {repoMaster}");

        var startedFetch = fetch;
        Task.Run(() =>
        {
            try
            {
                string data;
                if (ShouldCheckRepoList())
                {
                    DalamudApi.PluginLog.Information("Retrieving latest data from repo master api.");
                    data = httpClient.GetStringAsync(repoMaster).Result;
                    File.WriteAllText(GetReposFilePath(), data);
                    Config.LastUpdatedRepoList = new DateTimeOffset(DateTime.UtcNow).ToUnixTimeMilliseconds();
                    Config.Save();
                }
                else
                {
                    DalamudApi.PluginLog.Information("Using cached data for repo list.");
                    data = File.ReadAllText(GetReposFilePath());
                }

                var repos = JArray.Parse(data);

                if (fetch != startedFetch) return;

                DalamudApi.PluginLog.Information($"Fetched {repos.Count} repositories from {repoMaster}");

                var tempRepoList = new List<RepoInfo>();
                
                foreach (var json in repos)
                {
                    RepoInfo info;

                    try
                    {
                        info = new RepoInfo(json);
                    }
                    catch
                    {
                        DalamudApi.PluginLog.Error($"Failed parsing {(string?)json["repo_url"]}.");
                        continue;
                    }

                    lock (fetchedRepos)
                    {
                        if (!fetchedRepos.Add(info.url))
                        {
                            DalamudApi.PluginLog.Error($"{info.url} has already been fetched");
                            continue;
                        }
                    }

                    if (info.plugins.Count == 0)
                    {
                        DalamudApi.PluginLog.Information($"{info.url} contains no usable plugins!");
                        continue;
                    }

                    tempRepoList.Add(info);
                }
                
                // Apply plugin deduplication using priority repos
                DalamudApi.PluginLog.Information($"Applying plugin deduplication with {Config.PriorityRepos.Count} priority repos");
                var deduplicatedRepos = ApplyPluginDeduplication(tempRepoList);
                DalamudApi.PluginLog.Information($"Deduplication complete: {tempRepoList.Count} -> {deduplicatedRepos.Count} repos");
                
                lock (repoList)
                {
                    if (fetch != startedFetch) return;
                    repoList.AddRange(deduplicatedRepos);
                }

                sortList = 60;
            }
            catch (Exception e)
            {
                DalamudApi.PluginLog.Error(e, $"Failed loading repositories from {repoMaster}");
            }
        });
    }

    private static List<RepoInfo> ApplyPluginDeduplication(List<RepoInfo> repos)
    {
        DalamudApi.PluginLog.Information($"Starting deduplication with {repos.Count} repos");
        
        // Group plugins by their internal name for deduplication
        var pluginGroups = new Dictionary<string, List<(RepoInfo repo, PluginInfo plugin)>>();
        
        foreach (var repo in repos)
        {
            foreach (var plugin in repo.plugins)
            {
                var pluginKey = !string.IsNullOrEmpty(plugin.name) ? plugin.name : "Unknown";
                
                if (!pluginGroups.ContainsKey(pluginKey))
                {
                    pluginGroups[pluginKey] = new List<(RepoInfo, PluginInfo)>();
                }
                
                pluginGroups[pluginKey].Add((repo, plugin));
            }
        }
        
        // Apply deduplication logic based on priority repos
        DalamudApi.PluginLog.Information($"Found {pluginGroups.Count} unique plugins across all repos");
        
        var duplicatePlugins = pluginGroups.Where(pg => pg.Value.Count > 1).ToList();
        DalamudApi.PluginLog.Information($"Found {duplicatePlugins.Count} plugins with duplicates that need deduplication");
        
        // Since we can't modify the readonly struct, we'll return repos that have at least one allowed plugin
        // The actual deduplication will happen at the UI level
        var result = new List<RepoInfo>();
        var addedRepos = new HashSet<string>();
        
        foreach (var pluginGroup in pluginGroups)
        {
            var pluginName = pluginGroup.Key;
            var candidates = pluginGroup.Value;
            
            if (candidates.Count == 1)
            {
                // Only one version, add the repo if not already added
                var (repo, plugin) = candidates[0];
                if (!addedRepos.Contains(repo.url))
                {
                    result.Add(repo);
                    addedRepos.Add(repo.url);
                }
            }
            else
            {
                // Multiple versions, apply priority logic
                var priorityCandidates = candidates.Where(c => Config.PriorityRepos.Contains(c.repo.url)).ToList();
                
                if (priorityCandidates.Any())
                {
                    // Use the best candidate from priority repos
                    var bestCandidate = GetBestCandidate(priorityCandidates);
                    if (!addedRepos.Contains(bestCandidate.repo.url))
                    {
                        result.Add(bestCandidate.repo);
                        addedRepos.Add(bestCandidate.repo.url);
                    }
                }
                else
                {
                    // No priority repos, add all candidate repos
                    foreach (var (repo, plugin) in candidates)
                    {
                        if (!addedRepos.Contains(repo.url))
                        {
                            result.Add(repo);
                            addedRepos.Add(repo.url);
                        }
                    }
                }
            }
        }
        
        return result;
    }
    
    private static (RepoInfo repo, PluginInfo plugin) GetBestCandidate(List<(RepoInfo repo, PluginInfo plugin)> candidates)
    {
        // Sort by API level (descending) then by last update (descending)
        return candidates
            .OrderByDescending(c => c.plugin.apiLevel)
            .ThenByDescending(c => c.plugin.lastUpdate)
            .First();
    }
    


    public static bool GetRepoEnabled(string url)
    {
        var repo = GetRepoSettings(url);
        return repo is { IsEnabled: true };
    }

    public static void ReloadPluginMasters()
    {
        pluginReload?.Invoke(dalamudPluginManager, new object[] { true });
    }

    public static void SaveDalamudConfig()
    {
        configSave?.Invoke(dalamudConfig, null);
    }

    private static void ToggleConfig()
    {
        PluginUI.isVisible ^= true;
        PluginUI.openSettings = false;
    }

    [Command("/xlrepos")]
    [HelpMessage("Opens the repository browser.")]
    private void ToggleConfig(string command, string argument)
    {
        ToggleConfig();
    }

    public static void PrintEcho(string message)
    {
        DalamudApi.ChatGui.Print($"[DalamudRepoBrowser] {message}");
    }

    public static void PrintError(string message)
    {
        DalamudApi.ChatGui.PrintError($"[DalamudRepoBrowser] {message}");
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!disposing) return;
        Config.Save();
        DalamudApi.PluginInterface.UiBuilder.Draw -= PluginUI.Draw;
        DalamudApi.PluginInterface.UiBuilder.OpenConfigUi -= ToggleConfig;
        DalamudApi.Dispose();
        httpClient.Dispose();
    }
}