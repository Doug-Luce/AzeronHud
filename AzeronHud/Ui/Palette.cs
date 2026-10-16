using System;
using System.Numerics;
using AzeronHud.Game;
using Dalamud.Bindings.ImGui;

namespace AzeronHud.Ui;

/// <summary>
/// Turns the configured colours into what the draw list wants.
///
/// Raw ImDrawList calls take an explicit packed colour and never see ImGui's
/// style alpha, so the HUD's overall opacity has to be multiplied in here --
/// otherwise the panels fade and the icons and text stay solid.
/// </summary>
internal static class Palette
{
    /// <summary>Pack a configured colour, scaled by the HUD's opacity.</summary>
    internal static uint Of(Vector4 c, float opacity) =>
        ImGui.ColorConvertFloat4ToU32(c with { W = Math.Clamp(c.W * opacity, 0f, 1f) });

    /// <summary>Pseudo-tier the overlay uses for the thumbstick's own panel colour.</summary>
    internal const ResolutionTier StickTier = (ResolutionTier)100;

    internal static uint Panel(ColorSet c, ResolutionTier tier, float opacity) => Of(tier switch
    {
        StickTier => c.PanelStick,
        ResolutionTier.HotbarSlot => c.PanelHotbar,
        ResolutionTier.Keybind => c.PanelKeybind,
        ResolutionTier.RawKey => c.PanelRawKey,
        ResolutionTier.Unbindable => c.PanelUnresolvable,
        _ => c.PanelEmpty,
    }, opacity);

    internal static uint Border(ColorSet c, ResolutionTier tier, float opacity) => Of(tier switch
    {
        ResolutionTier.HotbarSlot => c.BorderHotbar,
        ResolutionTier.Keybind => c.BorderKeybind,
        ResolutionTier.Unbindable => c.BorderUnresolvable,
        _ => c.BorderOther,
    }, opacity);
}
