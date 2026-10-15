# Notes

Things that cost time to work out, so they don't have to be worked out twice.

## The Azeron profile has no geometry in it

`x`, `y`, `interval`, `yInterval` are `0` on all 43 inputs, in every profile. There are no row/column or
finger fields anywhere in the schema. The configurator's layout is a CSS `grid-template-areas` string
living in the Electron renderer bundle, one per keypad model:

`azeron-software.appimage` → squashfs → `resources/app.asar` → `out/renderer/assets/index-*.js`

The Cyborg II right-handed template (minified symbol `eJ`; the lefty mirror is `tJ`, picked by
`Ud(keypadType, isLefty)`):

```
"b21 b33 b34 b35 b39 b40 b42 b28 b43"
"b25 b4  b8  b12 b17 b44 b29 b22 b31"
"b26 b3  b7  b11 b16 b51 b46 b30 b45"
"b36 b2  b6  b10 b15 b19 b24 b24 b41"
"b27 b1  b5  b9  b14 b49 b24 b24 b20"
"b32 b37 b38 b13 b18 b50 b23 b48 b47"
```

Area `bN` is `inputs[].id == N`. `b24` spans 2x2 and is the thumbstick. Cells are
`buttonSize` wide by `buttonSize * 106/75` tall (`qp=106/75` in the bundle) — **not square**; the app's
three UI scale presets are `buttonSize` 60/72/90 with gaps 3/6/6.

`tools/extract_layout.py` parses that into `Layout/cyborg2.json` as grid cells rather than pixels, so the
plugin's single `CellSize` setting reproduces the configurator's proportions at any scale. Taking the
bounding box of each area name gets the thumbstick's 2x2 span for free, with no special case.

The grid references `b1`..`b51`, but only ~34 of those exist on a real Cyborg II. **`pinOne != 255` is the
test for "this button is physically present"** — 255 means the slot exists in the schema but not on this
model. The user's saved layout in the app's own localStorage (`azeron-ui-button-mapper`,
`deviceMappers["35298"]`) was byte-identical to the stock template, so there's no per-user override to
worry about yet. If someone rearranges their mapper in the app, we'd need to read that key.

## `pinOne != 255` is NOT the test for "this button exists"

It looks like it should be, and it is wrong. The profile lists buttons the
configurator never draws -- most visibly **id 21, the profile-switch button**,
which has `pinOne: 0, pinTwo: 0` and shows up as a phantom "Switch" cell. On the
Cyborg II it also over-counts ids 42 and 43.

The app filters the grid through a per-device descriptor, selected by
`Sf(keypadType)` in the renderer bundle:

```js
case $e.CyborgV2: return GI;
GI = { buttonCodes: [1..20, 22, 23, 24, 28, 29, 30, 31, 36, 37, 38, 41],
       profileSwitchCode: 21, isAnalogInput: e => e === 24, isMouseWheel: () => !1 }
```

31 codes -- 30 buttons plus the thumbstick -- which matches the advertised
"22 tower + 8 thumb" exactly. `tools/extract_layout.py` bakes that list into the
layout JSON and the resolver iterates it, so the profile's `pinOne` is now only
used for diagnostics.

Note `isMouseWheel` is non-trivial only for the Cyro descriptor (ids 39/40); the
Cyborg II has no wheel, which is why those two ids are absent from its list.

## `types[0]` is the only trustworthy signal, not `keyValues`

Unassigned slots keep stale `keyValues` from whatever was there before. The Arc Raiders profile has ids
28-31 carrying decimal VKs `38/37/40/39` (factory-default arrow keys) while `types[0] == 11` (None).
Gate on the type, always.

The full 41-value enum is in `Azeron/InputType.cs`, lifted from the same bundle. Two corrections to the
obvious guesses:

- ids **39/40** (`types[0]` 29/30) are the **scroll wheel**, not the analog stick.
- the analog stick is **id 24**, which is also the 2x2 `b24` cell.

## The key encoding is mixed

A slot holds either a W3C `KeyboardEvent.code` string (`"KeyW"`, `"Digit3"`, `"ShiftLeft"`) or a decimal
Windows VK already in string form (`"80"`, `"187"`). The Electron UI writes the former; the latter is
legacy/firmware-default residue. `"0"` means empty. Both have to be accepted.

`KeyCodeMap` deliberately produces **raw VK bytes** rather than named `SeVirtualKey` members: FFXIV's
`SeVirtualKey` is Windows VK codes below 128, so a cast at the call site is correct and survives the enum
being renamed upstream.

