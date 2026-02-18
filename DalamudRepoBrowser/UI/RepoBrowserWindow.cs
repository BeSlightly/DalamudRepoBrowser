using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Components;
using Dalamud.Interface.ImGuiNotification;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Windowing;

namespace DalamudRepoBrowser;

internal sealed class RepoBrowserWindow : Window, IDisposable
{
    private const string SourceUrl = "https://beslightly.github.io/Aetherfeed/";


    private readonly RepoManager repoManager;
    private readonly Configuration config;
    private readonly HashSet<string> prevSeenRepos;

    private bool openSettings;
    private bool firstOpen = true;
    private HashSet<RepoInfo> enabledRepos = new();
    private HashSet<RepoInfo> searchResults = new();
    private string searchText = string.Empty;
    private uint filteredCount;
    private DateTimeOffset uiOpenedAt;
    private bool enabledReposInitialized;
    private IReadOnlyList<RepoInfo>? enabledReposSource;
    private DateTimeOffset lastEnabledRefresh = DateTimeOffset.MinValue;
    private const int EnabledRefreshIntervalMs = 1500;
    private IReadOnlyList<RepoInfo>? modernCacheRepos;
    private bool modernCacheValid;
    private bool modernCacheShowOutdated;
    private bool modernCacheHideNonEnglish;
    private bool modernCacheHideClosedSource;
    private int modernCacheApiLevel;
    private DateTimeOffset modernCacheUiOpenedAt;
    private float modernTextScale = -1f;
    private readonly Dictionary<RepoInfo, ModernRepoCache> modernRepoCache = new();
    private readonly Dictionary<PluginInfo, float> pluginTextWidthCache = new();

    private string lastCopiedUrl = string.Empty;
    private DateTime lastCopiedTime = DateTime.MinValue;

    private const string ModernHeaderTitle = "Aetherfeed Browser";
    private const string ModernHeaderSubtitle = "Discover repositories automatically scraped from GitHub";

    public RepoBrowserWindow(RepoManager repoManager, Configuration config)
        : base("Repository Browser")
    {
        this.repoManager = repoManager;
        this.config = config;
        prevSeenRepos = config.SeenRepos.ToHashSet();

        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(830, 570),
            MaximumSize = new Vector2(9999)
        };

        TitleBarButtons.Add(new TitleBarButton
        {
            Icon = FontAwesomeIcon.SyncAlt,
            IconOffset = new Vector2(1, 1),
            Click = _ => repoManager.FetchRepoMasters(),
            ShowTooltip = () => ImGui.SetTooltip("Refresh repositories")
        });
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
        if (ImGui.IsWindowAppearing() || uiOpenedAt == default)
        {
            uiOpenedAt = DateTimeOffset.Now;
        }

        if (repoManager.TryConsumeSortCountdown())
        {
            enabledRepos = repoManager.SortAndUpdateSeen(prevSeenRepos);
            enabledReposInitialized = true;
            enabledReposSource = repoManager.RepoList;
        }

        if (firstOpen)
        {
            repoManager.FetchRepoMasters();
            firstOpen = false;
        }

        var repos = repoManager.RepoList;
        if (enabledReposInitialized && !ReferenceEquals(enabledReposSource, repos))
        {
            enabledReposInitialized = false;
            enabledReposSource = null;
            enabledRepos.Clear();
        }

        if (!ReferenceEquals(modernCacheRepos, repos))
        {
            modernCacheValid = false;
        }

        MaybeRefreshEnabledRepos(repos);

