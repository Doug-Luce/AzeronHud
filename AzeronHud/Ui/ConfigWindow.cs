using System;
using System.Collections.Generic;
using System.Numerics;
using AzeronHud.Azeron;
using AzeronHud.Game;
using System.Linq;
using AzeronHud.Layout;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.Keys;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;

namespace AzeronHud.Ui;

internal sealed class ConfigWindow : Window
{
    private readonly Configuration cfg;
    private readonly Action reload;
    private readonly Func<IReadOnlyList<AzeronProfile>> profiles;
    private readonly Func<string> status;
    private readonly SearchState search;
    private readonly Func<ButtonResolver> resolver;

    private bool capturingHotkey;

    /// <summary>Which tab's reset button is armed, if any.</summary>
    private string? pendingReset;

    internal ConfigWindow(
        Configuration cfg,
        Action reload,
        Func<IReadOnlyList<AzeronProfile>> profiles,
        Func<string> status,
        SearchState search,
        Func<ButtonResolver> resolver)
        : base("Azeron HUD settings###AzeronHudConfig")
    {
        this.cfg = cfg;
        this.reload = reload;
        this.profiles = profiles;
        this.status = status;
        this.search = search;
        this.resolver = resolver;

        Size = new Vector2(460, 480);
        SizeCondition = ImGuiCond.FirstUseEver;
    }

    public override void OnClose() => pendingReset = null;

    public override void Draw()
    {
        using var tabs = ImRaiiTabBar("##azhudtabs");
        if (!tabs)
            return;

        DrawProfileTab();
        DrawAppearanceTab();
        DrawContentTab();
        DrawFindTab();
        DrawCheckTab();
        DrawColorsTab();
        DrawToggleTab();
    }

    private void DrawProfileTab()
    {
        if (!ImGui.BeginTabItem("Profile"))
            return;

        ImGui.TextWrapped(
            "The plugin reads Azeron Software's own config directory. Under Wine the Linux "
            + "filesystem is the Z: drive, so the default path points there.");
        ImGui.Spacing();

        var root = cfg.AzeronRoot;
        ImGui.SetNextItemWidth(-1);
        if (ImGui.InputTextWithHint("##root", ProfileStore.GuessRoot(), ref root, 512))
        {
            cfg.AzeronRoot = root;
            cfg.Save();
            reload();
        }

        ImGui.TextDisabled("Leave blank to use the default shown above.");
        ImGui.Spacing();

        ImGui.TextUnformatted("Status:");
        ImGui.SameLine();
        ImGui.TextWrapped(status());

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        var all = profiles();
        var current = cfg.ProfileId.Length == 0
            ? "Auto (most recently saved)"
            : FindName(all, cfg.ProfileId);

        ImGui.TextUnformatted("Profile");
        ImGui.SetNextItemWidth(-1);
        if (ImGui.BeginCombo("##profile", current))
        {
            if (ImGui.Selectable("Auto (most recently saved)", cfg.ProfileId.Length == 0))
            {
                cfg.ProfileId = string.Empty;
                cfg.Save();
                reload();
            }

            foreach (var p in all)
            {
                if (ImGui.Selectable(p.Name, p.Id == cfg.ProfileId))
                {
                    cfg.ProfileId = p.Id;
                    cfg.Save();
                    reload();
                }
            }

            ImGui.EndCombo();
        }

        ImGui.Spacing();
        ImGui.TextUnformatted("Keypad");
        ImGui.SetNextItemWidth(-1);
        var layouts = DeviceLayout.All;
        var layoutName = layouts.TryGetValue(cfg.ProductId, out var l) ? l.Name : $"Unknown ({cfg.ProductId})";
        if (ImGui.BeginCombo("##device", layoutName))
        {
            foreach (var (pid, dev) in layouts)
            {
                if (ImGui.Selectable(dev.Name, pid == cfg.ProductId))
                {
                    cfg.ProductId = pid;
                    cfg.Save();
                    reload();
                }
            }

            ImGui.EndCombo();
        }

        ImGui.Spacing();
        if (ImGui.Button("Reload now"))
            reload();

        ImGui.EndTabItem();
    }

