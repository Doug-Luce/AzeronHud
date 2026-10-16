# Azeron HUD

A [Dalamud](https://github.com/goatcorp/Dalamud) plugin that draws your Azeron keypad over FFXIV, in the
same layout as the Azeron configurator, with every button labelled by what it actually does in game.

Each button shows, in order of preference:

1. the **hotbar action's icon and name**, when the key is bound to a hotbar slot — with the real cooldown,
   charge count and MP cost, a highlight when a proc is up, dimming when the action isn't usable, and a
   separate tint when the target is out of range;
2. the **FFXIV keybind's name** ("Turn On/Off Auto-run", "Move Forward"), when it's bound to something
   that isn't a hotbar slot;
3. your own **Azeron label**, if you set one in the Azeron app;
4. the **raw key** otherwise.

The **thumbstick** is drawn the way the Azeron configurator draws it — a disc with a knob on it — and the
knob moves: from the real gamepad axis when the stick is on XInput, or from whichever direction keys are
held when it emulates keys. In key-emulating mode each live direction is placed around the disc and
resolves like any other button, with the diagonals appearing only when eight-directional mode is on. On
XInput the game sees a gamepad axis rather than a keystroke, so the HUD names the stick instead of
guessing at a binding.

Buttons the game can't see — mouse clicks, XInput, macros, the scroll wheel — say what they are rather
than silently going blank.

Hidden by default. Toggle it with `/azeronhud` (or `/azhud`), or bind a key in the settings.

Position and size it while unlocked, then **lock it** (`/azeronhud lock`) so it stops taking the mouse.
A locked HUD is completely inert — clicks and drags over it go straight to the game.

## Supported hardware

Azeron **Cyborg II** (USB `16d0:12f7`). Other models are a data drop away — the layouts are generated from
the Azeron app itself by `tools/extract_layout.py`, which already knows how to read them; only the Cyborg II
is enabled because it's the only one that's been tested on real hardware.

## How it finds your keypad

It reads Azeron Software's own config directory. **The keypad doesn't need to be plugged in** — the plugin
reads the saved profile, not the device.

On Linux under Wine, the prefix maps `Z:` to the filesystem root, so the default path is
`Z:\home\<you>\.config\Azeron Software`. Override it in the settings if yours lives elsewhere.

Edits you make in the Azeron app show up within a couple of seconds, with no reload.

## Colors

Every color is editable under Settings → Colors, with a reset. Buttons are deliberately not tinted by how
much the plugin could work out — a button is a button, and anything it couldn't resolve says so in its
label and tooltip instead. The only panel distinction is assigned versus unassigned. Alpha is part of each
color and the opacity slider multiplies on top.

## Commands

| Command | What it does |
|---|---|
| `/azeronhud` | Show or hide the HUD |
| `/azeronhud config` | Open settings |
| `/azeronhud reload` | Re-read the profile and keybind table |
| `/azeronhud lock` | Make the HUD inert: no mouse input, no moving, no tooltips |
| `/azeronhud dump` | Print the full resolution chain to the Dalamud log |

`dump` is the thing to reach for when a button says something you don't expect: it prints every button's
decoded key, the FFXIV keybind it matched, and what that resolved to, as text.

## A trick for the toggle key

Dalamud has no global hotkey, so the plugin polls the game's own key state — meaning the toggle only fires
while the game window has focus and you aren't typing. That's the right behaviour for a HUD.

If you want the keypad to toggle its own HUD: bind a spare Azeron button to one of `F13`–`F24` in the
Azeron app, then set that as the hotkey here. FFXIV ignores those keys entirely, so nothing else fires.

## Building

```sh
export DALAMUD_HOME=~/.xlcore/dalamud/Hooks/dev
./deploy.sh -p:EnableWindowsTargeting=true
```

`EnableWindowsTargeting` is needed when building on Linux without the Windows Desktop runtime installed.
`deploy.sh` drops the result into `~/.xlcore/devPlugins/AzeronHud`; reload it in game from
`/xlplugins` → Dev Tools, with no restart.

## Regenerating the device layout

The Azeron profile JSON contains no geometry — `x`/`y` are zero on every input. The real layout is a CSS
`grid-template-areas` string inside the configurator's Electron bundle. To re-extract it (after an Azeron
Software update, say):

```sh
python3 tools/extract_layout.py ~/AppImages/azeron-software.appimage
```

It unpacks to a temp directory, writes `AzeronHud/Layout/*.json`, and cleans up after itself. The plugin
embeds those files, so it never touches the AppImage at runtime.
