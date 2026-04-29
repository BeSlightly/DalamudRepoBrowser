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
    private void DrawModernHeader()

    {

        var scale = ImGuiHelpers.GlobalScale;

        var headerHeight = 118f * scale;

        var padding = 18f * scale;



        using (ImRaii.PushStyle(ImGuiStyleVar.WindowPadding, new Vector2(0, 0)))
        {

            using (ImRaii.Child(

                "ModernHeader",

                new Vector2(0, headerHeight),

                false,

                ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse))
            {



                var windowPos = ImGui.GetWindowPos();

                var windowSize = ImGui.GetWindowSize();

                var drawList = ImGui.GetWindowDrawList();

                DrawModernHeaderAurora(windowPos, windowSize, drawList, padding, scale);



            }
        }

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



        var settingsPressed = false;
        var settingsHovered = false;
        var sourcePressed = false;
        var sourceHovered = false;

        using (ImRaii.PushStyle(ImGuiStyleVar.FrameRounding, rounding)
                   .Push(ImGuiStyleVar.FrameBorderSize, 1f * scale))
        using (ImRaii.PushColor(ImGuiCol.Button, baseColor)
                   .Push(ImGuiCol.ButtonHovered, hoverColor)
                   .Push(ImGuiCol.ButtonActive, activeColor)
                   .Push(ImGuiCol.Border, new Vector4(0.3f, 0.55f, 0.8f, 0.45f)))
        {

            using (ImRaii.PushFont(UiBuilder.IconFont))
            {

                settingsPressed = ImGui.Button($"{FontAwesomeIcon.Wrench.ToIconString()}##ModernSettings", buttonSize);

                settingsHovered = ImGui.IsItemHovered();



                ImGui.SameLine(0, buttonGap);

                sourcePressed = ImGui.Button($"{FontAwesomeIcon.Globe.ToIconString()}##ModernSource", buttonSize);

                sourceHovered = ImGui.IsItemHovered();

            }



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



        }

    }

}