    private void DrawAppearanceTab()
    {
        if (!ImGui.BeginTabItem("Appearance"))
            return;

        var scaleToWindow = cfg.ScaleToWindow;
        if (ImGui.Checkbox("Scale the keypad to the window", ref scaleToWindow))
        {
            cfg.ScaleToWindow = scaleToWindow;
            cfg.Save();
        }

        ImGui.TextDisabled(cfg.ScaleToWindow
            ? "Drag the HUD's bottom-right corner to resize it."
            : "The HUD sizes itself to the button size below.");

        ImGui.Spacing();

        using (ImRaii.Disabled(cfg.ScaleToWindow))
        {
            var size = cfg.CellSize;
            if (ImGui.SliderFloat("Button size", ref size, 36f, 120f, "%.0f px"))
            {
                cfg.CellSize = size;
                cfg.Save();
            }
        }

        var gap = cfg.GapRatio * 100f;
        if (ImGui.SliderFloat("Gap", ref gap, 0f, 25f, "%.0f%%"))
        {
            cfg.GapRatio = gap / 100f;
            cfg.Save();
        }

        var icon = cfg.IconScale;
        if (ImGui.SliderFloat("Icon size", ref icon, 0.35f, 1.0f, "%.2f"))
        {
            cfg.IconScale = icon;
            cfg.Save();
        }

        var alpha = cfg.Opacity;
        if (ImGui.SliderFloat("Opacity", ref alpha, 0.2f, 1f, "%.2f"))
        {
            cfg.Opacity = alpha;
            cfg.Save();
        }

        var rounding = cfg.Rounding;
        if (ImGui.SliderFloat("Corner rounding", ref rounding, 0f, 16f, "%.0f"))
        {
            cfg.Rounding = rounding;
            cfg.Save();
        }

        ImGui.Spacing();
        Toggle("Panel behind the whole HUD", ref cfg.ShowWindowBackground);
        Toggle("Panel behind each button", ref cfg.ShowButtonPanels);

        ImGui.Spacing();
        var locked = cfg.Locked;
        if (ImGui.Checkbox("Lock (ignore the mouse entirely)", ref locked))
        {
            cfg.Locked = locked;
            cfg.Save();
        }

        ImGui.TextWrapped(cfg.Locked
            ? "Locked: the HUD is inert. Clicks and drags over it go straight to the game, "
              + "and there are no tooltips. Unlock to move or resize it."
            : "Unlocked: you can drag and resize the HUD, and hovering a button shows what it "
              + "does. While unlocked the mouse is captured over the HUD, so lock it once "
              + "you're happy with the position. /azeronhud lock toggles this.");

        ResetButton("appearance", () =>
        {
            var d = new Configuration();
            cfg.ScaleToWindow = d.ScaleToWindow;
            cfg.CellSize = d.CellSize;
            cfg.IconScale = d.IconScale;
            cfg.GapRatio = d.GapRatio;
            cfg.Opacity = d.Opacity;
            cfg.Rounding = d.Rounding;
            cfg.ShowWindowBackground = d.ShowWindowBackground;
            cfg.ShowButtonPanels = d.ShowButtonPanels;
            cfg.Locked = d.Locked;
        });

        ImGui.EndTabItem();
    }

