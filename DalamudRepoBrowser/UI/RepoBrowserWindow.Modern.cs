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

}
