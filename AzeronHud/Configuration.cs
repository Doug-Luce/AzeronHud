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

    /// <summary>
    /// Work out which keypad to show from what's actually on disk, instead of
    /// making the user pick. Only worth turning off if you own two.
    /// </summary>
    public bool AutoDetectDevice = true;

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

    /// <summary>The action name along the foot of a button. Off gives an icon-only HUD.</summary>
    public bool ShowActionName = true;

    /// <summary>
    /// How many lines the action name may wrap onto. Two is enough for most of
    /// the long ones -- "Presence of Mind", "Perfect Balance" -- without the
    /// text crowding out the icon.
    /// </summary>
    public int NameLines = 2;

    /// <summary>Multiplier on every label's size. 1.0 is the built-in proportions.</summary>
    public float TextScale = 1f;

    /// <summary>
    /// Draw labels over a dark outline. The HUD sits on anything from a white
    /// wall to a dark cave, and flat text on no panel is unreadable over half
    /// of it.
    /// </summary>
    public bool ShowTextOutline = true;

    /// <summary>
    /// Show only buttons bound to a hotbar action, and trim the grid to what's
    /// left. Turns the full keypad reference into a combat-sized HUD.
    /// </summary>
    public bool OnlyHotbarActions;
    public bool ShowIcons = true;

    /// <summary>
    /// Icon size as a fraction of the space available in a cell, once the
    /// label strip is set aside. 1.0 fills that space edge to edge.
    /// </summary>
    public float IconScale = 0.68f;
    public bool ShowCooldown = true;
    public bool ShowCooldownNumerals = true;
    public bool DimUnusable = true;

    /// <summary>Light up a button when the game is highlighting its action -- a proc.</summary>
    public bool ShowProcGlow = true;

    /// <summary>Tint a button when its target is out of range, distinctly from unusable.</summary>
    public bool ShowRangeTint = true;

    /// <summary>MP or item count in the button's corner.</summary>
    public bool ShowCost = true;

    /// <summary>Brief pulse when a cooldown longer than a GCD comes back up.</summary>
    public bool ShowReadyFlash = true;
    public bool ShowEmptyButtons = true;
    public bool ShowSecondaryPips = true;

    /// <summary>
    /// The Azeron button index in the corner of each cell, matching the
    /// configurator's own button numbering -- handy while editing a bind.
    /// </summary>
    public bool ShowButtonNumbers;

    /// <summary>Highlight a button while its key is held down.</summary>
    public bool HighlightPressed = true;

    // --- visibility --------------------------------------------------------

    /// <summary>Reopen the HUD on load if it was open when you last quit.</summary>
    public bool RememberOpen = true;

    /// <summary>Whether the HUD was open last time. Maintained, not edited.</summary>
    public bool WasOpen;

    public bool HideInCutscenes = true;

    /// <summary>Fade the HUD down when you're not fighting.</summary>
    public bool DimOutOfCombat;

    /// <summary>Opacity multiplier applied while out of combat.</summary>
    public float OutOfCombatOpacity = 0.45f;

    /// <summary>Virtual-key code for the toggle hotkey, 0 for none.</summary>
    public int HotkeyVk;

    public bool HotkeyShift;
    public bool HotkeyCtrl;
    public bool HotkeyAlt;

    /// <summary>Every color the HUD draws with. See <see cref="ColorSet"/>.</summary>
    public ColorSet Colors = new();

    public void Save() => Plugin.PluginInterface.SavePluginConfig(this);
}