## Two differently-ordered modifier enums

```
FFXIV.Client.System.Input.KeyModifierFlag    None=0 Shift=1 Ctrl=2 Alt=4   <-- the one in a keybind
FFXIV.Component.GUI.ModifierFlag             None=0 Ctrl=1  Alt=2  Shift=4
```

Picking the wrong one gives a HUD that's right about everything except `Shift+C`.

## Keybind names come from the game, not a hardcoded table

The `ConfigKey` Excel sheet pairs each SCREAMING_SNAKE input id with its localized display text
("HOTBAR_1_1" / "Hotbar 1 - Slot 1"). Which of its two string columns holds which isn't guaranteed stable
across Lumina regenerations, so `KeybindIndex.LoadNames` tries both against the `InputId` name set and
takes whichever matches. No assumption to break.

## Hotbar slot suffixes aren't hex

`HOTBAR_<bar>_<slot>` runs `1`-`9` then `0`, `A`, `B` for slots 10, 11 and 12. So `'0'` is index 9, not 0.
Bars are 1-based in the name and 0-based in `GetSlotById`.

## Let the game compute the hotbar

`HotbarSlot` already exposes `GetIconIdForSlot`, `GetDisplayNameForSlot`, `IsSlotUsable`,
`GetSlotActionCooldownPercentage` and `GetApparentIconRecastCharges`. Those are the calls the real hotbar
renders from, so using them means the HUD behaves identically — and actions, items, macros, emotes, mounts
and gear sets all resolve through one code path.

Don't reimplement `ActionManager.GetRecastTime` arithmetic.

## Draw-list colours don't see ImGui's style alpha

`ImGuiStyleVar.Alpha` only reaches widgets that resolve their colour through
`GetColorU32`. Everything the overlay draws is a raw `ImDrawList` call with an
explicit colour, so the opacity setting has to be folded into each one by hand
(`Palette.Fade`) -- otherwise the panels fade and the icons and text stay solid.

Watch for double-application: `Palette.Background`/`Border` already take the
alpha, so they must not also be passed through `Fade`.

## AllowClickthrough does not make a window click-through

It is an *offer*, not a state:

> Gets or sets a value indicating whether this window should offer to be made
> click-through via the window's titlebar context menu.

The state is `IsClickthrough`, which is settable directly. `AllowPinning` is the
same shape. Both default on, and either one makes Dalamud draw an internal
titlebar button -- which on a `NoTitleBar` HUD is an interactive widget floating
over the content that takes focus when clicked. Both are turned off here and the
plugin manages `IsClickthrough` itself.

Locking a HUD has to mean **inert**, not merely immovable: without
`NoMouseInputs` the window still swallows every click and drag that lands on it,
so the game stops responding wherever the HUD happens to sit. `NoNavFocus` and
`NoBringToFrontOnFocus` are set unconditionally for the same reason.

## Scale is applied exactly once

`GlobalUiScale` is 1.5 on this machine at 4K. The JobBars fork learned the hard way that hover rects drift
from what's drawn when scale gets applied in two places. Here: one `cell` scalar computed at the top of
`Draw()`, every rect derived from it, and `ImGui.InvisibleButton` for all hit-testing so ImGui owns the
hover geometry. Never a hand-rolled `IsMouseHoveringRect`.

`IsDisableViewport` is true in this install, hence `ForceMainWindow = true` on the overlay.

## Profiles are rewritten live

The configurator saves on every slider drag, with no lock file, so reads can land mid-write. `ProfileStore`
retries on `JsonException`. This fires often and must not be noisy. (It bit during development: the profile
changed under the test harness between two runs because the app was open in the background.)

Profile selection is by mtime ("most recently saved") or an explicit pick in settings. Deliberately **not**
by decoding `lastUsedSoftwareProfile` out of the app's Chromium IndexedDB LevelDB — it's a compacting
binary store and the payoff is small. Noted so nobody re-litigates it.

## Deliberately out of scope for v1

- **Layering** (`layeringProfileId`): the schema supports a button switching to another profile while held.
  Neither of the user's profiles uses it. Would need the resolver to follow into a second profile.
- **Lefty layouts and other models**: the extractor already reads them; only the Cyborg II is emitted
  because it's the only one testable here.
- **Pet hotbar**: hotbar 1 shows pet actions in game, but `GetSlotById(0, n)` still returns the standard
  slot, so the HUD and the real bar can disagree while a pet bar is up.
