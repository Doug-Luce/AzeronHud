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

## The thumbstick is two unrelated problems

`analogSettings` is present on **every** input with identical default values, so
it is only meaningful on id 24. Reading it off a plain button gives you a
convincing-looking WASD that means nothing.

Worse, the same trap applies to the stick itself. The FFXIV profile has the
stick set to `XInputJoystick` (type 21) and *still* carries `analogKeys` of
W/D/S/A -- stale defaults. Gate on `InputType.EmulatesKeys()`, never on the
presence of `analogKeys`, or the HUD will confidently display movement keys the
stick does not send.

Two cases, both verified against the renderer bundle:

- **Emulates keys** (the AnalogJoystickWithKeys family, plus
  `XInputJoystickAndKeys`): each direction is an ordinary bind and resolves
  through the same chain as a button. `isRightAnalog` picks which half of
  `analogKeys`/`diagonalKeys` is live (`o.isRightAnalog ? Sl.RIGHT : Sl.LEFT`),
  and `isEightDirectionalTrigger` gates the diagonals -- when false they are
  stored but inert, and the configurator renders them as `null`.
- **XInput** (type 21): the game sees a gamepad axis. Analog axes are not in
  FFXIV's keybind table at all -- `Keybind._gamepadSettings` holds the PAD_*
  pseudo-keys for gamepad *buttons*, not sticks. Nothing in `IGameConfig`
  reliably names what the left stick does either, so the HUD says which stick
  it is and points at the game's Gamepad settings rather than inventing a
  binding.

Direction arrays mix encodings within one array -- `["KeyW",0,0]`, a string
then two bare JSON numbers. Json.NET coerces them to `"0"`, which `KeyCodeMap`
already treats as empty, but it means the DTO must be `List<string>` and not
`List<object>`.

## IGamepadState reports the raw axis, not a unit vector

`IGamepadState.LeftStick` is documented only as "the left analogue sticks tilt
vector" with no range given. It is the game's raw axis, roughly **-99..99**.
Treating it as -1..1 threw the joystick knob about 99x too far: a modest tilt of
25 produced a 460px offset on a 40px disc, so the knob slammed into the cell's
clip rect and jittered with every twitch.

Two defences, not one. The value is scaled by 1/99 and clamped to unit length
*and* the knob's travel is defined as `baseRadius - knobRadius`, so the knob is
contained by construction rather than by a magic factor -- a future wrong guess
about the range saturates instead of escaping. A small deadzone swallows a
resting stick's drift.

## Three families of hotbar keybind, not one

`^HOTBAR_(\d+)_([0-9AB])$` only covers 120 of the 134 slot-backed ids:

| ids | where the slot lives |
|---|---|
| `HOTBAR_1_1` … `HOTBAR_10_B` (120) | `GetSlotById(bar, slot)` |
| `HOTBAR_EX_0` … `HOTBAR_EX_B` (12) | `PetHotbar.GetHotbarSlot(slot)` |
| `HOTBAR_CONTENTS_ACT_L` / `_R` (2) | `GetDutyActionSlot(index)`, whose `DutyActionSlot` embeds a `HotbarSlot` |

Miss the last two families and those buttons silently fall to a lower tier — a name, no icon, no
cooldown, no proc. The remaining 13 `HOTBAR_*` ids (`HOTBAR_SET_n`, `HOTBAR_UP`/`DOWN`) are bar-switching
commands and correctly resolve to a name only.

The EX suffixes look 0-based next to the standard bars' 1-based ones, but the two sets are identical —
{1-9, 0, A, B} and {0-9, A, B} are the same twelve characters — so the slot mapping is shared, and '0'
means slot ten in both.

## The pet bar replaces hotbar 1 behind GetSlotById's back

While a pet bar is up the game shows pet actions on hotbar 1, but
`GetSlotById(0, n)` still returns the standard slot -- so the HUD would keep
showing the job actions hidden underneath it.

`RaptureHotbarModule.PetHotbarMode` (a `uint`) is the switch and
`PetHotbar.GetHotbarSlot(n)` the replacement. The exact meaning of nonzero
values is not documented, so `SlotFor` falls back to the standard slot whenever
the pet slot is empty: a misread of `PetHotbarMode` can then only ever cost us
the pet reading, never the normal one. Check it with `/azeronhud dump` while a
pet bar is actually up.

Note `PetHotbarActive` exists in the binary but is **not** on
`RaptureHotbarModule` -- don't reach for it there.

## Mini-hotbar state comes from the slot, not from ActionManager

