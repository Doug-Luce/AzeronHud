using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using AzeronHud.Azeron;
using AzeronHud.Game;
using AzeronHud.Layout;
using AzeronHud.Ui;
using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;

namespace AzeronHud;

public sealed class Plugin : IDalamudPlugin
{
    private const string Command = "/azeronhud";
    private const string ShortCommand = "/azhud";

    /// <summary>How often to re-stat the profile files looking for an edit.</summary>
    private static readonly TimeSpan ProfilePollInterval = TimeSpan.FromSeconds(2);

    /// <summary>How often to re-read hotbar contents (job swaps, page changes, upgrades).</summary>
    private static readonly TimeSpan StructurePollInterval = TimeSpan.FromMilliseconds(250);

    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static IClientState ClientState { get; private set; } = null!;
    [PluginService] internal static ICondition Condition { get; private set; } = null!;
    [PluginService] internal static IFramework Framework { get; private set; } = null!;
    [PluginService] internal static IDataManager Data { get; private set; } = null!;
    [PluginService] internal static ICommandManager Commands { get; private set; } = null!;
    [PluginService] internal static IChatGui Chat { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;
    [PluginService] internal static ITextureProvider Textures { get; private set; } = null!;
    [PluginService] internal static IKeyState KeyState { get; private set; } = null!;
    [PluginService] internal static IGamepadState Gamepad { get; private set; } = null!;

    private readonly Configuration cfg;
    private readonly WindowSystem windows = new("AzeronHud");
    private readonly OverlayWindow overlay;
    private readonly ConfigWindow configWindow;

    private readonly KeybindIndex keybinds = new();
    private readonly ButtonResolver resolver;

    private ProfileStore store;
    private IReadOnlyList<AzeronProfile> discovered = Array.Empty<AzeronProfile>();
    private AzeronProfile? active;
    private DeviceLayout? layout;
    private string statusText = "Not loaded yet.";

    private DateTime lastStructurePoll = DateTime.MinValue;
    private bool keybindsBuilt;
    private bool hotkeyWasDown;

    /// <summary>Set after a resolution failure so we don't throw every frame.</summary>
    private bool resolutionDisabled;

    public Plugin()
    {
        cfg = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        store = new ProfileStore(cfg.AzeronRoot);
        resolver = new ButtonResolver(keybinds);

        overlay = new OverlayWindow(cfg, () => layout, () => resolver, () => statusText);
        configWindow = new ConfigWindow(cfg, Reload, () => discovered, () => statusText);
        windows.AddWindow(overlay);
        windows.AddWindow(configWindow);

        PluginInterface.UiBuilder.Draw += windows.Draw;
        PluginInterface.UiBuilder.OpenConfigUi += ToggleConfig;
        PluginInterface.UiBuilder.OpenMainUi += ToggleOverlay;

        Commands.AddHandler(Command, new CommandInfo(OnCommand)
        {
            HelpMessage = "Toggle the Azeron keypad HUD. Also: config, reload, dump.",
        });
        Commands.AddHandler(ShortCommand, new CommandInfo(OnCommand)
        {
            HelpMessage = "Alias for /azeronhud.",
        });

        Framework.Update += OnUpdate;
        ClientState.Login += OnLogin;

        Reload();
    }

    // --- loading -----------------------------------------------------------

    private void Reload()
    {
        try
        {
            store = new ProfileStore(cfg.AzeronRoot);
            layout = DeviceLayout.ForProductId(cfg.ProductId);

            if (!store.Exists)
            {
                discovered = Array.Empty<AzeronProfile>();
                active = null;
                resolver.Rebuild(null, null);
                statusText = $"Azeron Software config not found at {store.Root}";
                return;
            }

            discovered = store.LoadAll(cfg.ProductId);
            if (discovered.Count == 0)
            {
                active = null;
                resolver.Rebuild(null, null);
                statusText = $"No profiles for device {cfg.ProductId} under {store.Root}";
                return;
            }

            active = cfg.ProfileId.Length > 0
                ? discovered.FirstOrDefault(p => p.Id == cfg.ProfileId) ?? discovered[0]
                : discovered[0];

            if (layout == null)
            {
                statusText = $"No layout bundled for device {cfg.ProductId}.";
                return;
            }

            RebuildKeybinds();
            resolver.Rebuild(active, layout);

            var present = active.Inputs.Count(i => i.IsPresent);
            statusText =
                $"\"{active.Name}\" on {layout.Name}: {present} buttons, "
                + $"{keybinds.BoundCount} FFXIV keybinds indexed.";
            Log.Information(statusText);
        }
        catch (Exception ex)
        {
            statusText = $"Failed to load: {ex.Message}";
            Log.Error(ex, "Azeron HUD reload failed");
        }
    }

    private void RebuildKeybinds()
    {
        if (!ClientState.IsLoggedIn)
        {
            keybindsBuilt = false;
            return;
        }

        keybinds.Rebuild();
        keybinds.LoadNames(Data.GetExcelSheet<Lumina.Excel.Sheets.ConfigKey>());
        keybindsBuilt = true;

        Log.Debug($"Indexed {keybinds.BoundCount} keybinds, {keybinds.NamedCount} with display names.");
    }

    private void OnLogin()
    {
        keybindsBuilt = false;
        resolutionDisabled = false;
    }

    // --- per-frame ----------------------------------------------------------

    private void OnUpdate(IFramework framework)
    {
        try
        {
            PollHotkey();

            if (!overlay.IsOpen || resolutionDisabled || !ClientState.IsLoggedIn)
                return;

            if (!keybindsBuilt)
            {
                RebuildKeybinds();
                if (keybindsBuilt)
                    resolver.Rebuild(active, layout);
            }

            // Picks up edits made in the Azeron app while the game is running.
            if (store.HasChanged(cfg.ProductId, ProfilePollInterval))
                Reload();

            var now = DateTime.UtcNow;
            if (now - lastStructurePoll >= StructurePollInterval)
            {
                lastStructurePoll = now;
                if (resolver.StructureLooksStale())
                    resolver.Rebuild(active, layout);
            }

            resolver.RefreshVolatile();
        }
        catch (Exception ex)
        {
            // Once, then stop -- a throw every frame would bury the log and the
            // game's own UI thread does not want our exceptions.
            resolutionDisabled = true;
            Log.Error(ex, "Azeron HUD update failed; resolution disabled until reload");
        }
    }

    private void PollHotkey()
    {
        if (cfg.HotkeyVk == 0)
        {
            hotkeyWasDown = false;
            return;
        }

        var key = (Dalamud.Game.ClientState.Keys.VirtualKey)cfg.HotkeyVk;
        if (!KeyState.IsVirtualKeyValid(key))
            return;

        var down = KeyState[key] && ModifiersHeld();
        if (down && !hotkeyWasDown)
            ToggleOverlay();

        hotkeyWasDown = down;
    }

    private bool ModifiersHeld()
    {
        var k = Dalamud.Game.ClientState.Keys.VirtualKey.SHIFT;
        if (cfg.HotkeyShift != KeyState[k])
            return false;

        k = Dalamud.Game.ClientState.Keys.VirtualKey.CONTROL;
        if (cfg.HotkeyCtrl != KeyState[k])
            return false;

        k = Dalamud.Game.ClientState.Keys.VirtualKey.MENU;
        return cfg.HotkeyAlt == KeyState[k];
    }

    // --- commands ------------------------------------------------------------

    private void OnCommand(string command, string args)
    {
        switch (args.Trim().ToLowerInvariant())
        {
            case "":
                ToggleOverlay();
                break;
            case "config" or "settings":
                ToggleConfig();
                break;
            case "reload":
                resolutionDisabled = false;
                keybindsBuilt = false;
                Reload();
                Chat.Print($"[Azeron HUD] {statusText}");
                break;
            case "lock":
                cfg.Locked = !cfg.Locked;
                cfg.Save();
                Chat.Print($"[Azeron HUD] {(cfg.Locked ? "locked" : "unlocked")}.");
                break;
            case "dump":
                Dump();
                break;
            default:
                Chat.Print("[Azeron HUD] Usage: /azeronhud [config|reload|lock|dump]");
                break;
        }
    }

    /// <summary>
    /// Print the whole resolution chain to the log. Every claim the HUD makes
    /// should be checkable as text before anyone squints at pixels.
    /// </summary>
    private void Dump()
    {
        if (active == null)
        {
            Chat.Print($"[Azeron HUD] {statusText}");
            return;
        }

        var sb = new StringBuilder();
        sb.AppendLine($"=== Azeron HUD dump: \"{active.Name}\" ({active.SourcePath}) ===");
        sb.AppendLine($"device {cfg.ProductId}, layout {layout?.Name ?? "<none>"}, "
                      + $"{keybinds.BoundCount} keybinds, {keybinds.NamedCount} named");
        sb.AppendLine("id   type            key          FFXIV keybind        resolved");

        foreach (var input in active.Inputs.Where(i => i.IsPresent).OrderBy(i => i.Id))
        {
            resolver.Buttons.TryGetValue(input.Id, out var b);
            sb.AppendLine(string.Format(
                "{0,-4} {1,-15} {2,-12} {3,-20} {4} [{5}]",
                input.Id,
                input.TypeAt(PressKind.Single),
                b?.Chord ?? string.Empty,
                b?.InputIdName ?? string.Empty,
                b?.Title ?? string.Empty,
                b?.Tier.ToString() ?? "?"));
        }

        Log.Information(sb.ToString());
        Chat.Print($"[Azeron HUD] Dumped {resolver.Buttons.Count} buttons to the Dalamud log.");
    }

    private void ToggleOverlay() => overlay.IsOpen = !overlay.IsOpen;

    private void ToggleConfig() => configWindow.IsOpen = !configWindow.IsOpen;

    public void Dispose()
    {
        ClientState.Login -= OnLogin;
        Framework.Update -= OnUpdate;
        Commands.RemoveHandler(ShortCommand);
        Commands.RemoveHandler(Command);
        PluginInterface.UiBuilder.OpenMainUi -= ToggleOverlay;
        PluginInterface.UiBuilder.OpenConfigUi -= ToggleConfig;
        PluginInterface.UiBuilder.Draw -= windows.Draw;
        windows.RemoveAllWindows();
    }
}
