using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Text.RegularExpressions;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Components;
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
    private static readonly Regex ChineseRegex = new(@"[\u4e00-\u9fff]", RegexOptions.Compiled);
    private static readonly Regex JapaneseRegex = new(@"[\u3040-\u30ff\u31f0-\u31ff\u3400-\u4dbf]", RegexOptions.Compiled);
    private static readonly Regex KoreanRegex = new(@"[\u1100-\u11ff\uac00-\ud7af]", RegexOptions.Compiled);

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
        }

        if (firstOpen)
        {
            repoManager.FetchRepoMasters();
            firstOpen = false;
        }

        var repos = repoManager.RepoList;

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
        if (ImGui.Checkbox("Use Modern UI", ref useModernUi))
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
        var headerHeight = 112f * scale;
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

        // Aetherfeed-inspired gradient: deep void to vibrant cyan
        var leftColor = new Vector4(0.012f, 0.024f, 0.05f, 1f);   // Deep void-950
        var midColor = new Vector4(0.02f, 0.08f, 0.16f, 1f);      // Void-900 blend
        var rightColor = new Vector4(0.024f, 0.45f, 0.65f, 1f);   // Vibrant aether-500

        // Main gradient background
        drawList.AddRectFilledMultiColor(
            windowPos,
            new Vector2(windowPos.X + windowSize.X, windowPos.Y + windowSize.Y),
            ImGui.GetColorU32(leftColor),
            ImGui.GetColorU32(rightColor),
            ImGui.GetColorU32(new Vector4(rightColor.X * 0.7f, rightColor.Y * 0.7f, rightColor.Z * 0.7f, 1f)),
            ImGui.GetColorU32(midColor));

        // Subtle top shine/glow effect
        var shineHeight = 3f * scale;
        drawList.AddRectFilledMultiColor(
            windowPos,
            new Vector2(windowPos.X + windowSize.X, windowPos.Y + shineHeight),
            ImGui.GetColorU32(new Vector4(0.4f, 0.8f, 0.95f, 0.15f)),
            ImGui.GetColorU32(new Vector4(0.4f, 0.8f, 0.95f, 0.3f)),
            ImGui.GetColorU32(new Vector4(0.4f, 0.8f, 0.95f, 0f)),
            ImGui.GetColorU32(new Vector4(0.4f, 0.8f, 0.95f, 0f)));

        // Bottom border with glow
        drawList.AddLine(
            new Vector2(windowPos.X, windowPos.Y + windowSize.Y - 1),
            new Vector2(windowPos.X + windowSize.X, windowPos.Y + windowSize.Y - 1),
            ImGui.GetColorU32(new Vector4(0.4f, 0.85f, 0.95f, 0.25f)),
            2f * scale);

        ImGui.SetCursorPos(new Vector2(padding, padding));
        ImGui.SetWindowFontScale(1.35f);

        // Title with subtle glow effect simulation
        var titlePos = ImGui.GetCursorScreenPos();
        ImGui.TextColored(new Vector4(1f, 1f, 1f, 1f), "Dalamud Repo Browser");
        ImGui.SetWindowFontScale(1f);

        // Subtitle with aether accent
        ImGui.TextColored(new Vector4(0.6f, 0.85f, 0.95f, 0.85f), "Discover repositories automatically scraped from GitHub");
        ImGui.TextColored(new Vector4(0.5f, 0.75f, 0.9f, 0.7f), GetRemoteUpdateStatusText());

        var buttonSize = new Vector2(34f * scale, 34f * scale);
        var buttonGap = 8f * scale;
        var rightGroupWidth = (buttonSize.X * 2) + buttonGap;
        ImGui.SetCursorPos(new Vector2(windowSize.X - rightGroupWidth - padding, padding));

        // Button styling with glassmorphism effect
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 12f * scale);
        ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.1f, 0.2f, 0.35f, 0.5f));
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.15f, 0.5f, 0.7f, 0.7f));
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.1f, 0.6f, 0.8f, 0.9f));

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

        ImGui.PopStyleColor(3);
        ImGui.PopStyleVar();

        ImGui.EndChild();
        ImGui.PopStyleVar();
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
        
        ImGui.PopStyleVar(); // FrameRounding
        ImGui.PopStyleColor(); // FrameBg

        var statusText = $"{filteredCount} repositories shown";
        var statusSize = ImGui.CalcTextSize(statusText);
        var textColor = ImGui.GetStyle().Colors[(int)ImGuiCol.Text];
        ImGui.SameLine(ImGui.GetWindowContentRegionMax().X - statusSize.X - padding);
        
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
            if (ImGui.Checkbox("Use Modern UI", ref useModernUi))
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

        ImGui.Spacing();
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
        var scale = ImGuiHelpers.GlobalScale;
        var padding = 16f * scale;
        var sectionSpacing = 6f * scale;
        var rowSpacing = 5f * scale;
        var chipPadding = new Vector2(7f * scale, 3f * scale);
        var rounding = 16f * scale;
        var chipRounding = 4f * scale;
        var borderThickness = 1f * scale;

        var textColor = ImGui.GetStyle().Colors[(int)ImGuiCol.Text];
        var mutedText = new Vector4(textColor.X, textColor.Y, textColor.Z, 0.6f);

        // Aetherfeed card style: Deep void background with subtle transparency
        var cardBg = new Vector4(0.06f, 0.08f, 0.12f, 0.9f);
        // Subtle border
        var cardBorder = new Vector4(0.2f, 0.35f, 0.6f, 0.25f);

        ImGui.BeginChild("RepoListModern");

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
                .Select(plugin => (plugin, valid: IsPluginCurrentOrUnknown(plugin)))
                .Where(plugin => (config.ShowOutdatedPlugins || plugin.valid)
                                 && PluginPassesLanguageFilter(plugin.plugin)
                                 && PluginPassesClosedSourceFilter(plugin.plugin))
                .ToList();
            if (visiblePlugins.Count == 0)
            {
                continue;
            }

            filteredCount++;

            var seen = prevSeenRepos.Contains(repoInfo.Url);
            var isPriority = config.PriorityRepos.Contains(repoInfo.Url);
            var accentWidth = 6f * scale;
            var accentInset = 0f;
            var accentGap = 8f * scale;
            var hasAccent = enabled || !seen;
            var accentOffset = accentWidth + accentGap;

            var lineHeight = ImGui.GetTextLineHeightWithSpacing();
            var singleChipHeight = ImGui.GetTextLineHeight() + (chipPadding.Y * 2);
            
            // Calculate height required for chips
            var chipsHeight = 0f;
            if (visiblePlugins.Count > 0)
            {
                var availWidth = ImGui.GetContentRegionAvail().X - (padding * 2) - accentOffset;
                var currentX = 0f;
                var rows = 1;
                
                for (var i = 0; i < visiblePlugins.Count; i++)
                {
                    var plugin = visiblePlugins[i].plugin;
                    var chipWidth = ImGui.CalcTextSize(plugin.Name).X + (chipPadding.X * 2);
                    
                    if (i > 0)
                    {
                        // Add spacing
                        currentX += rowSpacing;
                    }

                    if (currentX + chipWidth > availWidth)
                    {
                        // Wrap
                        rows++;
                        currentX = 0;
                    }
                    
                    currentX += chipWidth;
                }
                
                chipsHeight = (rows * singleChipHeight) + ((rows - 1) * rowSpacing);
            }

            var cardHeight = (padding * 2)
                             + (lineHeight * 2.2f) // More header space
                             + (sectionSpacing * 2)
                             + chipsHeight;

            var cardStart = ImGui.GetCursorScreenPos();
            var cardSize = new Vector2(ImGui.GetContentRegionAvail().X, cardHeight);
            var drawList = ImGui.GetWindowDrawList();

            // Detect Hover for interaction
            var isHovered = ImGui.IsMouseHoveringRect(cardStart, cardStart + cardSize);
            
            // Refined Card Background & Border with hover interaction
            var currentBg = isHovered ? new Vector4(0.08f, 0.11f, 0.16f, 0.95f) : cardBg;
            var currentBorder = isHovered ? new Vector4(0.3f, 0.5f, 0.8f, 0.5f) : cardBorder;
            var currentBorderThickness = isHovered ? 1.5f * scale : borderThickness;

            // Card Background
            drawList.AddRectFilled(cardStart, cardStart + cardSize, ImGui.GetColorU32(currentBg), rounding);

            // Card Border
            drawList.AddRect(cardStart, cardStart + cardSize, ImGui.GetColorU32(currentBorder), rounding, 0, currentBorderThickness);

            // Left vertical accent strip
            var accentColor = enabled
                ? new Vector4(0.2f, 0.8f, 0.5f, 0.8f) // Emerald for enabled
                : (!seen ? new Vector4(0.06f, 0.7f, 1f, 0.8f) : new Vector4(0f, 0f, 0f, 0f)); // Cyan for new

            if (hasAccent)
            {
                var accentMin = new Vector2(cardStart.X - currentBorderThickness, cardStart.Y + accentInset);
                var accentMax = new Vector2(cardStart.X + accentWidth, cardStart.Y + cardSize.Y - accentInset);
                var accentRounding = rounding;
                drawList.AddRectFilled(
                    accentMin,
                    accentMax,
                    ImGui.GetColorU32(accentColor),
                    accentRounding,
                    ImDrawFlags.RoundCornersLeft);
            }

            ImGui.Dummy(cardSize);

            var contentPaddingX = padding + accentOffset;
            ImGui.SetCursorScreenPos(new Vector2(cardStart.X + contentPaddingX, cardStart.Y + padding));
            ImGui.PushID(repoInfo.Url);

            // Using columns for layout
            if (ImGui.BeginTable($"Header##{repoInfo.Url}", 2, ImGuiTableFlags.SizingStretchProp))
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
                var buttonWidth = 30f * scale;
                var totalActionsWidth = toggleWidth + (buttonWidth * 2) + (ImGui.GetStyle().ItemSpacing.X * 2);
                
                // Right align within the column
                var columnWidth = ImGui.GetColumnWidth();
                if (columnWidth > totalActionsWidth)
                {
                    ImGui.SetCursorPosX(ImGui.GetCursorPosX() + (columnWidth - totalActionsWidth));
                }

                if (DrawCustomToggle($"##Enabled{repoInfo.Url}", ref enabled, scale))
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
                
                ImGui.PushFont(UiBuilder.IconFont);
                var copyPressed = ImGui.Button($"{FontAwesomeIcon.Copy.ToIconString()}##Copy{repoInfo.Url}", new Vector2(buttonWidth, 0));
                var copyHovered = ImGui.IsItemHovered();

                var openPressed = false;
                var openHovered = false;
                if (!string.IsNullOrEmpty(repoInfo.GitRepoUrl))
                {
                    ImGui.SameLine();
                    openPressed = ImGui.Button($"{FontAwesomeIcon.Globe.ToIconString()}##Open{repoInfo.Url}", new Vector2(buttonWidth, 0));
                    openHovered = ImGui.IsItemHovered();
                }
                else
                {
                    // Placeholder dummy to keep alignment consistent if globe is missing
                    ImGui.SameLine();
                    ImGui.Dummy(new Vector2(buttonWidth, 0));
                }

                ImGui.PopFont();

                if (copyPressed)
                {
                    ImGui.SetClipboardText(repoInfo.Url);
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
                ImGui.Dummy(new Vector2(0, sectionSpacing));
            }

            var infoParts = new List<string>();
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

            ImGui.TextColored(mutedText, string.Join("  •  ", infoParts));
            ImGui.Dummy(new Vector2(0, sectionSpacing));

            // Draw Chips with proper wrapping
            var chipAvailWidth = ImGui.GetContentRegionAvail().X;
            var chipCurrentX = 0f;
            var isFirstChip = true;

            foreach (var (plugin, valid) in visiblePlugins)
            {
                var chipWidth = ImGui.CalcTextSize(plugin.Name).X + (chipPadding.X * 2);
                
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

                DrawPluginChip(plugin, valid, chipPadding, scale, chipRounding);
                chipCurrentX += chipWidth;
                isFirstChip = false;
            }

            ImGui.PopID();

            ImGui.SetCursorScreenPos(new Vector2(cardStart.X, cardStart.Y + cardSize.Y + (12f * scale)));
        }

        ImGui.EndChild();
    }

    private void DrawPluginChip(
        PluginInfo plugin,
        bool valid,
        Vector2 padding,
        float scale,
        float chipRounding)
    {
        var drawList = ImGui.GetWindowDrawList();
        var textSize = ImGui.CalcTextSize(plugin.Name);
        var chipSize = new Vector2(textSize.X + (padding.X * 2), textSize.Y + (padding.Y * 2));

        ImGui.Dummy(chipSize);
        var rectMin = ImGui.GetItemRectMin();
        var rectMax = ImGui.GetItemRectMax();
        
        var hovered = ImGui.IsItemHovered();

        // Chip Background
        // Default: Dark transparent
        // Hover: Brighter blueish
        var fillColor = valid
            ? (hovered ? new Vector4(0.2f, 0.4f, 0.6f, 0.4f) : new Vector4(1f, 1f, 1f, 0.05f))
            : new Vector4(0.75f, 0.22f, 0.22f, 0.15f);

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
        var nextUpdate = remoteUpdated.AddHours(6);
        var now = DateTimeOffset.Now;
        var nextText = GetApproximateNextUpdateText(nextUpdate, now);
        return $"Aetherfeed updated {remoteUpdated:MMM dd, yyyy HH:mm} • {nextText}";
    }

    private static string GetApproximateNextUpdateText(DateTimeOffset nextUpdate, DateTimeOffset now)
    {
        if (nextUpdate <= now)
        {
            return "Next update soon";
        }

        var remaining = nextUpdate - now;
        if (remaining.TotalDays >= 1)
        {
            var days = (int)Math.Ceiling(remaining.TotalDays);
            return $"Next update in ~{days}d";
        }

        var hours = (int)Math.Ceiling(remaining.TotalHours);
        return $"Next update in ~{hours}h";
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
        var name = plugin.Name;
        var description = plugin.Description;
        return !ChineseRegex.IsMatch(name)
               && !ChineseRegex.IsMatch(description)
               && !JapaneseRegex.IsMatch(name)
               && !JapaneseRegex.IsMatch(description)
               && !KoreanRegex.IsMatch(name)
               && !KoreanRegex.IsMatch(description);
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
