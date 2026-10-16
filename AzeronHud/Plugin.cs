using System;
using System.Collections.Generic;
using System.IO;
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

    /// <summary>
    /// How often to check whether the keybind table changed. Rebinding mid-session
    /// is rare, and the check is a hash over a few hundred entries, so this can be
    /// slow without the HUD ever being noticeably wrong.
    /// </summary>
    private static readonly TimeSpan KeybindPollInterval = TimeSpan.FromSeconds(3);

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
    private DateTime lastKeybindPoll = DateTime.MinValue;
    private ulong keybindSignature;
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
            HelpMessage = "Toggle the Azeron keypad HUD. Also: config, reload, lock, dump.",
        });
        Commands.AddHandler(ShortCommand, new CommandInfo(OnCommand)
        {
            HelpMessage = "Alias for /azeronhud.",
        });

        Framework.Update += OnUpdate;
        ClientState.Login += OnLogin;
        ClientState.Logout += OnLogout;

        // Changing job reloads every hotbar, so the icons, names and even which
        // buttons resolve at all are different afterwards.
        ClientState.ClassJobChanged += OnClassJobChanged;

        Reload();

        // Reopen if it was up when the game last closed. Done after Reload so
        // the first frame has something to draw.
        if (cfg.RememberOpen && cfg.WasOpen)
            overlay.IsOpen = true;
    }

    // --- loading -----------------------------------------------------------

    private void Reload()
    {
        try
        {
            store = new ProfileStore(cfg.AzeronRoot);
            var productId = PickDevice();
            layout = DeviceLayout.ForProductId(productId);

            if (!store.Exists)
            {
                discovered = Array.Empty<AzeronProfile>();
                active = null;
                resolver.Rebuild(null, null);
                statusText = $"Azeron Software config not found at {store.Root}";
                return;
            }

            discovered = store.LoadAll(productId);
            if (discovered.Count == 0)
            {
                active = null;
                resolver.Rebuild(null, null);
                statusText = $"No profiles for device {productId} under {store.Root}";
                return;
            }

            active = cfg.ProfileId.Length > 0
                ? discovered.FirstOrDefault(p => p.Id == cfg.ProfileId) ?? discovered[0]
                : discovered[0];

            if (layout == null)
            {
                statusText = $"No layout bundled for device {productId}.";
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

    /// <summary>
    /// Which keypad to show.
    ///
    /// The configured id is the authority when the user has turned auto-detect
    /// off, or when it matches something on disk. Otherwise prefer whichever
    /// device directory has a layout bundled and the most recently saved
    /// profile -- which is almost always "the one keypad they own".
    /// </summary>
    private int PickDevice()
    {
        var devices = store.Devices().ToList();
        if (devices.Count == 0)
            return cfg.ProductId;

        if (!cfg.AutoDetectDevice || devices.Contains(cfg.ProductId))
            return cfg.ProductId;

        var known = devices.Where(d => DeviceLayout.All.ContainsKey(d)).ToList();
        var candidates = known.Count > 0 ? known : devices;

        var picked = candidates
            .OrderByDescending(d => store.ProfilePaths(d)
                .Select(File.GetLastWriteTimeUtc)
                .DefaultIfEmpty(DateTime.MinValue)
                .Max())
            .First();

        if (picked != cfg.ProductId)
        {
            Log.Information($"Azeron device {cfg.ProductId} has no profiles; using {picked}.");
            cfg.ProductId = picked;
            cfg.Save();
        }

        return picked;
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
        keybindSignature = keybinds.Signature();
        keybindsBuilt = true;

        Log.Debug($"Indexed {keybinds.BoundCount} keybinds, {keybinds.NamedCount} with display names.");
    }

    private void OnLogin()
    {
        keybindsBuilt = false;
        resolutionDisabled = false;
    }

    private void OnLogout(int type, int code) => keybindsBuilt = false;

    private void OnClassJobChanged(uint classJobId)
    {
        if (ClientState.IsLoggedIn)
            resolver.Rebuild(active, layout);
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

            // A rebind in game changes which FFXIV action a button maps to, and
            // nothing else would ever tell us.
            if (now - lastKeybindPoll >= KeybindPollInterval)
            {
                lastKeybindPoll = now;
                var signature = keybinds.Signature();
                if (signature != 0 && signature != keybindSignature)
                {
                    RebuildKeybinds();
                    resolver.Rebuild(active, layout);
                }
            }

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
            statusText = $"Stopped updating after an error: {ex.Message}. /azeronhud reload to retry.";
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

        // IKeyState reads the game's key array, which the game keeps filling
        // while you type in chat or a search box -- so without this, a hotkey
        // of M toggles the HUD every time you type the letter M to someone.
        var down = KeyState[key] && ModifiersHeld() && !IsTypingInGame();
        if (down && !hotkeyWasDown)
            ToggleOverlay();

        hotkeyWasDown = down;
    }

    private static unsafe bool IsTypingInGame()
    {
        var atk = FFXIVClientStructs.FFXIV.Client.UI.RaptureAtkModule.Instance();
        return atk != null && atk->AtkModule.IsTextInputActive();
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
        ClientState.ClassJobChanged -= OnClassJobChanged;
        ClientState.Logout -= OnLogout;
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
