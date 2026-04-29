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

}
