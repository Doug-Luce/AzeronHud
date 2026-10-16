using System;
using System.Numerics;
using AzeronHud.Game;
using Dalamud.Bindings.ImGui;

namespace AzeronHud.Ui;

/// <summary>
/// Colours for the overlay. The configurator's own palette is themeable and
/// light-leaning; a HUD sitting over the game wants something darker and
/// quieter, so these are our own, keyed off resolution tier so you can tell at
/// a glance which buttons the plugin actually understood.
/// </summary>
internal static class Palette
{
    internal static uint Background(ResolutionTier tier, float alpha) => tier switch
    {
        ResolutionTier.HotbarSlot => Rgba(0.13f, 0.15f, 0.19f, alpha),
        ResolutionTier.Keybind => Rgba(0.12f, 0.17f, 0.22f, alpha),
        ResolutionTier.RawKey => Rgba(0.14f, 0.14f, 0.15f, alpha * 0.9f),
        ResolutionTier.Unbindable => Rgba(0.22f, 0.17f, 0.09f, alpha * 0.9f),
        _ => Rgba(0.10f, 0.10f, 0.11f, alpha * 0.55f),
    };

    internal static uint Border(ResolutionTier tier, float alpha) => tier switch
    {
        ResolutionTier.HotbarSlot => Rgba(0.42f, 0.47f, 0.56f, alpha),
        ResolutionTier.Keybind => Rgba(0.33f, 0.47f, 0.60f, alpha),
        ResolutionTier.Unbindable => Rgba(0.60f, 0.47f, 0.22f, alpha),
        _ => Rgba(0.28f, 0.28f, 0.31f, alpha * 0.8f),
    };

    internal static readonly uint Title = Rgba(0.93f, 0.93f, 0.95f, 1f);
    internal static readonly uint TitleDim = Rgba(0.58f, 0.58f, 0.62f, 1f);
    internal static readonly uint Chord = Rgba(0.62f, 0.72f, 0.86f, 1f);
    internal static readonly uint Pressed = Rgba(1.00f, 0.84f, 0.38f, 1f);
    internal static readonly uint CooldownVeil = Rgba(0f, 0f, 0f, 0.62f);
    internal static readonly uint UnusableVeil = Rgba(0f, 0f, 0f, 0.45f);
    internal static readonly uint Charges = Rgba(1.00f, 0.95f, 0.70f, 1f);
    internal static readonly uint Pip = Rgba(0.70f, 0.78f, 0.90f, 0.85f);
    internal static readonly uint StickRing = Rgba(0.45f, 0.50f, 0.60f, 0.75f);
    internal static readonly Vector4 IconTintUnusable = new(0.55f, 0.55f, 0.55f, 1f);

    /// <summary>
    /// Scale a packed colour's alpha. Raw draw-list calls take an explicit
    /// colour, so ImGui's own style alpha doesn't reach them -- the HUD's
    /// opacity has to be folded in by hand or icons and text stay solid while
    /// the panels fade.
    /// </summary>
    internal static uint Fade(uint col, float a)
    {
        var alpha = (uint)Math.Clamp(((col >> 24) & 0xFF) * a, 0f, 255f);
        return (col & 0x00FFFFFF) | (alpha << 24);
    }

    private static uint Rgba(float r, float g, float b, float a) =>
        ImGui.ColorConvertFloat4ToU32(new Vector4(r, g, b, a));
}
