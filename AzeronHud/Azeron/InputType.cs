namespace AzeronHud.Azeron;

/// <summary>
/// What an Azeron button slot does. These are the numeric values stored as
/// strings in a profile's <c>types</c> array, lifted verbatim from the
/// configurator's renderer bundle (see tools/extract_layout.py).
/// </summary>
internal enum InputType
{
    Unset = 0,
    KeyboardKey = 1,
    Switch = 2,
    AnalogJoystick = 3,
    AnalogJoystickWithKeys = 4,
    DirectInput = 5,
    Disabled = 6,
    AnalogJoystickWithKeysUp = 7,
    AnalogJoystickWithKeysRight = 8,
    AnalogJoystickWithKeysDown = 9,
    AnalogJoystickWithKeysLeft = 10,
    None = 11,
    DirectInputDpad = 12,
    ToggleAnalog = 13,
    ToggleAnalogShort = 14,
    MouseButton = 15,
    Macro = 16,
    AnalogJoystickAndWithKeysUp = 17,
    AnalogJoystickAndWithKeysRight = 18,
    AnalogJoystickAndWithKeysDown = 19,
    AnalogJoystickAndWithKeysLeft = 20,
    XInputJoystick = 21,
    XInputButton = 22,
    XInputTrigger = 23,
    SwitchProfile = 24,
    AnalogJoystickWithDriftAndKeysUp = 25,
    AnalogJoystickWithDriftAndKeysRight = 26,
    AnalogJoystickWithDriftAndKeysDown = 27,
    AnalogJoystickWithDriftAndKeysLeft = 28,
    MouseWheelUp = 29,
    MouseWheelDown = 30,
    MouseWheelTrigger = 31,
    ProfileWheelModifier = 32,
    DpiUp = 33,
    DpiDown = 34,
    ButtonMouseWheel = 35,
    AnalogWheel = 36,
    InputSequence = 37,
    MediaKeys = 38,
    XInputJoystickAndKeys = 39,
    JoystickMouse = 40,
    ProfileSwitchButton = 41,
}

internal static class InputTypeExtensions
{
    /// <summary>True when the slot emits a keystroke we can match against FFXIV's keybinds.</summary>
    internal static bool IsKeyboard(this InputType t) => t == InputType.KeyboardKey;

    /// <summary>True when nothing is assigned -- the common filler in a profile.</summary>
    internal static bool IsEmpty(this InputType t) =>
        t is InputType.Unset or InputType.None or InputType.Disabled;

    /// <summary>Short label for a slot FFXIV can't resolve, shown in place of an action name.</summary>
    internal static string Describe(this InputType t) => t switch
    {
        InputType.Switch => "Switch",
        InputType.MouseButton => "Mouse",
        InputType.Macro => "Macro",
        InputType.MediaKeys => "Media",
        InputType.MouseWheelUp => "Wheel up",
        InputType.MouseWheelDown => "Wheel down",
        InputType.MouseWheelTrigger => "Wheel",
        InputType.ButtonMouseWheel => "Wheel",
        InputType.AnalogWheel => "Wheel",
        InputType.InputSequence => "Sequence",
        InputType.SwitchProfile or InputType.ProfileSwitchButton => "Profile",
        InputType.ProfileWheelModifier => "Profile wheel",
        InputType.DpiUp => "DPI +",
        InputType.DpiDown => "DPI -",
        InputType.ToggleAnalog or InputType.ToggleAnalogShort => "Toggle stick",
        InputType.DirectInput or InputType.DirectInputDpad => "DirectInput",
        InputType.XInputButton or InputType.XInputTrigger => "XInput",
        InputType.XInputJoystick or InputType.XInputJoystickAndKeys => "Stick",
        InputType.JoystickMouse => "Stick (mouse)",
        _ when t.ToString().StartsWith("AnalogJoystick") => "Stick",
        _ => t.ToString(),
    };
}
