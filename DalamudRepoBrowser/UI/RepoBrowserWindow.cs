using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Windowing;

namespace DalamudRepoBrowser;

internal sealed class RepoBrowserWindow : Window, IDisposable
{
    private readonly RepoManager repoManager;
    private readonly Configuration config;
    private readonly HashSet<string> prevSeenRepos;

    private bool openSettings;
    private bool firstOpen = true;
    private HashSet<RepoInfo> enabledRepos = new();
    private HashSet<RepoInfo> searchResults = new();
    private string searchText = string.Empty;
    private uint filteredCount;

    public RepoBrowserWindow(RepoManager repoManager, Configuration config)
        : base("Repository Browser", ImGuiWindowFlags.NoCollapse)
    {
        this.repoManager = repoManager;
        this.config = config;
        prevSeenRepos = config.SeenRepos.ToHashSet();

        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(830, 570),
            MaximumSize = new Vector2(9999)
        };
    }

    public void Dispose()
    {
    }

    public void OpenSettings()
    {
        IsOpen = true;
        openSettings = true;
    }

    public override void Draw()
    {
        if (repoManager.TryConsumeSortCountdown())
        {
            enabledRepos = repoManager.SortAndUpdateSeen(prevSeenRepos);
        }

        if (firstOpen)
        {
            repoManager.FetchRepoMasters();
            firstOpen = false;
        }

        ImGui.SetWindowFontScale(0.85f);
        if (AddHeaderIcon("RefreshRepoMaster", FontAwesomeIcon.SyncAlt.ToIconString()))
        {
            repoManager.FetchRepoMasters();
        }
        ImGui.SetWindowFontScale(1);

        ImGui.PushFont(UiBuilder.IconFont);

        if (ImGui.Button(FontAwesomeIcon.Wrench.ToIconString()))
        {
            openSettings = !openSettings;
        }

        ImGui.SameLine();

        if (ImGui.Button(FontAwesomeIcon.Globe.ToIconString()))
        {
            OpenUrl("https://beslightly.github.io/Aetherfeed/");
        }

        ImGui.PopFont();

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Source");
        }

        ImGui.SameLine();

        ImGui.TextColored(new Vector4(1, 0, 0, 1), "DO NOT INSTALL FROM REPOSITORIES YOU DO NOT TRUST.");

        var repos = repoManager.RepoList;
        var inputWidth = ImGui.GetWindowContentRegionMax().X / 4;
        ImGui.SameLine(inputWidth * 3);
        ImGui.SetNextItemWidth(inputWidth);
        if (ImGui.InputTextWithHint(
                "##Search",
                $"Search {filteredCount} / {repos.Count} Repos",
                ref searchText,
                64))
        {
            searchResults = repos.Where(repo => RepoMatchesSearch(repo, searchText)).ToHashSet();
        }

        if (openSettings)
        {
            var save = false;

            ImGui.Columns(2, string.Empty, false);

            var showOutdated = config.ShowOutdatedPlugins;
            if (ImGui.Checkbox("Show Outdated Plugins", ref showOutdated))
            {
                config.ShowOutdatedPlugins = showOutdated;
                save = true;
            }

            ImGui.Spacing();

            ImGui.TextUnformatted("\tSort");
            var repoSort = config.RepoSort;
            var repoSortChanged = false;
            repoSortChanged |= ImGui.RadioButton("Owner", ref repoSort, 1);
            ImGui.SameLine();
            repoSortChanged |= ImGui.RadioButton("URL", ref repoSort, 2);
            ImGui.SameLine();
            repoSortChanged |= ImGui.RadioButton("# Plugins", ref repoSort, 3);
            ImGui.SameLine();
            repoSortChanged |= ImGui.RadioButton("Last Updated", ref repoSort, 4);
            if (repoSortChanged)
            {
                config.RepoSort = repoSort;
                save = true;
            }

            ImGui.NextColumn();
            ImGui.TextUnformatted(string.Empty);

            var hideEnabled = config.HideEnabledRepos;
            if (ImGui.Checkbox("Hide Enabled Repos", ref hideEnabled))
            {
                config.HideEnabledRepos = hideEnabled;
                save = true;
            }

            ImGui.SetNextItemWidth(ImGui.GetWindowWidth() / 5);
            var maxPlugins = config.MaxPlugins;
            if (ImGui.SliderInt(
                config.MaxPlugins >= 50 ? "∞ Plugins###MaxPlugins" : "Maximum Plugins###MaxPlugins",
                ref maxPlugins,
                20,
                50))
            {
                config.MaxPlugins = maxPlugins;
                save = true;
            }

            ImGui.Columns();

            if (save)
            {
                repoManager.RequestSort();
                config.Save();
            }
        }

        ImGui.Separator();

        ImGui.BeginChild("RepoList");
        var indent = 32 * ImGuiHelpers.GlobalScale;
        var spacing = indent / 6;
        var padding = indent / 8;

        filteredCount = 0;
        foreach (var repoInfo in repos)
        {
            var hasAnyValidPlugins = repoInfo.Plugins.Any(p => p.ApiLevel == repoManager.CurrentApiLevel);
            if (!config.ShowOutdatedPlugins && !hasAnyValidPlugins)
            {
                continue;
            }

            if (config.MaxPlugins < 50 && config.MaxPlugins < repoInfo.Plugins.Count)
            {
                continue;
            }

            if (!string.IsNullOrEmpty(searchText) && !searchResults.Contains(repoInfo))
            {
                continue;
            }

            var enabled = repoManager.GetRepoEnabled(repoInfo.Url) || repoManager.GetRepoEnabled(repoInfo.RawUrl);
            if (enabled && config.HideEnabledRepos && enabledRepos.Contains(repoInfo))
            {
                continue;
            }

            filteredCount++;

            var seen = prevSeenRepos.Contains(repoInfo.Url);

            if (ImGui.Button($"Copy Link##{repoInfo.Url}"))
            {
                ImGui.SetClipboardText(repoInfo.Url);
            }

            ImGui.SameLine();

            ImGui.PushFont(UiBuilder.IconFont);

            if (!string.IsNullOrEmpty(repoInfo.GitRepoUrl)
                && ImGui.Button($"{FontAwesomeIcon.Globe.ToIconString()}##{repoInfo.Url}"))
            {
                OpenUrl(repoInfo.GitRepoUrl);
            }

            ImGui.PopFont();

            if (!seen)
            {
                ImGui.GetWindowDrawList().AddRectFilledMultiColor(
                    ImGui.GetItemRectMin(),
                    new Vector2(
                        ImGui.GetWindowPos().X + ImGui.GetWindowSize().X,
                        ImGui.GetItemRectMax().Y + ImGui.GetItemRectSize().Y),
                    0,
                    ImGui.GetColorU32(ImGuiCol.TitleBgActive) | 0xFF000000,
                    0,
                    0);
            }

            ImGui.SameLine();

            if (ImGui.Checkbox($"{repoInfo.Url}##Enabled", ref enabled))
            {
                repoManager.ToggleRepo(repoManager.HasRepo(repoInfo.RawUrl) ? repoInfo.RawUrl : repoInfo.Url);
                if (enabled)
                {
                    enabledRepos.Add(repoInfo);
                }
                else
                {
                    enabledRepos.Remove(repoInfo);
                }
            }

            ImGui.TextUnformatted($"Owner: {repoInfo.Owner}");
            ImGui.SameLine();
            if (!repoInfo.IsDefaultBranch && !string.IsNullOrEmpty(repoInfo.BranchName))
            {
                ImGui.TextUnformatted($"Branch: {repoInfo.BranchName}");
            }

            if (repoInfo.LastUpdated > 0)
            {
                ImGui.SameLine();
                var lastUpdatedStr = DateTimeOffset.FromUnixTimeSeconds(repoInfo.LastUpdated)
                    .LocalDateTime
                    .ToString("yyyy-MM-dd");
                ImGui.TextUnformatted($"Updated: {lastUpdatedStr}");
            }

            ImGui.NewLine();

            ImGui.Indent(indent);
            ImGui.SetWindowFontScale(0.9f);

            var count = 0;
            foreach (var plugin in repoInfo.Plugins)
            {
                var valid = plugin.ApiLevel == repoManager.CurrentApiLevel;
                if (!config.ShowOutdatedPlugins && !valid)
                {
                    continue;
                }

                var prevCursor = ImGui.GetCursorPos();
                ImGui.Dummy(ImGui.CalcTextSize(plugin.Name));
                var textMin = ImGui.GetItemRectMin();
                var textMax = ImGui.GetItemRectMax();
                textMin.X -= padding;
                textMax.X += padding;
                var drawList = ImGui.GetWindowDrawList();
                drawList.AddRectFilled(textMin, textMax, valid ? 0x20FFFFFFu : 0x200000FFu, ImGui.GetStyle().FrameRounding);
                ImGui.SetCursorPos(prevCursor);

                if (!valid)
                {
                    const uint color = 0xA00000FF;
                    var thickness = 2 * ImGuiHelpers.GlobalScale;
                    drawList.AddLine(textMin, textMax, color, thickness);
                    drawList.AddLine(new Vector2(textMin.X, textMax.Y), new Vector2(textMax.X, textMin.Y), color, thickness);
                }

                ImGui.Text(plugin.Name);
                if (ImGui.IsItemHovered())
                {
                    var hasRepo = !string.IsNullOrEmpty(plugin.RepoUrl);
                    var hasPunchline = !string.IsNullOrEmpty(plugin.Punchline);
                    var hasDescription = !string.IsNullOrEmpty(plugin.Description);
                    var tooltip = hasRepo ? plugin.RepoUrl : string.Empty;

                    if (hasPunchline)
                    {
                        tooltip += hasRepo ? $"\n-\n{plugin.Punchline}" : plugin.Punchline;
                    }

                    if (hasDescription)
                    {
                        tooltip += hasRepo || hasPunchline ? $"\n-\n{plugin.Description}" : plugin.Description;
                    }

                    if (!string.IsNullOrEmpty(tooltip))
                    {
                        ImGui.SetTooltip(tooltip);
                    }

                    if (hasRepo && ImGui.IsMouseReleased(ImGuiMouseButton.Left)
                        && plugin.RepoUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                    {
                        OpenUrl(plugin.RepoUrl);
                    }
                }

                if (++count % 6 == 5)
                {
                    continue;
                }

                ImGui.SameLine();
                ImGui.SetCursorPosX(ImGui.GetCursorPosX() + spacing);
            }

            ImGui.SetWindowFontScale(1);
            ImGui.Unindent(indent);

            ImGui.Spacing();
            ImGui.Separator();
        }

        ImGui.EndChild();
    }

    private static bool RepoMatchesSearch(RepoInfo repo, string searchValue)
    {
        if (string.IsNullOrEmpty(searchValue))
        {
            return false;
        }

        return repo.Url.Contains(searchValue, StringComparison.CurrentCultureIgnoreCase)
               || repo.FullName.Contains(searchValue, StringComparison.CurrentCultureIgnoreCase)
               || repo.Plugins.Any(plugin => PluginMatchesSearch(plugin, searchValue));
    }

    private static bool PluginMatchesSearch(PluginInfo plugin, string searchValue)
    {
        if (plugin.Name.Contains(searchValue, StringComparison.CurrentCultureIgnoreCase))
        {
            return true;
        }

        if (plugin.Punchline.Contains(searchValue, StringComparison.CurrentCultureIgnoreCase))
        {
            return true;
        }

        if (plugin.Description.Contains(searchValue, StringComparison.CurrentCultureIgnoreCase))
        {
            return true;
        }

        if (plugin.Tags.Any(tag => tag.Equals(searchValue, StringComparison.CurrentCultureIgnoreCase)))
        {
            return true;
        }

        return plugin.CategoryTags.Any(tag => tag.Equals(searchValue, StringComparison.CurrentCultureIgnoreCase));
    }

    private static bool AddHeaderIcon(string id, string icon)
    {
        if (ImGui.IsWindowCollapsed())
        {
            return false;
        }

        var scale = ImGuiHelpers.GlobalScale;
        var prevCursorPos = ImGui.GetCursorPos();
        var buttonSize = new Vector2(20 * scale);
        var buttonPos = new Vector2(
            ImGui.GetWindowWidth() - buttonSize.X - 17 * scale - ImGui.GetStyle().FramePadding.X * 2,
            2);
        ImGui.SetCursorPos(buttonPos);
        var drawList = ImGui.GetWindowDrawList();
        drawList.PushClipRectFullScreen();

        var pressed = false;
        ImGui.InvisibleButton(id, buttonSize);
        var itemMin = ImGui.GetItemRectMin();
        var itemMax = ImGui.GetItemRectMax();
        var halfSize = ImGui.GetItemRectSize() / 2;
        var center = itemMin + halfSize;
        if (ImGui.IsWindowHovered() && ImGui.IsMouseHoveringRect(itemMin, itemMax, false))
        {
            ImGui.GetWindowDrawList().AddCircleFilled(
                center,
                halfSize.X,
                ImGui.GetColorU32(ImGui.IsMouseDown(ImGuiMouseButton.Left)
                    ? ImGuiCol.ButtonActive
                    : ImGuiCol.ButtonHovered));
            if (ImGui.IsMouseReleased(ImGuiMouseButton.Left))
            {
                pressed = true;
            }
        }

        ImGui.SetCursorPos(buttonPos);
        ImGui.PushFont(UiBuilder.IconFont);
        drawList.AddText(
            UiBuilder.IconFont,
            ImGui.GetFontSize(),
            itemMin + halfSize - ImGui.CalcTextSize(icon) / 2 + Vector2.One,
            0xFFFFFFFF,
            icon);
        ImGui.PopFont();

        ImGui.PopClipRect();
        ImGui.SetCursorPos(prevCursorPos);

        return pressed;
    }

    private static void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
        }
        catch
        {
        }
    }
}
