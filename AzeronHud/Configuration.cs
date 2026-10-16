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

    /// <summary>
    /// Size the keypad to whatever the window is, instead of sizing the window
    /// to the keypad. The grid keeps its proportions either way.
    /// </summary>
    public bool ScaleToWindow = true;

    /// <summary>
    /// Width of one grid cell in unscaled pixels, used when <see cref="ScaleToWindow"/>
    /// is off. The Azeron app's own presets are 60/72/90.
    /// </summary>
    public float CellSize = 64f;

    /// <summary>Gap between cells, as a fraction of cell width. The app uses 6/72.</summary>
    public float GapRatio = 6f / 72f;

    /// <summary>Overall opacity of the HUD -- panels, icons, text, everything.</summary>
    public float Opacity = 0.85f;

    /// <summary>Migrates configs written before the setting covered more than the panels.</summary>
    [Newtonsoft.Json.JsonProperty("BackgroundAlpha")]
    private float LegacyBackgroundAlpha { set => Opacity = value; }

    public float Rounding = 6f;

    /// <summary>The panel behind the whole HUD. Off leaves the buttons floating.</summary>
    public bool ShowWindowBackground = true;

    /// <summary>The filled panel behind each button. Off leaves outlines and icons only.</summary>
    public bool ShowButtonPanels = true;

    /// <summary>
    /// Make the HUD completely inert: no mouse input, no moving, no resizing,
    /// no tooltips, and nothing taken away from the game. This is what you
    /// want once it's positioned.
    /// </summary>
    public bool Locked;

    // --- what each button shows -------------------------------------------

    public bool ShowChord = true;
    public bool ShowIcons = true;
    public bool ShowCooldown = true;
    public bool ShowCooldownNumerals = true;
    public bool DimUnusable = true;

    /// <summary>Light up a button when the game is highlighting its action -- a proc.</summary>
    public bool ShowProcGlow = true;

    /// <summary>Tint a button when its target is out of range, distinctly from unusable.</summary>
    public bool ShowRangeTint = true;

    /// <summary>MP or item count in the button's corner.</summary>
    public bool ShowCost = true;
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

    /// <summary>Every colour the HUD draws with. See <see cref="ColorSet"/>.</summary>
    public ColorSet Colors = new();

    public void Save() => Plugin.PluginInterface.SavePluginConfig(this);
}