    private void DrawContentTab()
    {
        if (!ImGui.BeginTabItem("Content"))
            return;

        Toggle("Action icons", ref cfg.ShowIcons);
        Toggle("Key each button sends", ref cfg.ShowChord);
        Toggle("Cooldown sweep", ref cfg.ShowCooldown);
        Toggle("Cooldown seconds", ref cfg.ShowCooldownNumerals);
        Toggle("Dim unusable actions", ref cfg.DimUnusable);
        Toggle("Highlight procs", ref cfg.ShowProcGlow);
        Toggle("Tint when out of range", ref cfg.ShowRangeTint);
        Toggle("MP / item cost", ref cfg.ShowCost);
        Toggle("Highlight the key being held", ref cfg.HighlightPressed);
        Toggle("Unassigned buttons", ref cfg.ShowEmptyButtons);
        Toggle("Long-press / double-press dots", ref cfg.ShowSecondaryPips);

        ResetButton("content", () =>
        {
            var d = new Configuration();
            cfg.ShowIcons = d.ShowIcons;
            cfg.ShowChord = d.ShowChord;
            cfg.ShowCooldown = d.ShowCooldown;
            cfg.ShowCooldownNumerals = d.ShowCooldownNumerals;
            cfg.DimUnusable = d.DimUnusable;
            cfg.ShowProcGlow = d.ShowProcGlow;
            cfg.ShowRangeTint = d.ShowRangeTint;
            cfg.ShowCost = d.ShowCost;
            cfg.HighlightPressed = d.HighlightPressed;
            cfg.ShowEmptyButtons = d.ShowEmptyButtons;
            cfg.ShowSecondaryPips = d.ShowSecondaryPips;
        });

        ImGui.EndTabItem();
    }

    private void DrawFindTab()
    {
        if (!ImGui.BeginTabItem("Find"))
            return;

        ImGui.TextWrapped(
            "Type an action and the buttons that cast it light up on the HUD. Matches the action "
            + "name, the key a button sends, your own Azeron label, or the FFXIV keybind id.");
        ImGui.Spacing();

        var query = search.Query;
        ImGui.SetNextItemWidth(-90);
        if (ImGui.InputTextWithHint("##find", "Fire III, Sprint, Shift+C...", ref query, 128))
            search.Query = query;

        ImGui.SameLine();
        if (ImGui.Button("Clear", new Vector2(80, 0)))
            search.Clear();

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        if (!search.Active)
        {
            ImGui.TextDisabled("Nothing searched for yet.");
            ImGui.EndTabItem();
            return;
        }

        var hits = resolver().Buttons.Values.Where(search.Matches).OrderBy(b => b.AzeronId).ToList();
        if (hits.Count == 0)
        {
            ImGui.TextDisabled("No button does that.");
            ImGui.EndTabItem();
            return;
        }

        ImGui.TextUnformatted($"{hits.Count} button{(hits.Count == 1 ? "" : "s")}:");
        foreach (var b in hits)
        {
            ImGui.BulletText($"Button {b.AzeronId}");
            ImGui.SameLine();
            ImGui.TextUnformatted(b.Title.Length > 0 ? b.Title : "(unnamed)");
            if (b.Chord.Length > 0)
            {
                ImGui.SameLine();
                ImGui.TextDisabled($"— {b.Chord}");
            }
        }

        ImGui.EndTabItem();
    }