        if (config.UseModernUi)
        {
            DrawModern(repos);
        }
        else
        {
            DrawClassic(repos);
        }
    }

    private void DrawClassic(IReadOnlyList<RepoInfo> repos)
    {
        ImGui.PushFont(UiBuilder.IconFont);

        var settingsPressed = ImGui.Button(FontAwesomeIcon.Wrench.ToIconString());
        var settingsHovered = ImGui.IsItemHovered();

        ImGui.SameLine();

        var sourcePressed = ImGui.Button(FontAwesomeIcon.Globe.ToIconString());
        var sourceHovered = ImGui.IsItemHovered();

        ImGui.PopFont();

        if (settingsPressed)
        {
            openSettings = !openSettings;
        }

        if (settingsHovered)
        {
            ImGui.SetTooltip("Settings");
        }

        if (sourcePressed)
        {
            OpenUrl(SourceUrl);
        }

        if (sourceHovered)
        {
            ImGui.SetTooltip("Source");
        }

        ImGui.SameLine();

        ImGui.TextColored(new Vector4(1, 0, 0, 1), "DO NOT INSTALL FROM REPOSITORIES YOU DO NOT TRUST.");

        var inputWidth = ImGui.GetWindowContentRegionMax().X / 4;
        ImGui.SameLine(inputWidth * 3);
        ImGui.SetNextItemWidth(inputWidth);
        if (ImGui.InputTextWithHint(
                "##Search",
                $"Search {filteredCount} / {repos.Count} Repos",
                ref searchText,
                64))
        {
            UpdateSearchResults(repos);
        }

        if (openSettings)
        {
            DrawClassicSettingsPanel();
        }

        ImGui.Separator();

        ImGui.BeginChild("RepoList");
        var chipBase = 32 * ImGuiHelpers.GlobalScale;
        var spacing = chipBase / 6;
        var padding = chipBase / 8;
        var indent = padding * 2;

        filteredCount = 0;
        foreach (var repoInfo in repos)
        {
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

            var visiblePlugins = repoInfo.Plugins
                .Where(plugin => (config.ShowOutdatedPlugins || IsPluginCurrentOrUnknown(plugin))
                                 && PluginPassesLanguageFilter(plugin)
                                 && PluginPassesClosedSourceFilter(plugin))
                .ToList();
            if (visiblePlugins.Count == 0)
            {
                continue;
            }

            filteredCount++;

            var seen = prevSeenRepos.Contains(repoInfo.Url);

            var isJustCopied = lastCopiedUrl == repoInfo.Url && (DateTime.Now - lastCopiedTime).TotalSeconds < 2;
            var copyButtonText = isJustCopied ? $"Copied!##{repoInfo.Url}" : $"Copy Link##{repoInfo.Url}";
            
            if (isJustCopied) ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.2f, 0.8f, 0.5f, 0.8f));
            if (ImGui.Button(copyButtonText))
            {
                CopyUrl(repoInfo.Url);
            }
            if (isJustCopied) ImGui.PopStyleColor();

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
            foreach (var plugin in visiblePlugins)
            {
                var valid = IsPluginCurrentOrUnknown(plugin);
                var prevCursor = ImGui.GetCursorPos();
                ImGui.Dummy(ImGui.CalcTextSize(plugin.Name));
                var textMin = ImGui.GetItemRectMin();
                var textMax = ImGui.GetItemRectMax();
                textMin.X -= padding;
                textMax.X += padding;
                var drawList = ImGui.GetWindowDrawList();
                var rounding = ImGui.GetStyle().FrameRounding;
                drawList.AddRectFilled(textMin, textMax, valid ? 0x20FFFFFFu : 0x200000FFu, rounding);
                if (plugin.IsClosedSource)
                {
                    drawList.AddRectFilled(textMin, textMax, ImGui.GetColorU32(new Vector4(0.35f, 0.22f, 0.05f, 0.2f)), rounding);
                    drawList.AddRect(
                        textMin,
                        textMax,
                        ImGui.GetColorU32(new Vector4(1f, 0.7f, 0.2f, 0.6f)),
                        rounding,
                        0,
                        1f * ImGuiHelpers.GlobalScale);
                }
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
                    ShowPluginTooltip(plugin);

                    if (!string.IsNullOrEmpty(plugin.RepoUrl)
                        && ImGui.IsMouseReleased(ImGuiMouseButton.Left)
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

    private void DrawClassicSettingsPanel()
    {
        var save = false;

        ImGui.Columns(2, string.Empty, false);

        var useModernUi = config.UseModernUi;
        if (ImGui.Checkbox("Use new UI", ref useModernUi))
        {
            config.UseModernUi = useModernUi;
            save = true;
        }

        ImGui.Spacing();

        var showOutdated = config.ShowOutdatedPlugins;
        if (ImGui.Checkbox("Show Outdated Plugins", ref showOutdated))
        {
            config.ShowOutdatedPlugins = showOutdated;
            save = true;
        }

        ImGui.Spacing();

        var hideNonEnglish = config.HideNonEnglishPlugins;
        if (ImGui.Checkbox("Hide non-English plugins", ref hideNonEnglish))
        {
            config.HideNonEnglishPlugins = hideNonEnglish;
            save = true;
            if (!string.IsNullOrEmpty(searchText))
            {
                UpdateSearchResults(repoManager.RepoList);
            }
        }

        ImGui.Spacing();

        var hideClosedSource = config.HideClosedSourcePlugins;
        if (ImGui.Checkbox("Hide closed-source plugins", ref hideClosedSource))
        {
            config.HideClosedSourcePlugins = hideClosedSource;
            save = true;
            if (!string.IsNullOrEmpty(searchText))
            {
                UpdateSearchResults(repoManager.RepoList);
            }
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
            config.MaxPlugins >= 50 ? "\u221e Plugins###MaxPlugins" : "Maximum Plugins###MaxPlugins",
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

    private void DrawModern(IReadOnlyList<RepoInfo> repos)
    {
        DrawModernHeader();
        DrawModernFilterBar(repos);
        DrawModernWarning();

        if (openSettings)
        {
            DrawModernSettingsPanel();
        }

        ImGui.Spacing();
        DrawModernRepoList(repos);
    }

    private void DrawModernHeader()
    {
        var scale = ImGuiHelpers.GlobalScale;
        var headerHeight = 118f * scale;
        var padding = 18f * scale;

        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(0, 0));
        ImGui.BeginChild(
            "ModernHeader",
            new Vector2(0, headerHeight),
            false,
            ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);

        var windowPos = ImGui.GetWindowPos();
        var windowSize = ImGui.GetWindowSize();
        var drawList = ImGui.GetWindowDrawList();
        DrawModernHeaderAurora(windowPos, windowSize, drawList, padding, scale);

        ImGui.EndChild();
        ImGui.PopStyleVar();
    }

    private void DrawModernHeaderAurora(
        Vector2 windowPos,
        Vector2 windowSize,
        ImDrawListPtr drawList,
        float padding,
        float scale)
    {
        var leftColor = new Vector4(0.012f, 0.024f, 0.05f, 1f);
        var midColor = new Vector4(0.02f, 0.08f, 0.16f, 1f);
        var rightColor = new Vector4(0.024f, 0.45f, 0.65f, 1f);

        drawList.AddRectFilledMultiColor(
            windowPos,
            new Vector2(windowPos.X + windowSize.X, windowPos.Y + windowSize.Y),
            ImGui.GetColorU32(leftColor),
            ImGui.GetColorU32(rightColor),
            ImGui.GetColorU32(new Vector4(rightColor.X * 0.7f, rightColor.Y * 0.7f, rightColor.Z * 0.7f, 1f)),
            ImGui.GetColorU32(midColor));

        var glowMin = windowPos;
        var glowMax = new Vector2(windowPos.X + windowSize.X, windowPos.Y + windowSize.Y * 0.75f);
        drawList.AddRectFilledMultiColor(
            glowMin,
            glowMax,
            ImGui.GetColorU32(new Vector4(0.3f, 0.7f, 0.95f, 0.2f)),
            ImGui.GetColorU32(new Vector4(0.1f, 0.4f, 0.7f, 0.05f)),
            ImGui.GetColorU32(new Vector4(0.1f, 0.4f, 0.7f, 0f)),
            ImGui.GetColorU32(new Vector4(0.1f, 0.4f, 0.7f, 0f)));

        var accentWidth = 4f * scale;
        var accentMin = new Vector2(windowPos.X + padding, windowPos.Y + padding);
        var accentMax = new Vector2(windowPos.X + padding + accentWidth, windowPos.Y + windowSize.Y - padding);
        drawList.AddRectFilled(accentMin, accentMax, ImGui.GetColorU32(new Vector4(0.4f, 0.85f, 0.95f, 0.7f)));

        var shineHeight = 3f * scale;
        drawList.AddRectFilledMultiColor(
            windowPos,
            new Vector2(windowPos.X + windowSize.X, windowPos.Y + shineHeight),
            ImGui.GetColorU32(new Vector4(0.4f, 0.8f, 0.95f, 0.15f)),
            ImGui.GetColorU32(new Vector4(0.4f, 0.8f, 0.95f, 0.3f)),
            ImGui.GetColorU32(new Vector4(0.4f, 0.8f, 0.95f, 0f)),
            ImGui.GetColorU32(new Vector4(0.4f, 0.8f, 0.95f, 0f)));

        drawList.AddLine(
            new Vector2(windowPos.X, windowPos.Y + windowSize.Y - 1),
            new Vector2(windowPos.X + windowSize.X, windowPos.Y + windowSize.Y - 1),
            ImGui.GetColorU32(new Vector4(0.4f, 0.85f, 0.95f, 0.25f)),
            2f * scale);

        var separatorHeight = 6f * scale;
        var separatorMin = new Vector2(windowPos.X, windowPos.Y + windowSize.Y - separatorHeight);
        var separatorMax = new Vector2(windowPos.X + windowSize.X, windowPos.Y + windowSize.Y);
        drawList.AddRectFilledMultiColor(
            separatorMin,
            separatorMax,
            ImGui.GetColorU32(new Vector4(0f, 0f, 0f, 0.35f)),
            ImGui.GetColorU32(new Vector4(0f, 0f, 0f, 0.35f)),
            ImGui.GetColorU32(new Vector4(0f, 0f, 0f, 0f)),
            ImGui.GetColorU32(new Vector4(0f, 0f, 0f, 0f)));

        var textX = padding + accentWidth + (10f * scale);
        var textY = padding + (4f * scale);
        ImGui.SetCursorPos(new Vector2(textX, textY));
        ImGui.SetWindowFontScale(1.45f);
        var titleHeight = ImGui.GetTextLineHeight();
        ImGui.TextColored(new Vector4(1f, 1f, 1f, 1f), ModernHeaderTitle);

        ImGui.SetWindowFontScale(0.95f);
        var subtitleY = textY + titleHeight + (8f * scale);
        ImGui.SetCursorPos(new Vector2(textX, subtitleY));
        var subtitleHeight = ImGui.GetTextLineHeight();
        ImGui.TextColored(new Vector4(0.65f, 0.88f, 0.98f, 0.9f), ModernHeaderSubtitle);

        ImGui.SetWindowFontScale(0.9f);
        ImGui.SetCursorPos(new Vector2(textX, subtitleY + subtitleHeight + (6f * scale)));
        ImGui.TextColored(new Vector4(0.55f, 0.78f, 0.9f, 0.75f), GetRemoteUpdateStatusText());
        ImGui.SetWindowFontScale(1f);

        DrawModernHeaderButtons(
            windowSize,
            padding,
            scale,
            new Vector4(0.1f, 0.2f, 0.35f, 0.5f),
            new Vector4(0.15f, 0.5f, 0.7f, 0.7f),
            new Vector4(0.1f, 0.6f, 0.8f, 0.9f),
            12f * scale);
    }


    private void DrawModernHeaderButtons(
        Vector2 windowSize,
        float padding,
        float scale,
        Vector4 baseColor,
        Vector4 hoverColor,
        Vector4 activeColor,
        float rounding)
    {
        var buttonSize = new Vector2(34f * scale, 34f * scale);
        var buttonGap = 8f * scale;
        var rightGroupWidth = (buttonSize.X * 2) + buttonGap;
        ImGui.SetCursorPos(new Vector2(windowSize.X - rightGroupWidth - padding, padding));

        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, rounding);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameBorderSize, 1f * scale);
        ImGui.PushStyleColor(ImGuiCol.Button, baseColor);
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, hoverColor);
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, activeColor);
        ImGui.PushStyleColor(ImGuiCol.Border, new Vector4(0.3f, 0.55f, 0.8f, 0.45f));

        ImGui.PushFont(UiBuilder.IconFont);
        var settingsPressed = ImGui.Button($"{FontAwesomeIcon.Wrench.ToIconString()}##ModernSettings", buttonSize);
        var settingsHovered = ImGui.IsItemHovered();

        ImGui.SameLine(0, buttonGap);
        var sourcePressed = ImGui.Button($"{FontAwesomeIcon.Globe.ToIconString()}##ModernSource", buttonSize);
        var sourceHovered = ImGui.IsItemHovered();
        ImGui.PopFont();

        if (settingsPressed)
        {
            openSettings = !openSettings;
        }
        if (settingsHovered)
        {
            ImGui.SetTooltip("Settings");
        }

        if (sourcePressed)
        {
            OpenUrl(SourceUrl);
        }
        if (sourceHovered)
        {
            ImGui.SetTooltip("View on Aetherfeed");
        }

        ImGui.PopStyleColor(4);
        ImGui.PopStyleVar(2);
    }

    private void DrawModernFilterBar(IReadOnlyList<RepoInfo> repos)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var barHeight = 52f * scale; // Slightly taller
        var padding = 12f * scale;

        ImGui.PushStyleVar(ImGuiStyleVar.ChildRounding, 12f * scale);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(padding, padding / 1.5f));
        // Glassy dark blue background
        ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(0.05f, 0.08f, 0.14f, 0.6f));
        // Add a subtle border
        ImGui.PushStyleColor(ImGuiCol.Border, new Vector4(0.2f, 0.3f, 0.5f, 0.3f));
        ImGui.PushStyleVar(ImGuiStyleVar.ChildBorderSize, 1f * scale);

        ImGui.BeginChild(
            "ModernFilterBar",
            new Vector2(0, barHeight),
            true, // Enable border
            ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);

        var inputWidth = ImGui.GetContentRegionAvail().X * 0.7f;
        
        // Style the input text to blend better
        ImGui.PushStyleColor(ImGuiCol.FrameBg, new Vector4(0.03f, 0.05f, 0.1f, 0.8f));
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 8f * scale);
        
        ImGui.SetNextItemWidth(inputWidth);
        if (ImGui.InputTextWithHint(
                "##SearchModern",
                $"Search {filteredCount} / {repos.Count} Repos...",
                ref searchText,
                64))
        {
            UpdateSearchResults(repos);
        }

        var inputHeight = ImGui.GetFrameHeight();
        ImGui.PopStyleVar(); // FrameRounding
        ImGui.PopStyleColor(); // FrameBg

        var statusText = $"{filteredCount} repositories shown";
        var statusSize = ImGui.CalcTextSize(statusText);
        var statusX = ImGui.GetWindowContentRegionMax().X - statusSize.X - padding;
        ImGui.SameLine(statusX);
        var statusY = ImGui.GetCursorPosY() + (inputHeight - statusSize.Y) * 0.5f;
        ImGui.SetCursorPosY(statusY);

        // Aether blue text for the counter
        ImGui.TextColored(new Vector4(0.4f, 0.7f, 0.9f, 0.8f), statusText);

        ImGui.EndChild();
        ImGui.PopStyleVar(3); // ChildRounding, WindowPadding, ChildBorderSize
        ImGui.PopStyleColor(2); // ChildBg, Border
    }


    private void DrawModernWarning()
    {
        if (config.DismissedModernWarning)
            return;

        var scale = ImGuiHelpers.GlobalScale;
        var padding = 10f * scale;
        
        var disclaimerText = "These plugin lists are dynamically generated and not vetted. They may contain malware or jeopardize your account. Proceed with caution. If you're installing plugins from custom repositories, DO NOT ask for help on the XIVLauncher discord. Please review the plugin's README for support.";
        
        // Fixed safe height
        var bannerHeight = 85f * scale;

        ImGui.PushStyleVar(ImGuiStyleVar.ChildRounding, 10f * scale);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(padding, padding));
        // Amber/Orange tinted background
        ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(0.25f, 0.14f, 0.02f, 0.4f)); 
        ImGui.PushStyleColor(ImGuiCol.Border, new Vector4(0.5f, 0.35f, 0.1f, 0.3f));
        ImGui.PushStyleVar(ImGuiStyleVar.ChildBorderSize, 1f * scale);

        ImGui.BeginChild(
            "ModernWarning",
            new Vector2(0, bannerHeight),
            true,
            ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);

        ImGui.PushFont(UiBuilder.IconFont);
        ImGui.TextColored(new Vector4(1f, 0.7f, 0.2f, 1f), FontAwesomeIcon.ExclamationTriangle.ToIconString());
        ImGui.PopFont();
        ImGui.SameLine();

        var width = ImGui.GetContentRegionAvail().X - (30f * scale);
        ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + width);
        ImGui.TextColored(new Vector4(1f, 0.85f, 0.6f, 0.9f), disclaimerText);
        ImGui.PopTextWrapPos();

        // Dismiss button
        ImGui.SetCursorPos(new Vector2(ImGui.GetWindowContentRegionMax().X - (24f * scale), padding));
        ImGui.PushStyleColor(ImGuiCol.Button, Vector4.Zero);
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(1f, 1f, 1f, 0.1f));
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(1f, 1f, 1f, 0.2f));
        ImGui.PushFont(UiBuilder.IconFont);
        if (ImGui.Button($"{FontAwesomeIcon.Times.ToIconString()}##DismissWarning", new Vector2(20f * scale, 20f * scale)))
        {
            config.DismissedModernWarning = true;
            config.Save();
        }
        ImGui.PopFont();
        ImGui.PopStyleColor(3);

        ImGui.EndChild();
        ImGui.PopStyleVar(3);
        ImGui.PopStyleColor(2);
    }

    private void DrawModernSettingsPanel()
    {
        var save = false;
        var scale = ImGuiHelpers.GlobalScale;
        var settingsHeight = 250f * scale; // Slightly taller for breathing room

        ImGui.PushStyleVar(ImGuiStyleVar.ChildRounding, 12f * scale);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(16f * scale, 14f * scale));
        // Deep void background
        ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(0.04f, 0.06f, 0.1f, 0.95f));
        ImGui.PushStyleColor(ImGuiCol.Border, new Vector4(0.2f, 0.3f, 0.5f, 0.3f));
        ImGui.PushStyleVar(ImGuiStyleVar.ChildBorderSize, 1f * scale);

        ImGui.BeginChild("ModernSettingsPanel", new Vector2(0, settingsHeight), true);

        ImGui.PushFont(UiBuilder.IconFont);
        ImGui.TextColored(new Vector4(0.4f, 0.7f, 0.9f, 1f), FontAwesomeIcon.Cog.ToIconString());
        ImGui.PopFont();
        ImGui.SameLine();
        ImGui.TextColored(new Vector4(0.9f, 0.9f, 0.9f, 1f), "Configuration");
        
        ImGui.SameLine();
        var width = ImGui.GetContentRegionAvail().X;
        ImGui.Dummy(new Vector2(width - (20 * scale), 0)); // Spacer
        ImGui.SameLine();
        ImGui.PushStyleColor(ImGuiCol.Button, Vector4.Zero);
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(1f, 1f, 1f, 0.1f));
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(1f, 1f, 1f, 0.2f));
        ImGui.PushFont(UiBuilder.IconFont);
        var closePressed = ImGui.Button($"{FontAwesomeIcon.Times.ToIconString()}##CloseSettings", new Vector2(20f * scale, 20f * scale));
        var closeHovered = ImGui.IsItemHovered();
        ImGui.PopFont();
        ImGui.PopStyleColor(3);
        if (closePressed)
        {
            openSettings = false;
        }
        if (closeHovered)
        {
            ImGui.SetTooltip("Close");
        }

        ImGui.Separator();
        ImGui.Spacing();

        if (ImGui.BeginTable("ModernSettingsOptions", 2, ImGuiTableFlags.SizingStretchSame))
        {
            ImGui.TableSetupColumn("Left", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn("Right", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableNextRow();

            ImGui.TableSetColumnIndex(0);
            var useModernUi = config.UseModernUi;
            if (ImGui.Checkbox("Use new UI", ref useModernUi))
            {
                config.UseModernUi = useModernUi;
                save = true;
            }

            var showOutdated = config.ShowOutdatedPlugins;
            if (ImGui.Checkbox("Show Outdated Plugins", ref showOutdated))
            {
                config.ShowOutdatedPlugins = showOutdated;
                save = true;
            }

            var hideNonEnglish = config.HideNonEnglishPlugins;
            if (ImGui.Checkbox("Hide non-English plugins", ref hideNonEnglish))
            {
                config.HideNonEnglishPlugins = hideNonEnglish;
                save = true;
                if (!string.IsNullOrEmpty(searchText))
                {
                    UpdateSearchResults(repoManager.RepoList);
                }
            }

            ImGui.TableSetColumnIndex(1);
            var hideClosedSource = config.HideClosedSourcePlugins;
            if (ImGui.Checkbox("Hide closed-source plugins", ref hideClosedSource))
            {
                config.HideClosedSourcePlugins = hideClosedSource;
                save = true;
                if (!string.IsNullOrEmpty(searchText))
                {
                    UpdateSearchResults(repoManager.RepoList);
                }
            }

            var hideEnabled = config.HideEnabledRepos;
            if (ImGui.Checkbox("Hide Enabled Repos", ref hideEnabled))
            {
                config.HideEnabledRepos = hideEnabled;
                save = true;
            }

            ImGui.EndTable();
        }

        ImGui.Spacing();
        ImGui.SetNextItemWidth(ImGui.GetWindowWidth() / 3f);
        var maxPlugins = config.MaxPlugins;
        if (ImGui.SliderInt(
            config.MaxPlugins >= 50 ? "\u221e Plugins###MaxPlugins" : "Maximum Plugins###MaxPlugins",
            ref maxPlugins,
            20,
            50))
        {
            config.MaxPlugins = maxPlugins;
            save = true;
        }

        ImGui.Separator();
        ImGui.Spacing();

        ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.7f, 1f), "Sort By");

        var repoSort = config.RepoSort;
        ImGui.SetNextItemWidth(200f * scale);
        if (ImGui.BeginCombo("##RepoSort", GetSortLabel(repoSort)))
        {
            if (ImGui.Selectable("Owner", repoSort == 1)) repoSort = 1;
            if (ImGui.Selectable("URL", repoSort == 2)) repoSort = 2;
            if (ImGui.Selectable("# Plugins", repoSort == 3)) repoSort = 3;
            if (ImGui.Selectable("Last Updated", repoSort == 4)) repoSort = 4;
            ImGui.EndCombo();
        }

        if (repoSort != config.RepoSort)
        {
            config.RepoSort = repoSort;
            save = true;
        }

        ImGui.EndChild();
        ImGui.PopStyleColor(2);
        ImGui.PopStyleVar(3);

        if (save)
        {
            repoManager.RequestSort();
            config.Save();
        }
    }

    private void DrawModernRepoList(IReadOnlyList<RepoInfo> repos)
    {
        EnsureModernCache(repos);

        var scale = ImGuiHelpers.GlobalScale;
        var padding = 16f * scale;
        var sectionSpacing = 6f * scale;
        var rowSpacing = 5f * scale;
        var chipPadding = new Vector2(7f * scale, 3f * scale);
        var rounding = 16f * scale;
        var chipRounding = 4f * scale;
        var borderThickness = 1f * scale;
        var cardSpacing = 12f * scale;
        var headerSpacingExtra = 2f * scale;
        var chipsBottomPadding = 6f * scale;
        var accentWidth = 6f * scale;
        var accentGap = 8f * scale;

        var textColor = ImGui.GetStyle().Colors[(int)ImGuiCol.Text];
        var mutedText = new Vector4(textColor.X, textColor.Y, textColor.Z, 0.6f);

        // Aetherfeed card style: Deep void background with subtle transparency
        var cardBg = new Vector4(0.06f, 0.08f, 0.12f, 0.9f);
        // Subtle border
        var cardBorder = new Vector4(0.2f, 0.35f, 0.6f, 0.25f);

        ImGui.BeginChild("RepoListModern");

        var listWidth = ImGui.GetContentRegionAvail().X;

        if (modernTextScale != scale)
        {
            pluginTextWidthCache.Clear();
            modernTextScale = scale;
        }

        filteredCount = 0;
        var entries = new List<ModernVisibleRepoEntry>(repos.Count);
        var startOffsets = new List<float>(repos.Count);
        var endOffsets = new List<float>(repos.Count);
        var runningY = 0f;

        foreach (var repoInfo in repos)
        {
            if (config.MaxPlugins < 50 && config.MaxPlugins < repoInfo.Plugins.Count)
            {
                continue;
            }

            if (!string.IsNullOrEmpty(searchText) && !searchResults.Contains(repoInfo))
            {
                continue;
            }

            var enabled = enabledReposInitialized
                ? enabledRepos.Contains(repoInfo)
                : repoManager.GetRepoEnabled(repoInfo.Url) || repoManager.GetRepoEnabled(repoInfo.RawUrl);
            if (enabled && config.HideEnabledRepos)
            {
                continue;
            }

            if (!modernRepoCache.TryGetValue(repoInfo, out var cache) || cache.Plugins.Count == 0)
            {
                continue;
            }

            filteredCount++;

            var seen = prevSeenRepos.Contains(repoInfo.Url);
            var isPriority = config.PriorityRepos.Contains(repoInfo.Url);
            var hasAccent = enabled || !seen;
            var accentOffset = hasAccent ? (accentWidth + accentGap) : 0f;

            var availWidth = listWidth - (padding * 2) - accentOffset;
            if (availWidth < 1f)
            {
                availWidth = 1f;
            }

            UpdateRepoLayout(cache, availWidth, scale, padding, sectionSpacing, headerSpacingExtra, chipsBottomPadding, rowSpacing, chipPadding, enabled);

            entries.Add(new ModernVisibleRepoEntry(repoInfo, cache, enabled, seen, isPriority, accentOffset, cache.CardHeight));
            startOffsets.Add(runningY);
            runningY += cache.CardHeight + cardSpacing;
            endOffsets.Add(runningY);
        }

        if (entries.Count == 0)
        {
            ImGui.EndChild();
            return;
        }

        var scrollY = ImGui.GetScrollY();
        var viewHeight = ImGui.GetWindowHeight();
        var overscan = 200f * scale;
        var viewTop = MathF.Max(0f, scrollY - overscan);
        var viewBottom = scrollY + viewHeight + overscan;

        var startIndex = LowerBound(endOffsets, viewTop + 0.001f);
        if (startIndex >= entries.Count)
        {
            ImGui.Dummy(new Vector2(0, runningY));
            ImGui.EndChild();
            return;
        }

        var endExclusive = LowerBound(startOffsets, viewBottom);
        var endIndex = Math.Max(startIndex, endExclusive - 1);
        if (endIndex >= entries.Count)
        {
            endIndex = entries.Count - 1;
        }

        var topPadding = startOffsets[startIndex];
        if (topPadding > 0f)
        {
            ImGui.Dummy(new Vector2(0, topPadding));
        }

        for (var index = startIndex; index <= endIndex; index++)
        {
            var entry = entries[index];
            var repoInfo = entry.Repo;
            var cache = entry.Cache;
            var enabled = entry.Enabled;
            var seen = entry.Seen;
            var isPriority = entry.IsPriority;
            var accentOffset = entry.AccentOffset;
            var hasAccent = accentOffset > 0f;

            var availWidth = listWidth - (padding * 2) - accentOffset;
            if (availWidth < 1f)
            {
                availWidth = 1f;
            }

            var cardStart = ImGui.GetCursorScreenPos();
            var cardSize = new Vector2(listWidth, entry.CardHeight);
            var drawList = ImGui.GetWindowDrawList();

            // Detect Hover for interaction
            var isHovered = ImGui.IsMouseHoveringRect(cardStart, cardStart + cardSize);
            
            // Refined Card Background & Border with hover interaction
            var currentBg = isHovered ? new Vector4(0.08f, 0.11f, 0.16f, 0.95f) : cardBg;
            var currentBorder = isHovered ? new Vector4(0.3f, 0.5f, 0.8f, 0.5f) : cardBorder;
            var currentBorderThickness = isHovered ? 1.5f * scale : borderThickness;

            if (isHovered)
            {
                var shadowOffset = new Vector2(0f, 1.5f * scale);
                drawList.AddRectFilled(
                    cardStart + shadowOffset,
                    cardStart + cardSize + shadowOffset,
                    ImGui.GetColorU32(new Vector4(0f, 0f, 0f, 0.22f)),
                    rounding);
            }

            // Card Background
            drawList.AddRectFilled(cardStart, cardStart + cardSize, ImGui.GetColorU32(currentBg), rounding);

            // Left vertical accent strip (clip a rounded rect to match card corners)
            var accentColor = enabled
                ? new Vector4(0.2f, 0.8f, 0.5f, 0.8f) // Emerald for enabled
                : (!seen ? new Vector4(0.06f, 0.7f, 1f, 0.8f) : new Vector4(0f, 0f, 0f, 0f)); // Cyan for new

            if (hasAccent)
            {
                var accentClipMin = cardStart;
                var accentClipMax = new Vector2(cardStart.X + accentWidth, cardStart.Y + cardSize.Y);
                drawList.PushClipRect(accentClipMin, accentClipMax, true);
                drawList.AddRectFilled(
                    cardStart,
                    cardStart + cardSize,
                    ImGui.GetColorU32(accentColor),
                    rounding,
                    ImDrawFlags.RoundCornersLeft);
                drawList.PopClipRect();
            }

            // Card Border (draw last so it stays crisp)
            drawList.AddRect(cardStart, cardStart + cardSize, ImGui.GetColorU32(currentBorder), rounding, 0, currentBorderThickness);

            ImGui.Dummy(cardSize);

            var contentPaddingX = padding + accentOffset;
            ImGui.SetCursorScreenPos(new Vector2(cardStart.X + contentPaddingX, cardStart.Y + padding));
            ImGui.PushID(repoInfo.Url);
            ImGui.Indent(contentPaddingX);

            // Using columns for layout
            if (ImGui.BeginTable("Header", 2, ImGuiTableFlags.SizingStretchProp))
            {
                ImGui.TableSetupColumn("Main", ImGuiTableColumnFlags.WidthStretch);
                ImGui.TableSetupColumn("Actions", ImGuiTableColumnFlags.WidthFixed, 200f * scale);

                ImGui.TableNextRow();
                ImGui.TableSetColumnIndex(0);

                var repoName = string.IsNullOrEmpty(repoInfo.FullName) ? repoInfo.Url : repoInfo.FullName;
                
                // Title Styling
                ImGui.SetWindowFontScale(1.15f);
                ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1f, 1f, 1f, 0.95f));
                ImGui.TextUnformatted(repoName);
                ImGui.PopStyleColor();
                ImGui.SetWindowFontScale(1f);

                if (!seen)
                {
                    ImGui.SameLine();
                    // Vibrant NEW badge: Cyan background, dark text
                    DrawBadge("NEW", new Vector4(0.06f, 0.7f, 1f, 0.2f), new Vector4(0.4f, 0.9f, 1f, 1f), scale);
                }

                if (isPriority)
                {
                    ImGui.SameLine();
                    // Vibrant PRIORITY badge: Violet background, light text
                    DrawBadge("PRIORITY", new Vector4(0.5f, 0.3f, 0.9f, 0.25f), new Vector4(0.8f, 0.6f, 1f, 1f), scale);
                    if (ImGui.IsItemHovered())
                    {
                        ImGui.SetTooltip("Priority repos are hand-picked by Aetherfeed for deduplication.\nThese are usually trusted devs, but third-party plugins always carry risk.");
                    }
                }

                ImGui.TableSetColumnIndex(1);

                var toggleWidth = 45f * scale; // Approximate width of toggle
                var actionButtonSize = 32f * scale;
                var actionButton = new Vector2(actionButtonSize, 0f);
                var totalActionsWidth = toggleWidth + (actionButtonSize * 2) + (ImGui.GetStyle().ItemSpacing.X * 2);
                
                // Right align within the column
                var columnWidth = ImGui.GetColumnWidth();
                if (columnWidth > totalActionsWidth)
                {
                    ImGui.SetCursorPosX(ImGui.GetCursorPosX() + (columnWidth - totalActionsWidth));
                }

                if (DrawCustomToggle("##Enabled", ref enabled, scale))
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
                
                if (ImGui.IsItemHovered())
                {
                    ImGui.SetTooltip(enabled ? "Disable Repository" : "Enable Repository");
                }

                ImGui.SameLine();
                ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 8f * scale);
                ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(1f, 1f, 1f, 0.05f));
                ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(1f, 1f, 1f, 0.1f));
                
                var isJustCopied = lastCopiedUrl == repoInfo.Url && (DateTime.Now - lastCopiedTime).TotalSeconds < 2;
                var copyIcon = isJustCopied ? FontAwesomeIcon.Check : FontAwesomeIcon.Copy;
                
                ImGui.PushFont(UiBuilder.IconFont);
                if (isJustCopied) ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.4f, 1f, 0.7f, 1f));
                var copyPressed = ImGui.Button($"{copyIcon.ToIconString()}##Copy", actionButton);
                var copyHovered = ImGui.IsItemHovered();
                if (isJustCopied) ImGui.PopStyleColor();

                var openPressed = false;
                var openHovered = false;
                if (!string.IsNullOrEmpty(repoInfo.GitRepoUrl))
                {
                    ImGui.SameLine();
                    openPressed = ImGui.Button($"{FontAwesomeIcon.Globe.ToIconString()}##Open", actionButton);
                    openHovered = ImGui.IsItemHovered();
                }
                else
                {
                    // Placeholder dummy to keep alignment consistent if globe is missing
                    ImGui.SameLine();
                    ImGui.Dummy(actionButton);
                }

                ImGui.PopFont();

                if (copyPressed)
                {
                    CopyUrl(repoInfo.Url);
                }
                if (copyHovered)
                {
                    ImGui.SetTooltip("Copy Repo URL");
                }

                if (openPressed)
                {
                    OpenUrl(repoInfo.GitRepoUrl);
                }
                if (openHovered)
                {
                    ImGui.SetTooltip("Open Website");
                }
                ImGui.PopStyleColor(2);
                ImGui.PopStyleVar();

                ImGui.EndTable();
                ImGui.Dummy(new Vector2(0, sectionSpacing + headerSpacingExtra));
            }

            ImGui.TextColored(mutedText, cache.InfoText);
            ImGui.Dummy(new Vector2(0, sectionSpacing));

            // Draw Chips with proper wrapping
            var chipAvailWidth = availWidth;
            var chipCurrentX = 0f;
            var isFirstChip = true;

            foreach (var pluginEntry in cache.Plugins)
            {
                var plugin = pluginEntry.Plugin;
                var textWidth = GetPluginTextWidth(plugin);
                var chipWidth = textWidth + (chipPadding.X * 2);
                
                if (!isFirstChip)
                {
                    if (chipCurrentX + rowSpacing + chipWidth > chipAvailWidth)
                    {
                        // New line
                        ImGui.Dummy(new Vector2(0, rowSpacing)); // vertical spacing
                        chipCurrentX = 0;
                    }
                    else
                    {
                        ImGui.SameLine(0f, rowSpacing);
                        chipCurrentX += rowSpacing;
                    }
                }

                DrawPluginChip(plugin, pluginEntry.IsValid, textWidth, chipPadding, scale, chipRounding);
                chipCurrentX += chipWidth;
                isFirstChip = false;
            }

            ImGui.Dummy(new Vector2(0, chipsBottomPadding));

            ImGui.Unindent(contentPaddingX);
            ImGui.PopID();

            ImGui.SetCursorScreenPos(new Vector2(cardStart.X, cardStart.Y + cardSize.Y + cardSpacing));
        }

        var bottomPadding = runningY - endOffsets[endIndex];
        if (bottomPadding > 0f)
        {
            ImGui.Dummy(new Vector2(0, bottomPadding));
        }

        ImGui.EndChild();
    }

    private void DrawPluginChip(
        PluginInfo plugin,
        bool valid,
        float textWidth,
        Vector2 padding,
        float scale,
        float chipRounding)
    {
        var drawList = ImGui.GetWindowDrawList();
        var textHeight = ImGui.GetTextLineHeight();
        var chipSize = new Vector2(textWidth + (padding.X * 2), textHeight + (padding.Y * 2));

        ImGui.Dummy(chipSize);
        var rectMin = ImGui.GetItemRectMin();
        var rectMax = ImGui.GetItemRectMax();
        
        var hovered = ImGui.IsItemHovered();

        // Chip Background
        // Default: Dark transparent
        // Hover: Brighter blueish
        var fillColor = valid
            ? (hovered ? new Vector4(0.2f, 0.4f, 0.6f, 0.32f) : new Vector4(1f, 1f, 1f, 0.03f))
            : new Vector4(0.75f, 0.22f, 0.22f, 0.12f);

        drawList.AddRectFilled(rectMin, rectMax, ImGui.GetColorU32(fillColor), chipRounding);
        
        // Chip Border
        var borderColor = valid 
            ? (hovered ? new Vector4(0.4f, 0.7f, 0.9f, 0.5f) : new Vector4(1f, 1f, 1f, 0.1f))
            : new Vector4(0.75f, 0.22f, 0.22f, 0.3f);
            
        drawList.AddRect(rectMin, rectMax, ImGui.GetColorU32(borderColor), chipRounding);

        if (plugin.IsClosedSource)
        {
            var closedFill = hovered
                ? new Vector4(0.35f, 0.22f, 0.05f, 0.35f)
                : new Vector4(0.3f, 0.18f, 0.05f, 0.2f);
            var closedBorder = hovered
                ? new Vector4(1f, 0.75f, 0.2f, 0.9f)
                : new Vector4(1f, 0.7f, 0.2f, 0.7f);
            drawList.AddRectFilled(rectMin, rectMax, ImGui.GetColorU32(closedFill), chipRounding);
            drawList.AddRect(rectMin, rectMax, ImGui.GetColorU32(closedBorder), chipRounding, 0, 1.3f * scale);
        }

        if (!valid)
        {
            var strikeColor = ImGui.GetColorU32(new Vector4(0.9f, 0.3f, 0.3f, 0.6f));
            var thickness = 2f * scale;
            drawList.AddLine(rectMin, rectMax, strikeColor, thickness);
        }

        var textColor = valid 
            ? (hovered ? new Vector4(1f, 1f, 1f, 1f) : new Vector4(0.9f, 0.9f, 0.95f, 0.8f))
            : new Vector4(1f, 0.6f, 0.6f, 0.7f);
        var textPos = new Vector2(rectMin.X + padding.X, rectMin.Y + padding.Y);
        drawList.AddText(ImGui.GetFont(), ImGui.GetFontSize(), textPos, ImGui.GetColorU32(textColor), plugin.Name);

        if (hovered)
        {
            ShowPluginTooltip(plugin);

            if (!string.IsNullOrEmpty(plugin.RepoUrl)
                && ImGui.IsMouseReleased(ImGuiMouseButton.Left)
                && plugin.RepoUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                OpenUrl(plugin.RepoUrl);
            }
        }
    }

    private void EnsureModernCache(IReadOnlyList<RepoInfo> repos)
    {
        var apiLevel = repoManager.CurrentApiLevel;
        if (modernCacheValid
            && ReferenceEquals(modernCacheRepos, repos)
            && modernCacheShowOutdated == config.ShowOutdatedPlugins
            && modernCacheHideNonEnglish == config.HideNonEnglishPlugins
            && modernCacheHideClosedSource == config.HideClosedSourcePlugins
            && modernCacheApiLevel == apiLevel
            && modernCacheUiOpenedAt == uiOpenedAt)
        {
            return;
        }

        RebuildModernCache(repos);

        modernCacheValid = true;
        modernCacheRepos = repos;
        modernCacheShowOutdated = config.ShowOutdatedPlugins;
        modernCacheHideNonEnglish = config.HideNonEnglishPlugins;
        modernCacheHideClosedSource = config.HideClosedSourcePlugins;
        modernCacheApiLevel = apiLevel;
        modernCacheUiOpenedAt = uiOpenedAt;
    }

    private void MaybeRefreshEnabledRepos(IReadOnlyList<RepoInfo> repos)
    {
        if (repos.Count == 0)
        {
            return;
        }

        var now = DateTimeOffset.Now;
        if ((now - lastEnabledRefresh).TotalMilliseconds < EnabledRefreshIntervalMs)
        {
            return;
        }

        enabledRepos.Clear();
        foreach (var repo in repos)
        {
            if (repoManager.GetRepoEnabled(repo.Url) || repoManager.GetRepoEnabled(repo.RawUrl))
            {
                enabledRepos.Add(repo);
            }
        }

        enabledReposInitialized = true;
        enabledReposSource = repos;
        lastEnabledRefresh = now;
    }

    private void RebuildModernCache(IReadOnlyList<RepoInfo> repos)
    {
        modernRepoCache.Clear();

        foreach (var repo in repos)
        {
            var visiblePlugins = new List<ModernPluginEntry>(repo.Plugins.Count);
            foreach (var plugin in repo.Plugins)
            {
                var valid = IsPluginCurrentOrUnknown(plugin);
                if (!config.ShowOutdatedPlugins && !valid)
                {
                    continue;
                }

                if (!PluginPassesLanguageFilter(plugin))
                {
                    continue;
                }

                if (!PluginPassesClosedSourceFilter(plugin))
                {
                    continue;
                }

                visiblePlugins.Add(new ModernPluginEntry(plugin, valid));
            }

            var cache = new ModernRepoCache(repo, visiblePlugins, BuildRepoInfoText(repo));
            modernRepoCache[repo] = cache;
        }
    }

    private void UpdateRepoLayout(
        ModernRepoCache cache,
        float availWidth,
        float scale,
        float padding,
        float sectionSpacing,
        float headerSpacingExtra,
        float chipsBottomPadding,
        float rowSpacing,
        Vector2 chipPadding,
        bool enabled)
    {
        if (cache.LayoutScale == scale
            && Math.Abs(cache.LayoutAvailWidth - availWidth) < 0.1f
            && cache.LayoutEnabled == enabled)
        {
            return;
        }

        cache.LayoutScale = scale;
        cache.LayoutAvailWidth = availWidth;
        cache.LayoutEnabled = enabled;

        var lineHeight = ImGui.GetTextLineHeightWithSpacing();
        var singleChipHeight = ImGui.GetTextLineHeight() + (chipPadding.Y * 2);

        var chipsHeight = 0f;
        if (cache.Plugins.Count > 0)
        {
            var currentX = 0f;
            var rows = 1;
            var isFirst = true;

            foreach (var entry in cache.Plugins)
            {
                var chipWidth = GetPluginTextWidth(entry.Plugin) + (chipPadding.X * 2);
                if (!isFirst)
                {
                    currentX += rowSpacing;
                }

                if (currentX + chipWidth > availWidth)
                {
                    rows++;
                    currentX = 0f;
                }

                currentX += chipWidth;
                isFirst = false;
            }

            chipsHeight = (rows * singleChipHeight) + ((rows - 1) * rowSpacing);
        }

        cache.ChipsHeight = chipsHeight;
        cache.CardHeight = (padding * 2)
                           + (lineHeight * 2.2f)
                           + (sectionSpacing * 2)
                           + headerSpacingExtra
                           + chipsBottomPadding
                           + chipsHeight;
    }

    private float GetPluginTextWidth(PluginInfo plugin)
    {
        if (pluginTextWidthCache.TryGetValue(plugin, out var width))
        {
            return width;
        }

        width = ImGui.CalcTextSize(plugin.Name).X;
        pluginTextWidthCache[plugin] = width;
        return width;
    }

    private string BuildRepoInfoText(RepoInfo repoInfo)
    {
        var infoParts = new List<string>(4);
        if (!string.IsNullOrEmpty(repoInfo.Owner))
        {
            infoParts.Add($"Owner: {repoInfo.Owner}");
        }

        infoParts.Add($"Plugins: {repoInfo.Plugins.Count}");

        if (!repoInfo.IsDefaultBranch && !string.IsNullOrEmpty(repoInfo.BranchName))
        {
            infoParts.Add($"Branch: {repoInfo.BranchName}");
        }

        DateTimeOffset? lastUpdated = repoInfo.LastUpdated > 0
            ? DateTimeOffset.FromUnixTimeSeconds(repoInfo.LastUpdated).ToLocalTime()
            : null;
        if (lastUpdated.HasValue)
        {
            var relative = GetRelativeTimeText(lastUpdated.Value, uiOpenedAt);
            infoParts.Add($"Updated: {relative} ({lastUpdated.Value:MMM dd, yyyy HH:mm})");
        }
        else
        {
            infoParts.Add("Updated: Unknown");
        }

        return string.Join("  •  ", infoParts);
    }

    private readonly struct ModernPluginEntry
    {
        public ModernPluginEntry(PluginInfo plugin, bool isValid)
        {
            Plugin = plugin;
            IsValid = isValid;
        }

        public PluginInfo Plugin { get; }
        public bool IsValid { get; }
    }

    private sealed class ModernRepoCache
    {
        public ModernRepoCache(RepoInfo repo, List<ModernPluginEntry> plugins, string infoText)
        {
            Repo = repo;
            Plugins = plugins;
            InfoText = infoText;
        }

        public RepoInfo Repo { get; }
        public List<ModernPluginEntry> Plugins { get; }
        public string InfoText { get; set; }
        public float LayoutScale { get; set; } = -1f;
        public float LayoutAvailWidth { get; set; } = -1f;
        public bool LayoutEnabled { get; set; }
        public float CardHeight { get; set; }
        public float ChipsHeight { get; set; }
    }

    private readonly struct ModernVisibleRepoEntry
    {
        public ModernVisibleRepoEntry(
            RepoInfo repo,
            ModernRepoCache cache,
            bool enabled,
            bool seen,
            bool isPriority,
            float accentOffset,
            float cardHeight)
        {
            Repo = repo;
            Cache = cache;
            Enabled = enabled;
            Seen = seen;
            IsPriority = isPriority;
            AccentOffset = accentOffset;
            CardHeight = cardHeight;
        }

        public RepoInfo Repo { get; }
        public ModernRepoCache Cache { get; }
        public bool Enabled { get; }
        public bool Seen { get; }
        public bool IsPriority { get; }
        public float AccentOffset { get; }
        public float CardHeight { get; }
    }

    private static bool DrawCustomToggle(string id, ref bool v, float scale)
    {
        var p = ImGui.GetCursorScreenPos();
        var drawList = ImGui.GetWindowDrawList();

        var height = ImGui.GetFrameHeight();
        var width = height * 1.6f;
        var radius = height * 0.5f;

        var changed = false;
        ImGui.InvisibleButton(id, new Vector2(width, height));
        if (ImGui.IsItemClicked())
        {
            v = !v;
            changed = true;
        }

        var hovered = ImGui.IsItemHovered();
        
        // Colors
        var enabledColor = new Vector4(0.2f, 0.8f, 0.5f, 0.8f); // Emerald
        var disabledColor = new Vector4(0.8f, 0.2f, 0.2f, 0.7f); // Soft Red
        var bgColor = v ? enabledColor : disabledColor;
        
        if (!hovered)
        {
            bgColor.W *= 0.6f;
        }

        // Draw Background
        drawList.AddRectFilled(p, new Vector2(p.X + width, p.Y + height), ImGui.GetColorU32(bgColor), radius);
        
        // Draw Border (subtle)
        drawList.AddRect(p, new Vector2(p.X + width, p.Y + height), ImGui.GetColorU32(new Vector4(1, 1, 1, 0.1f)), radius);

        // Draw Knob
        var knobPos = new Vector2(p.X + radius + ((v ? 1 : 0) * (width - (radius * 2.0f))), p.Y + radius);
        drawList.AddCircleFilled(knobPos, radius - (2.5f * scale), ImGui.GetColorU32(new Vector4(1, 1, 1, 1)));

        return changed;
    }

    private void DrawBadge(string text, Vector4 background, Vector4 foreground, float scale)
    {
        var drawList = ImGui.GetWindowDrawList();
        var padding = new Vector2(6f * scale, 1.5f * scale);
        
        ImGui.SetWindowFontScale(0.85f);
        var textSize = ImGui.CalcTextSize(text);
        var badgeSize = new Vector2(textSize.X + (padding.X * 2), textSize.Y + (padding.Y * 2));
        var startPos = ImGui.GetCursorPos();

        ImGui.Dummy(badgeSize);
        var rectMin = ImGui.GetItemRectMin();
        var rectMax = ImGui.GetItemRectMax();
        
        drawList.AddRectFilled(rectMin, rectMax, ImGui.GetColorU32(background), 4f * scale);
        drawList.AddRect(rectMin, rectMax, ImGui.GetColorU32(foreground), 4f * scale, 0, 1f * scale);

        ImGui.SetCursorPos(new Vector2(startPos.X + padding.X, startPos.Y + padding.Y));
        ImGui.TextColored(foreground, text);
        ImGui.SetWindowFontScale(1f);
        
        ImGui.SetCursorPos(new Vector2(startPos.X + badgeSize.X, startPos.Y));
    }

    private bool PluginPassesLanguageFilter(PluginInfo plugin)
    {
        return !config.HideNonEnglishPlugins || IsLatinOnly(plugin);
    }

    private bool IsPluginCurrentOrUnknown(PluginInfo plugin)
    {
        return plugin.ApiLevel == 0 || plugin.ApiLevel == repoManager.CurrentApiLevel;
    }

    private bool PluginPassesClosedSourceFilter(PluginInfo plugin)
    {
        return !config.HideClosedSourcePlugins || !plugin.IsClosedSource;
    }

    private string GetRemoteUpdateStatusText()
    {
        if (config.LastRemoteRepoListUpdatedUtc <= 0)
        {
            return "Aetherfeed updated: unknown";
        }

        var remoteUpdated = DateTimeOffset.FromUnixTimeSeconds(config.LastRemoteRepoListUpdatedUtc).ToLocalTime();
        var nextUpdate = config.NextRemoteRepoListUpdatedUtc > 0
            ? DateTimeOffset.FromUnixTimeSeconds(config.NextRemoteRepoListUpdatedUtc).ToLocalTime()
            : remoteUpdated.AddHours(6);
        var now = uiOpenedAt;
        var nextText = GetApproximateNextUpdateText(nextUpdate, now);
        return $"Aetherfeed updated {remoteUpdated:MMM dd, yyyy HH:mm} • {nextText}";
    }

    private static string GetApproximateNextUpdateText(DateTimeOffset nextUpdate, DateTimeOffset now)
    {
        var remaining = nextUpdate - now;
        if (remaining.TotalSeconds <= 0)
        {
            return "Next update soon";
        }

        if (remaining.TotalHours >= 24)
        {
            var days = (int)Math.Ceiling(remaining.TotalDays);
            return $"Next update in ~{days}d";
        }

        if (remaining.TotalHours >= 1)
        {
            var hours = (int)Math.Floor(remaining.TotalHours);
            var mins = remaining.Minutes;
            return mins > 0 
                ? $"Next update in ~{hours}h {mins}m"
                : $"Next update in ~{hours}h";
        }

        var minutes = (int)Math.Ceiling(remaining.TotalMinutes);
        return $"Next update in ~{minutes}m";
    }

    private static string GetRelativeTimeText(DateTimeOffset timestamp, DateTimeOffset reference)
    {
        if (reference == default)
        {
            reference = DateTimeOffset.Now;
        }

        var delta = reference - timestamp;
        if (delta.TotalSeconds < 60)
        {
            return "just now";
        }

        if (delta.TotalMinutes < 60)
        {
            var minutes = (int)Math.Floor(delta.TotalMinutes);
            return $"{minutes}m ago";
        }

        if (delta.TotalHours < 24)
        {
            var hours = (int)Math.Floor(delta.TotalHours);
            return $"{hours}h ago";
        }

        var days = (int)Math.Floor(delta.TotalDays);
        return $"{days}d ago";
    }

    private static bool IsLatinOnly(PluginInfo plugin)
    {
        return plugin.IsLatinOnly;
    }

    private static string GetSortLabel(int repoSort)
    {
        return repoSort switch
        {
            1 => "Owner",
            2 => "URL",
            3 => "# Plugins",
            4 => "Last Updated",
            _ => "Default"
        };
    }

    private static int LowerBound(IReadOnlyList<float> values, float target)
    {
        var lo = 0;
        var hi = values.Count;
        while (lo < hi)
        {
            var mid = lo + ((hi - lo) / 2);
            if (values[mid] < target)
            {
                lo = mid + 1;
            }
            else
            {
                hi = mid;
            }
        }

        return lo;
    }


    private void UpdateSearchResults(IReadOnlyList<RepoInfo> repos)
    {
        searchResults = repos.Where(repo =>
            RepoMatchesSearch(repo, searchText, config.HideNonEnglishPlugins, config.HideClosedSourcePlugins)).ToHashSet();
    }

    private static void ShowPluginTooltip(PluginInfo plugin)
    {
        var scale = ImGuiHelpers.GlobalScale;
        
        ImGui.BeginTooltip();
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(4 * scale, 8 * scale));

        // Header: Name
        ImGui.SetWindowFontScale(1.1f);
        ImGui.TextColored(new Vector4(1f, 1f, 1f, 1f), plugin.Name);
        ImGui.SetWindowFontScale(1f);

        // Punchline (Italic/Muted)
        if (!string.IsNullOrEmpty(plugin.Punchline))
        {
            ImGui.TextColored(new Vector4(0.6f, 0.8f, 0.9f, 0.8f), plugin.Punchline);
        }

        if (plugin.IsClosedSource)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1f, 0.7f, 0.2f, 0.95f));
            ImGui.TextUnformatted("Closed Source");
            ImGui.PopStyleColor();
        }

        ImGui.Separator();

        // Description
        if (!string.IsNullOrEmpty(plugin.Description))
        {
            ImGui.PushTextWrapPos(ImGui.GetFontSize() * 25);
            ImGui.TextUnformatted(plugin.Description);
            ImGui.PopTextWrapPos();
        }

        // Footer: Metadata
        ImGui.Separator();
        
        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.5f, 0.5f, 0.6f, 1f));
        ImGui.TextUnformatted($"API Level: {plugin.ApiLevel}");
        
        if (!string.IsNullOrEmpty(plugin.RepoUrl))
        {
            ImGui.TextUnformatted(plugin.RepoUrl);
        }
        ImGui.PopStyleColor();

        ImGui.PopStyleVar();
        ImGui.EndTooltip();
    }

    private static bool RepoMatchesSearch(
        RepoInfo repo,
        string searchValue,
        bool hideNonEnglishPlugins,
        bool hideClosedSourcePlugins)
    {
        if (string.IsNullOrEmpty(searchValue))
        {
            return false;
        }

        return repo.Url.Contains(searchValue, StringComparison.CurrentCultureIgnoreCase)
               || repo.FullName.Contains(searchValue, StringComparison.CurrentCultureIgnoreCase)
               || repo.Plugins.Any(plugin => PluginMatchesSearch(
                   plugin,
                   searchValue,
                   hideNonEnglishPlugins,
                   hideClosedSourcePlugins));
    }

    private static bool PluginMatchesSearch(
        PluginInfo plugin,
        string searchValue,
        bool hideNonEnglishPlugins,
        bool hideClosedSourcePlugins)
    {
        if (hideNonEnglishPlugins && !IsLatinOnly(plugin))
        {
            return false;
        }
        if (hideClosedSourcePlugins && plugin.IsClosedSource)
        {
            return false;
        }

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

    private void CopyUrl(string url)
    {
        ImGui.SetClipboardText(url);
        lastCopiedUrl = url;
        lastCopiedTime = DateTime.Now;
        
        Plugin.NotificationManager.AddNotification(new Notification
        {
            Content = $"Copied to clipboard:\n{url}",
            Title = "Repository Browser",
            Type = NotificationType.Success
        });
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
