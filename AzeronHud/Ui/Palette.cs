using System;
using System.Numerics;
using AzeronHud.Game;
using Dalamud.Bindings.ImGui;

namespace AzeronHud.Ui;

/// <summary>
/// Turns the configured colors into what the draw list wants.
///
/// Raw ImDrawList calls take an explicit packed color and never see ImGui's
/// style alpha, so the HUD's overall opacity has to be multiplied in here --
/// otherwise the panels fade and the icons and text stay solid.
/// </summary>
internal static class Palette
{
    /// <summary>Pack a configured color, scaled by the HUD's opacity.</summary>
    internal static uint Of(Vector4 c, float opacity) =>
        ImGui.ColorConvertFloat4ToU32(c with { W = Math.Clamp(c.W * opacity, 0f, 1f) });

    /// <summary>
    /// Buttons are not color-coded by how much the plugin managed to resolve.
    /// Only "has something assigned" versus "doesn't" changes the panel, so an
    /// unreadable bind looks like any other button and says what it is in its
    /// label and tooltip instead.
    /// </summary>
    internal static uint Panel(ColorSet c, ResolutionTier tier, float opacity) =>
        Of(tier == ResolutionTier.Empty ? c.PanelEmpty : c.Panel, opacity);

    internal static uint Border(ColorSet c, ResolutionTier tier, float opacity) =>
        Of(tier == ResolutionTier.Empty ? c.BorderEmpty : c.Border, opacity);
}