Three calls on `HotbarSlot`, signatures confirmed against the shipped
FFXIVClientStructs rather than guessed:

```
bool IsActionHighlighted(HotbarSlotType commandType, uint actionId)   // proc is up
bool IsSlotActionTargetInRange()                                      // no args
Span<byte> CostText                                                   // fixed buffer, NUL-terminated
```

`CostText` is a raw buffer, so decoding it per cell per frame would be pure
garbage. The numeric `CostValue` on the same slot moves with it, so that is the
change detector and the string is only rebuilt when it actually changes.

Out-of-range is deliberately **not** drawn as the unusable grey. They are
different states -- the action is fine, you just can't reach -- and sharing a
color would make a reachable-but-uncastable action indistinguishable from a
castable one at the wrong distance.

## The joystick is drawn the way Azeron draws it

The configurator's analog representation is a styled `Circle` (filled,
`border-radius:50%`) with a `PureDot` absolutely positioned inside it, moved by
`transform: translate(x, y)` from the live stick reading. The HUD mirrors that:
a filled disc, a rim, and a knob offset from centre.

The knob is driven differently per mode, because the two modes expose different
things. An XInput stick reports a real axis, so `IGamepadState.LeftStick` /
`RightStick` drives it directly. A key-emulating stick has no axis to read at
all -- but the keys it sends are observable through `IKeyState`, so the
deflection is reconstructed from whichever direction keys are currently held.

## Buttons are not color-coded by resolution tier

An earlier version tinted each button by how far the chain got -- amber for
"assigned to something FFXIV cannot report", and so on. It was rejected, and
rightly: it put a warning colour on perfectly normal setups like an XInput
thumbstick, and it made the HUD read as a diagnostic rather than a reference.

The only panel distinction left is "has something assigned" versus "doesn't".
Everything else an unreadable bind needs to say, it says in its label and
tooltip. `ResolutionTier` still drives *what text* a button shows; it just no
longer drives what colour it is.

## Colors are all user-editable

Every color lives in `ColorSet`, serialized with the config. Alpha is part of
each color and the HUD's opacity multiplies on top, so a veil at 0.6 alpha
stays proportionally lighter as the whole HUD fades -- which is why none of the
helpers apply their own alpha multipliers.

`ImGui.ColorEdit4` fires every frame of a drag, so the save is gated on
`IsItemDeactivatedAfterEdit()`; saving per frame would rewrite the config file
continuously while the user drags a picker.

## IKeyState keeps reporting keys while you type

The game fills its key-state array regardless of whether a text field has
focus, so a hotkey polled from `IKeyState` fires while you are typing in chat.
A hotkey of M toggled the HUD on every M you sent to someone.

`RaptureAtkModule.Instance()->AtkModule.IsTextInputActive()` is the guard, and
covers search boxes as well as chat.

## Draw-list colors don't see ImGui's style alpha

`ImGuiStyleVar.Alpha` only reaches widgets that resolve their color through
`GetColorU32`. Everything the overlay draws is a raw `ImDrawList` call with an
explicit color, so the opacity setting has to be folded into each one by hand
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

Profile selection **does** read the app's Chromium IndexedDB LevelDB, reversing an earlier decision here
that called the payoff small. It wasn't: "most recently saved" shows another game's bindings the moment
you last edited that game's profile, and the HUD is then confidently, silently wrong about every button.
The app records the answer itself:

```json
{"profileId":"75ca7f5e-…","linkedGame":"ffxiv_dx11","linkedType":"process","isFocusSwitching":true}
```

The chain is: pinned in settings, then the profile linked to FFXIV, then `lastUsedSoftwareProfile`, then
most recently saved. The reason lands in the status line so a surprising pick is visible.

The store is log-structured, so `AzeronAppState` scans the raw `.log`/`.ldb` files for the JSON the app
wrote and takes the **last** match rather than truly parsing it. That is a heuristic, so every failure
— missing directory, mid-write, unreadable — returns "no information" and falls through to the next step.
It is never an error.

Preferring the *link* over *last used* also means we need not follow live profile switching: the link is
stable, while last-used moves when the app switches on focus.

## Deliberately out of scope for v1

- **Layering** (`layeringProfileId`): the schema supports a button switching to another profile while held.
  Neither of the user's profiles uses it. Would need the resolver to follow into a second profile.
- **Lefty layouts and other models**: the extractor already reads them; only the Cyborg II is emitted
  because it's the only one testable here.
- **Pet hotbar**: hotbar 1 shows pet actions in game, but `GetSlotById(0, n)` still returns the standard
  slot, so the HUD and the real bar can disagree while a pet bar is up.
