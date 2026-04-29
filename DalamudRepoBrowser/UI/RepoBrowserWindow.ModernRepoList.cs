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
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;

namespace DalamudRepoBrowser;

internal sealed partial class RepoBrowserWindow
{
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

        using (ImRaii.Child("RepoListModern"))
        {

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
                using (ImRaii.PushId(repoInfo.Url))
                {
                    ImGui.Indent(contentPaddingX);

                    // Using columns for layout
                    using (var table = ImRaii.Table("Header", 2, ImGuiTableFlags.SizingStretchProp))
                        if (table)
                        {
                            ImGui.TableSetupColumn("Main", ImGuiTableColumnFlags.WidthStretch);
                            ImGui.TableSetupColumn("Actions", ImGuiTableColumnFlags.WidthFixed, 200f * scale);

                            ImGui.TableNextRow();
                            ImGui.TableSetColumnIndex(0);

                            var repoName = string.IsNullOrEmpty(repoInfo.FullName) ? repoInfo.Url : repoInfo.FullName;

                            // Title Styling
                            ImGui.SetWindowFontScale(1.15f);
                            using (ImRaii.PushColor(ImGuiCol.Text, new Vector4(1f, 1f, 1f, 0.95f)))
                            {
                                ImGui.TextUnformatted(repoName);
                            }
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
                            using (ImRaii.PushStyle(ImGuiStyleVar.FrameRounding, 8f * scale))
                            using (ImRaii.PushColor(ImGuiCol.Button, new Vector4(1f, 1f, 1f, 0.05f))
                                       .Push(ImGuiCol.ButtonHovered, new Vector4(1f, 1f, 1f, 0.1f)))
                            {

                                var isJustCopied = lastCopiedUrl == repoInfo.Url && (DateTime.Now - lastCopiedTime).TotalSeconds < 2;
                                var copyIcon = isJustCopied ? FontAwesomeIcon.Check : FontAwesomeIcon.Copy;
                                var copyPressed = false;
                                var copyHovered = false;
                                var openPressed = false;
                                var openHovered = false;

                                using (ImRaii.PushFont(UiBuilder.IconFont))
                                {
                                    using (ImRaii.PushColor(ImGuiCol.Text, new Vector4(0.4f, 1f, 0.7f, 1f), isJustCopied))
                                    {
                                        copyPressed = ImGui.Button($"{copyIcon.ToIconString()}##Copy", actionButton);
                                        copyHovered = ImGui.IsItemHovered();
                                    }

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

                                }

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
                            }

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
                }

                ImGui.SetCursorScreenPos(new Vector2(cardStart.X, cardStart.Y + cardSize.Y + cardSpacing));
            }

            var bottomPadding = runningY - endOffsets[endIndex];
            if (bottomPadding > 0f)
            {
                ImGui.Dummy(new Vector2(0, bottomPadding));
            }

        }
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

}
