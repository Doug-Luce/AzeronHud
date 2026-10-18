using System;
using System.Collections.Generic;
using System.Numerics;
using AzeronHud.Azeron;
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
    private readonly Action fitWindow;

    private bool capturingHotkey;

    /// <summary>Which tab's reset button is armed, if any.</summary>
    private string? pendingReset;

    internal ConfigWindow(
        Configuration cfg,
        Action reload,
        Func<IReadOnlyList<AzeronProfile>> profiles,
        Func<string> status,
        Action fitWindow)
        : base("Azeron HUD settings###AzeronHudConfig")
    {
        this.fitWindow = fitWindow;
        this.cfg = cfg;
        this.reload = reload;
        this.profiles = profiles;
        this.status = status;

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
            ? "Auto (follow the Azeron app)"
            : FindName(all, cfg.ProfileId);

        ImGui.TextUnformatted("Profile");
        ImGui.TextDisabled("Auto uses whichever profile the Azeron app has linked to FFXIV.");
        ImGui.SetNextItemWidth(-1);
        if (ImGui.BeginCombo("##profile", current))
        {
            if (ImGui.Selectable("Auto (follow the Azeron app)", cfg.ProfileId.Length == 0))
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

        var auto = cfg.AutoDetectDevice;
        if (ImGui.Checkbox("Detect automatically", ref auto))
        {
            cfg.AutoDetectDevice = auto;
            cfg.Save();
            reload();
        }

        // Scoped to the picker alone -- a method-scoped `using` here would grey
        // out everything below it, including the Reload button.
        using (ImRaii.Disabled(cfg.AutoDetectDevice))
        {
            ImGui.SetNextItemWidth(-1);
            var layouts = DeviceLayout.All;
            var layoutName = layouts.TryGetValue(cfg.ProductId, out var l)
                ? l.Name
                : $"Unknown ({cfg.ProductId})";

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

        var text = cfg.TextScale;
        if (ImGui.SliderFloat("Label size", ref text, 0.5f, 2.0f, "%.2fx"))
        {
            cfg.TextScale = text;
            cfg.Save();
        }

        var alpha = cfg.Opacity;
        if (ImGui.SliderFloat("Opacity", ref alpha, 0.2f, 1f, "%.2f"))
        {
            cfg.Opacity = alpha;
            cfg.Save();
        }

        var pad = cfg.WindowPadding;
        if (ImGui.SliderFloat("Window padding", ref pad, 0f, 16f, "%.0f px"))
        {
            cfg.WindowPadding = pad;
            cfg.Save();
        }

        var rounding = cfg.Rounding;
        if (ImGui.SliderFloat("Corner rounding", ref rounding, 0f, 16f, "%.0f"))
        {
            cfg.Rounding = rounding;
            cfg.Save();
        }

        ImGui.Spacing();
        if (ImGui.Button("Fit window to keypad"))
            fitWindow();

        ImGui.SameLine();
        Toggle("automatically", ref cfg.AutoFitWindow);
        ImGui.TextDisabled("The keypad keeps its proportions, so a window of another shape\nleaves an empty band. Fitting removes it.");

        ImGui.Spacing();
        Toggle("Panel behind the whole HUD", ref cfg.ShowWindowBackground);
        Toggle("Panel behind each button", ref cfg.ShowButtonPanels);

        ImGui.Spacing();
        var locked = cfg.Locked;
        if (ImGui.Checkbox("Click-through (locks position, ignores the mouse)", ref locked))
        {
            cfg.Locked = locked;
            cfg.Save();
        }

        ImGui.TextWrapped(cfg.Locked
            ? "On: the HUD is inert. Clicks and drags over it go straight to the game, "
              + "and there are no tooltips. Turn it off to move or resize it."
            : "Off: you can drag and resize the HUD, and hovering a button shows what it "
              + "does. While it's off the mouse is captured over the HUD, so turn it on once "
              + "you're happy with the position.");

        ResetButton("appearance", () =>
        {
            var d = new Configuration();
            cfg.ScaleToWindow = d.ScaleToWindow;
            cfg.CellSize = d.CellSize;
            cfg.IconScale = d.IconScale;
            cfg.TextScale = d.TextScale;
            cfg.GapRatio = d.GapRatio;
            cfg.Opacity = d.Opacity;
            cfg.Rounding = d.Rounding;
            cfg.WindowPadding = d.WindowPadding;
            cfg.AutoFitWindow = d.AutoFitWindow;
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
        Toggle("Action names", ref cfg.ShowActionName);

        using (ImRaii.Disabled(!cfg.ShowActionName))
        {
            var lines = cfg.NameLines;
            ImGui.SetNextItemWidth(220);
            if (ImGui.SliderInt("Lines a name may wrap to", ref lines, 1, 3))
            {
                cfg.NameLines = lines;
                cfg.Save();
            }
        }

        Toggle("Key each button sends", ref cfg.ShowChord);
        Toggle("Azeron button numbers", ref cfg.ShowButtonNumbers);
        Toggle("Dark outline behind text", ref cfg.ShowTextOutline);
        Toggle("Cooldown sweep", ref cfg.ShowCooldown);
        Toggle("Cooldown seconds", ref cfg.ShowCooldownNumerals);

        using (ImRaii.Disabled(!cfg.ShowCooldownNumerals))
        {
            ImGui.Indent();

            var cdScale = cfg.CooldownNumberScale;
            ImGui.SetNextItemWidth(220);
            if (ImGui.SliderFloat("Size##cooldown", ref cdScale, 0.12f, 0.7f, "%.2f"))
            {
                cfg.CooldownNumberScale = cdScale;
                cfg.Save();
            }

            ImGui.SetNextItemWidth(220);
            if (ImGui.BeginCombo("Position##cooldown", AnchorName(cfg.CooldownNumberAnchor)))
            {
                foreach (var a in new[]
                         {
                             LabelAnchor.Center, LabelAnchor.TopLeft, LabelAnchor.TopRight,
                             LabelAnchor.BottomLeft, LabelAnchor.BottomRight,
                         })
                {
                    if (ImGui.Selectable(AnchorName(a), a == cfg.CooldownNumberAnchor))
                    {
                        cfg.CooldownNumberAnchor = a;
                        cfg.Save();
                    }
                }

                ImGui.EndCombo();
            }

            Toggle("Outline##cooldown", ref cfg.OutlineCooldownNumbers);
            ImGui.TextDisabled("Corners already hold the key, charges and cost.");

            ImGui.Unindent();
        }
        Toggle("Dim unusable actions", ref cfg.DimUnusable);
        Toggle("Highlight procs", ref cfg.ShowProcGlow);
        Toggle("Tint when out of range", ref cfg.ShowRangeTint);
        Toggle("MP / item cost", ref cfg.ShowCost);
        Toggle("Highlight the key being held", ref cfg.HighlightPressed);
        Toggle("Flash when a cooldown finishes", ref cfg.ShowReadyFlash);
        Toggle("Mark actions on the global cooldown", ref cfg.ShowGcdMark);
        ImGui.TextDisabled("A bar along the top edge. Abilities are unmarked by default;\ngive them a color under Colors to mark both.");

        ImGui.Spacing();
        Toggle("Only buttons bound to a hotbar action", ref cfg.OnlyHotbarActions);
        ImGui.TextDisabled("Trims the HUD to your abilities. Empty edges close up; gaps between\nbuttons stay, so the layout still matches your hand.");

        using (ImRaii.Disabled(cfg.OnlyHotbarActions))
            Toggle("Unassigned buttons", ref cfg.ShowEmptyButtons);
        Toggle("Long-press / double-press dots", ref cfg.ShowSecondaryPips);

        ResetButton("content", () =>
        {
            var d = new Configuration();
            cfg.ShowIcons = d.ShowIcons;
            cfg.ShowChord = d.ShowChord;
            cfg.ShowCooldown = d.ShowCooldown;
            cfg.ShowCooldownNumerals = d.ShowCooldownNumerals;
            cfg.CooldownNumberScale = d.CooldownNumberScale;
            cfg.CooldownNumberAnchor = d.CooldownNumberAnchor;
            cfg.OutlineCooldownNumbers = d.OutlineCooldownNumbers;
            cfg.DimUnusable = d.DimUnusable;
            cfg.ShowProcGlow = d.ShowProcGlow;
            cfg.ShowRangeTint = d.ShowRangeTint;
            cfg.ShowCost = d.ShowCost;
            cfg.ShowActionName = d.ShowActionName;
            cfg.NameLines = d.NameLines;
            cfg.ShowTextOutline = d.ShowTextOutline;
            cfg.ShowButtonNumbers = d.ShowButtonNumbers;
            cfg.ShowReadyFlash = d.ShowReadyFlash;
            cfg.ShowGcdMark = d.ShowGcdMark;
            cfg.OnlyHotbarActions = d.OnlyHotbarActions;
            cfg.HighlightPressed = d.HighlightPressed;
            cfg.ShowEmptyButtons = d.ShowEmptyButtons;
            cfg.ShowSecondaryPips = d.ShowSecondaryPips;
        });

        ImGui.EndTabItem();
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
            Color("Cooldown finished", ref c.ReadyFlash);
            Color("On the global cooldown", ref c.GcdMark);
            Color("Off the global cooldown", ref c.OffGcdMark);
        }

        if (ImGui.CollapsingHeader("Text"))
        {
            Color("Action name", ref c.TextTitle);
            Color("Action name, unresolved", ref c.TextTitleDim);
            Color("Key", ref c.TextKey);
            Color("Cost", ref c.TextCost);
            Color("Charges", ref c.TextCharges);
            Color("Cooldown seconds", ref c.TextCooldown);
            Color("Button number", ref c.TextButtonNumber);
            Color("Text outline", ref c.TextOutline);
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
            + "window has focus, and it is ignored while you're typing in chat or a search box. "
            + "A neat trick: bind a spare Azeron button to F13-F24 in the Azeron app and use that "
            + "here -- the keypad then toggles its own HUD, and FFXIV ignores the key entirely.");

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        ImGui.TextUnformatted("Show the HUD");
        ImGui.SetNextItemWidth(260);
        if (ImGui.BeginCombo("##showwhen", ShowWhenName(cfg.ShowWhen)))
        {
            foreach (var v in new[]
                     {
                         CombatVisibility.Always,
                         CombatVisibility.InCombatOnly,
                         CombatVisibility.OutOfCombatOnly,
                     })
            {
                if (ImGui.Selectable(ShowWhenName(v), v == cfg.ShowWhen))
                {
                    cfg.ShowWhen = v;
                    cfg.Save();
                }
            }

            ImGui.EndCombo();
        }

        if (cfg.ShowWhen != CombatVisibility.Always)
        {
            ImGui.TextDisabled("Leave the HUD toggled on and it will come and go on its own.");

            var linger = cfg.CombatLingerSeconds;
            ImGui.SetNextItemWidth(260);
            if (ImGui.SliderFloat("Linger after combat", ref linger, 0f, 15f, "%.0f s"))
            {
                cfg.CombatLingerSeconds = linger;
                cfg.Save();
            }

            ImGui.TextDisabled("The game drops its combat flag between pulls, so a few seconds\nstops the HUD flickering while you chain packs.");
        }

        ImGui.Spacing();
        Toggle("Reopen on load if it was open", ref cfg.RememberOpen);
        Toggle("Hide during cutscenes", ref cfg.HideInCutscenes);
        Toggle("Dim when out of combat", ref cfg.DimOutOfCombat);

        using (ImRaii.Disabled(!cfg.DimOutOfCombat))
        {
            var dim = cfg.OutOfCombatOpacity;
            ImGui.SetNextItemWidth(220);
            if (ImGui.SliderFloat("Out-of-combat opacity", ref dim, 0.05f, 1f, "%.2f"))
            {
                cfg.OutOfCombatOpacity = dim;
                cfg.Save();
            }
        }

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

    private static string ShowWhenName(CombatVisibility v) => v switch
    {
        CombatVisibility.InCombatOnly => "Only in combat",
        CombatVisibility.OutOfCombatOnly => "Only out of combat",
        _ => "Always",
    };

    private static string AnchorName(LabelAnchor a) => a switch
    {
        LabelAnchor.TopLeft => "Top left",
        LabelAnchor.TopRight => "Top right",
        LabelAnchor.BottomLeft => "Bottom left",
        LabelAnchor.BottomRight => "Bottom right",
        _ => "Center",
    };

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