    private void DrawCheckTab()
    {
        if (!ImGui.BeginTabItem("Check"))
            return;

        var buttons = resolver().Buttons.Values.OrderBy(b => b.AzeronId).ToList();
        if (buttons.Count == 0)
        {
            ImGui.TextDisabled("No profile loaded.");
            ImGui.EndTabItem();
            return;
        }

        ImGui.TextWrapped("Problems worth knowing about in the loaded profile.");
        ImGui.Spacing();

        // Two buttons sending the same keystroke. Sometimes deliberate, often
        // a leftover from rearranging a profile.
        var duplicates = buttons
            .Where(b => b.Chord.Length > 0 && !b.IsStick)
            .GroupBy(b => b.Chord, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .ToList();

        Section($"Same key on more than one button ({duplicates.Count})", duplicates.Count, () =>
        {
            foreach (var g in duplicates)
                ImGui.BulletText($"{g.Key} — buttons {string.Join(", ", g.Select(b => b.AzeronId))}");
        });

        var unbound = buttons.Where(b => b.Tier == ResolutionTier.RawKey).ToList();
        Section($"Sends a key FFXIV does nothing with ({unbound.Count})", unbound.Count, () =>
        {
            foreach (var b in unbound)
                ImGui.BulletText($"Button {b.AzeronId} — {b.Chord}");
        });

        var unreadable = buttons.Where(b => b.Tier == ResolutionTier.Unbindable && !b.IsStick).ToList();
        Section($"FFXIV can't see these at all ({unreadable.Count})", unreadable.Count, () =>
        {
            foreach (var b in unreadable)
                ImGui.BulletText($"Button {b.AzeronId} — {b.Title}");
        });

        var empty = buttons.Where(b => b.Tier == ResolutionTier.Empty).ToList();
        Section($"Nothing assigned ({empty.Count})", empty.Count, () =>
        {
            ImGui.TextDisabled("Free buttons, if you need somewhere to put something.");
            foreach (var b in empty)
                ImGui.BulletText($"Button {b.AzeronId}");
        });

        ImGui.Spacing();
        ImGui.Separator();
        var resolved = buttons.Count(b => b.Tier == ResolutionTier.HotbarSlot);
        ImGui.TextDisabled($"{resolved} of {buttons.Count} buttons are bound to a hotbar action.");

        ImGui.EndTabItem();
    }

    /// <summary>A collapsing section that says "none" instead of opening empty.</summary>
    private static void Section(string label, int count, Action body)
    {
        if (count == 0)
        {
            ImGui.TextDisabled($"{label} — none");
            return;
        }

        if (ImGui.CollapsingHeader(label, ImGuiTreeNodeFlags.DefaultOpen))
            body();
    }

    private void DrawColorsTab()
    {
        if (!ImGui.BeginTabItem("Colors"))
            return;

        ImGui.TextWrapped(
            "Alpha is part of each color. The opacity slider on the Appearance tab multiplies on "
            + "top, so veils stay proportional as the whole HUD fades.");
        ImGui.Spacing();

        var c = cfg.Colors;

        if (ImGui.CollapsingHeader("Buttons", ImGuiTreeNodeFlags.DefaultOpen))
        {
            Color("Button", ref c.Panel);
            Color("Button outline", ref c.Border);
            ImGui.Spacing();
            Color("Unassigned button", ref c.PanelEmpty);
            Color("Unassigned outline", ref c.BorderEmpty);
        }

        if (ImGui.CollapsingHeader("States"))
        {
            Color("Cooldown", ref c.Cooldown);
            Color("Unusable", ref c.Unusable);
            Color("Unusable icon tint", ref c.IconUnusable);
            Color("Out of range", ref c.OutOfRange);
            Color("Proc outline", ref c.Proc);
            Color("Proc wash", ref c.ProcGlow);
            Color("Key held", ref c.KeyHeld);
            Color("Search match", ref c.SearchMatch);
        }

        if (ImGui.CollapsingHeader("Text"))
        {
            Color("Action name", ref c.TextTitle);
            Color("Action name, unresolved", ref c.TextTitleDim);
            Color("Key", ref c.TextKey);
            Color("Cost", ref c.TextCost);
            Color("Charges", ref c.TextCharges);
            Color("Long/double-press dots", ref c.Pip);
        }

        if (ImGui.CollapsingHeader("Thumbstick"))
        {
            Color("Disc", ref c.StickBase);
            Color("Rim", ref c.StickRim);
            Color("Knob", ref c.StickKnob);
            Color("Knob, deflected", ref c.StickKnobActive);
        }

        ResetButton("colors", () => cfg.Colors = new ColorSet());

        ImGui.EndTabItem();
    }

    /// <summary>
    /// A color row. Saving happens when the picker is released rather than on
    /// every frame of a drag, which would rewrite the config file continuously.
    /// </summary>
    private void Color(string label, ref Vector4 value)
    {
        ImGui.ColorEdit4(label, ref value,
            ImGuiColorEditFlags.AlphaBar | ImGuiColorEditFlags.AlphaPreviewHalf);

        if (ImGui.IsItemDeactivatedAfterEdit())
            cfg.Save();
    }

    private void DrawToggleTab()
    {
        if (!ImGui.BeginTabItem("Toggle"))
            return;

        ImGui.TextWrapped("Type /azeronhud to show or hide the HUD. You can also bind a key:");
        ImGui.Spacing();

        var label = capturingHotkey
            ? "Press a key..."
            : cfg.HotkeyVk == 0
                ? "Click to set"
                : KeyCodeMap.ModifierPrefix(CurrentMods()) + KeyCodeMap.KeyName((byte)cfg.HotkeyVk);

        if (ImGui.Button(label, new Vector2(200, 0)))
            capturingHotkey = !capturingHotkey;

        if (capturingHotkey && TryCaptureKey(out var captured))
        {
            cfg.HotkeyVk = captured;
            capturingHotkey = false;
            cfg.Save();
        }

        ImGui.SameLine();
        if (ImGui.Button("Clear"))
        {
            cfg.HotkeyVk = 0;
            capturingHotkey = false;
            cfg.Save();
        }

        var shift = cfg.HotkeyShift;
        var ctrl = cfg.HotkeyCtrl;
        var alt = cfg.HotkeyAlt;
        if (ImGui.Checkbox("Shift", ref shift)) { cfg.HotkeyShift = shift; cfg.Save(); }
        ImGui.SameLine();
        if (ImGui.Checkbox("Ctrl", ref ctrl)) { cfg.HotkeyCtrl = ctrl; cfg.Save(); }
        ImGui.SameLine();
        if (ImGui.Checkbox("Alt", ref alt)) { cfg.HotkeyAlt = alt; cfg.Save(); }

        ImGui.Spacing();
        ImGui.TextWrapped(
            "The hotkey is read from the game's own key state, so it only fires while the game "
            + "window has focus and you're not typing. A neat trick: bind a spare Azeron button "
            + "to F13-F24 in the Azeron app and use that here -- the keypad then toggles its own "
            + "HUD, and FFXIV ignores the key entirely.");

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        Toggle("Hide during cutscenes", ref cfg.HideInCutscenes);

        ImGui.EndTabItem();
    }

    private Mods CurrentMods()
    {
        var m = Mods.None;
        if (cfg.HotkeyShift) m |= Mods.Shift;
        if (cfg.HotkeyCtrl) m |= Mods.Ctrl;
        if (cfg.HotkeyAlt) m |= Mods.Alt;
        return m;
    }

    private static bool TryCaptureKey(out int vk)
    {
        foreach (var key in Plugin.KeyState.GetValidVirtualKeys())
        {
            if (key is VirtualKey.LBUTTON or VirtualKey.RBUTTON or VirtualKey.MBUTTON)
                continue;
            if (key is VirtualKey.SHIFT or VirtualKey.CONTROL or VirtualKey.MENU)
                continue;

            if (Plugin.KeyState[key])
            {
                vk = (int)key;
                return true;
            }
        }

        vk = 0;
        return false;
    }

    /// <summary>
    /// Reset control for one tab. Two clicks, because there is no undo and a
    /// stray click would otherwise throw away a layout someone tuned.
    /// </summary>
    private void ResetButton(string scope, Action reset)
    {
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        var armed = pendingReset == scope;
        if (ImGui.Button(armed ? "Click again to confirm" : "Reset to defaults"))
        {
            if (armed)
            {
                reset();
                cfg.Save();
                pendingReset = null;
            }
            else
            {
                pendingReset = scope;
            }
        }

        if (!armed)
            return;

        ImGui.SameLine();
        if (ImGui.Button("Cancel"))
            pendingReset = null;
    }

    private void Toggle(string label, ref bool value)
    {
        var v = value;
        if (!ImGui.Checkbox(label, ref v))
            return;

        value = v;
        cfg.Save();
    }

    private static string FindName(IReadOnlyList<AzeronProfile> all, string id)
    {
        foreach (var p in all)
            if (p.Id == id)
                return p.Name;
        return "(profile not found)";
    }

    /// <summary>ImGui.BeginTabBar with an End that runs even on an early return.</summary>
    private static TabBarScope ImRaiiTabBar(string id) => new(ImGui.BeginTabBar(id));

    private readonly ref struct TabBarScope(bool open)
    {
        private readonly bool open = open;
        public static implicit operator bool(TabBarScope s) => s.open;
        public void Dispose()
        {
            if (open)
                ImGui.EndTabBar();
        }
    }
}
