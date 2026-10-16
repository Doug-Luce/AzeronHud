# Azeron HUD

A [Dalamud](https://github.com/goatcorp/Dalamud) plugin that draws your Azeron keypad on screen, in the
same layout as the Azeron configurator, with every button labelled by what it does in FFXIV.

## What it shows

Buttons bound to a hotbar slot get the real thing: the action's icon and name, its cooldown and remaining
seconds, charges, MP cost, a highlight when a proc comes up, and dimming when you can't use it or the
target is out of range. A bar along the top edge marks the actions that run on the global cooldown, so
your weaponskills and your abilities are easy to tell apart at a glance.

Buttons bound to anything else are labelled too — movement, targeting, the map, whatever you've put
there. Buttons the game can't see at all, like a mouse click or a macro, say so rather than sitting
blank. Unassigned buttons fade into the background.

The thumbstick is drawn as a stick, and the knob follows your thumb.

## Setting it up

Install it, then type `/azeronhud`. That's usually all there is to it — the plugin finds Azeron Software's
settings on its own and reads whichever profile the app has linked to FFXIV.

**Your keypad doesn't need to be plugged in.** The plugin reads your saved profile, not the device.

Changes you make in the Azeron app show up within a couple of seconds, so you can tweak a bind and watch
the HUD follow.

Drag the HUD where you want it and resize it by its corner, then turn on **click-through** in the
settings so it stops taking your mouse. A click-through HUD is completely inert: clicks and drags over it
go straight to the game.

## Making it yours

Everything is adjustable, in `/azeronhud config`:

- **Size and position** — the keypad scales to whatever you drag the window to, or you can set an exact
  button size. There's a button to snap the window tight around the keypad.
- **What each button shows** — icons, action names, the key it sends, cooldown numbers, cost, charges,
  button numbers, and so on. Turn the names off for an icon-only HUD.
- **Compact mode** — show only the buttons bound to an actual ability, and the HUD shrinks to just those.
- **Colours** — all of them, including a dark outline behind text so it stays readable over a bright wall
  or a dark cave.
- **When it's visible** — a toggle key, hiding during cutscenes, and fading out while you're not in
  combat.

If you want the keypad to toggle its own HUD, bind a spare button to one of F13–F24 in the Azeron app and
set that as the toggle key. FFXIV ignores those keys entirely, so nothing else fires.

## Commands

| Command | |
|---|---|
| `/azeronhud` | Show or hide the HUD |
| `/azeronhud config` | Open the settings |
| `/azeronhud reload` | Re-read your profile and keybinds |

## Supported keypads

Azeron **Cyborg II**. Other Azeron models use the same profile format and would mostly be a matter of
adding their layout — open an issue if you have one and want it supported.

## Licence

MIT.
