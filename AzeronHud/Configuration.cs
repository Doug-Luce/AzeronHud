using Dalamud.Configuration;

namespace AzeronHud;

public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;

    // --- where the Azeron data lives -------------------------------------

    /// <summary>
    /// Azeron Software's config directory, as the game sees it. Empty means
    /// "work it out from the Wine user name", which is right on this setup.
    /// </summary>
    public string AzeronRoot = string.Empty;

    /// <summary>USB product id of the keypad to show. 4855 is the Cyborg II.</summary>
    public int ProductId = 4855;

    /// <summary>Profile id to show, or empty for "whichever was saved most recently".</summary>
    public string ProfileId = string.Empty;

    // --- appearance -------------------------------------------------------

    /// <summary>Width of one grid cell in unscaled pixels. The app's own presets are 60/72/90.</summary>
    public float CellSize = 64f;

    /// <summary>Gap between cells, as a fraction of cell width. The app uses 6/72.</summary>
    public float GapRatio = 6f / 72f;

    public float BackgroundAlpha = 0.85f;
    public float Rounding = 6f;

    /// <summary>Lock position and let clicks pass through to the game.</summary>
    public bool Locked;

    // --- what each button shows -------------------------------------------

    public bool ShowChord = true;
    public bool ShowIcons = true;
    public bool ShowCooldown = true;
    public bool ShowCooldownNumerals = true;
    public bool DimUnusable = true;
    public bool ShowEmptyButtons = true;
    public bool ShowSecondaryPips = true;

    /// <summary>Highlight a button while its key is held down.</summary>
    public bool HighlightPressed = true;

    // --- visibility --------------------------------------------------------

    public bool HideInCutscenes = true;

    /// <summary>Virtual-key code for the toggle hotkey, 0 for none.</summary>
    public int HotkeyVk;

    public bool HotkeyShift;
    public bool HotkeyCtrl;
    public bool HotkeyAlt;

    public void Save() => Plugin.PluginInterface.SavePluginConfig(this);
}
