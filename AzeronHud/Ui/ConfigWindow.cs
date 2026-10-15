using System;
using System.Collections.Generic;
using System.Numerics;
using AzeronHud.Azeron;
using AzeronHud.Layout;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.Keys;
using Dalamud.Interface.Windowing;

namespace AzeronHud.Ui;

internal sealed class ConfigWindow : Window
{
    private readonly Configuration cfg;
    private readonly Action reload;
    private readonly Func<IReadOnlyList<AzeronProfile>> profiles;
    private readonly Func<string> status;

    private bool capturingHotkey;

    internal ConfigWindow(
        Configuration cfg,
        Action reload,
        Func<IReadOnlyList<AzeronProfile>> profiles,
        Func<string> status)
        : base("Azeron HUD settings###AzeronHudConfig")
    {
        this.cfg = cfg;
        this.reload = reload;
        this.profiles = profiles;
        this.status = status;

        Size = new Vector2(460, 480);
        SizeCondition = ImGuiCond.FirstUseEver;
    }

    public override void Draw()
    {
        using var tabs = ImRaiiTabBar("##azhudtabs");
        if (!tabs)
            return;

        DrawProfileTab();
        DrawAppearanceTab();
        DrawContentTab();
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

        var size = cfg.CellSize;
        if (ImGui.SliderFloat("Button size", ref size, 36f, 120f, "%.0f px"))
        {
            cfg.CellSize = size;
            cfg.Save();
        }

        var gap = cfg.GapRatio * 100f;
        if (ImGui.SliderFloat("Gap", ref gap, 0f, 25f, "%.0f%%"))
        {
            cfg.GapRatio = gap / 100f;
            cfg.Save();
        }

        var alpha = cfg.BackgroundAlpha;
        if (ImGui.SliderFloat("Opacity", ref alpha, 0.2f, 1f, "%.2f"))
        {
            cfg.BackgroundAlpha = alpha;
            cfg.Save();
        }

        var rounding = cfg.Rounding;
        if (ImGui.SliderFloat("Corner rounding", ref rounding, 0f, 16f, "%.0f"))
        {
            cfg.Rounding = rounding;
            cfg.Save();
        }

        ImGui.Spacing();
        var locked = cfg.Locked;
        if (ImGui.Checkbox("Lock position (clicks pass through)", ref locked))
        {
            cfg.Locked = locked;
            cfg.Save();
        }

        ImGui.TextDisabled("While locked you can't drag the HUD, and hovering won't show tooltips.");

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
        Toggle("Highlight the key being held", ref cfg.HighlightPressed);
        Toggle("Unassigned buttons", ref cfg.ShowEmptyButtons);
        Toggle("Long-press / double-press dots", ref cfg.ShowSecondaryPips);

        ImGui.EndTabItem();
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
