using System.Numerics;

namespace AzeronHud;

/// <summary>
/// Every color the HUD draws with, so all of it is user-editable.
///
/// Alpha is part of each color; the HUD's overall opacity multiplies on top,
/// so a veil set to 0.6 alpha stays proportionally lighter as the whole HUD
/// fades. Defaults are a dark, quiet set meant to sit over the game rather
/// than compete with it -- the Azeron configurator's own palette is themeable
/// and light-leaning, which does not transfer.
/// </summary>
public sealed class ColorSet
{
    // --- buttons -----------------------------------------------------------

    /// <summary>Every button that has something assigned to it.</summary>
    public Vector4 Panel = new(0.13f, 0.15f, 0.19f, 1.00f);

    /// <summary>Buttons with nothing assigned, so they recede.</summary>
    public Vector4 PanelEmpty = new(0.10f, 0.10f, 0.11f, 0.55f);

    public Vector4 Border = new(0.42f, 0.47f, 0.56f, 1.00f);
    public Vector4 BorderEmpty = new(0.28f, 0.28f, 0.31f, 0.80f);

    // --- text ---------------------------------------------------------------

    public Vector4 TextTitle = new(0.93f, 0.93f, 0.95f, 1.00f);
    public Vector4 TextTitleDim = new(0.58f, 0.58f, 0.62f, 1.00f);
    public Vector4 TextKey = new(0.62f, 0.72f, 0.86f, 1.00f);
    public Vector4 TextCost = new(0.68f, 0.82f, 1.00f, 1.00f);
    public Vector4 TextCharges = new(1.00f, 0.95f, 0.70f, 1.00f);

    // --- states -------------------------------------------------------------

    public Vector4 Cooldown = new(0.00f, 0.00f, 0.00f, 0.62f);
    public Vector4 Unusable = new(0.00f, 0.00f, 0.00f, 0.45f);
    public Vector4 OutOfRange = new(0.45f, 0.10f, 0.10f, 0.38f);
    public Vector4 Proc = new(1.00f, 0.90f, 0.45f, 1.00f);
    public Vector4 ProcGlow = new(1.00f, 0.82f, 0.30f, 0.30f);
    public Vector4 KeyHeld = new(1.00f, 0.84f, 0.38f, 1.00f);


    /// <summary>Icon multiply color when the action can't be used.</summary>
    public Vector4 IconUnusable = new(0.55f, 0.55f, 0.55f, 1.00f);

    /// <summary>Long-press / double-press dots.</summary>
    public Vector4 Pip = new(0.70f, 0.78f, 0.90f, 0.85f);

    // --- the joystick --------------------------------------------------------

    public Vector4 StickBase = new(0.20f, 0.22f, 0.27f, 1.00f);
    public Vector4 StickRim = new(0.52f, 0.57f, 0.66f, 1.00f);
    public Vector4 StickKnob = new(0.74f, 0.79f, 0.88f, 1.00f);
    public Vector4 StickKnobActive = new(1.00f, 0.84f, 0.38f, 1.00f);
}
