using System.Collections.Generic;
using Newtonsoft.Json;

namespace AzeronHud.Azeron;

/// <summary>
/// The subset of <c>azeron-profile-v1</c> we care about. The real file carries a
/// lot more (LEDs, macros, turbo, per-slot analog settings); anything not named
/// here is ignored by Json.NET.
/// </summary>
internal sealed class AzeronProfile
{
    [JsonProperty("id")] public string Id = string.Empty;
    [JsonProperty("name")] public string Name = string.Empty;
    [JsonProperty("version")] public int Version;
    [JsonProperty("metaData")] public ProfileMeta? MetaData;
    [JsonProperty("inputs")] public List<AzeronInput> Inputs = new();

    /// <summary>Where this profile came from. Not part of the file.</summary>
    [JsonIgnore] public string SourcePath = string.Empty;

    [JsonIgnore] public bool LooksValid =>
        Version == 1 && MetaData?.SchemeName == "azeron-profile-v1" && Inputs.Count > 0;
}

internal sealed class ProfileMeta
{
    [JsonProperty("schemeName")] public string SchemeName = string.Empty;

    /// <summary>KeypadType -- 8 is the Cyborg II. Matches the layout files' keypadType.</summary>
    [JsonProperty("device")] public int Device;

    [JsonProperty("createdBy")] public string CreatedBy = string.Empty;
}

/// <summary>
/// One physical button. <c>id</c> is the stable index the layout grid is keyed
/// on; <c>pinOne</c> is the firmware matrix pin, and 255 means the slot exists in
/// the schema but not on this model.
/// </summary>
internal sealed class AzeronInput
{
    [JsonProperty("id")] public int Id;
    [JsonProperty("pinOne")] public int PinOne = 255;
    [JsonProperty("pinTwo")] public int PinTwo = 255;
    [JsonProperty("label")] public string? Label;

    /// <summary>[normal, long-press, double-press], as decimal strings.</summary>
    [JsonProperty("types")] public List<string> Types = new();

    [JsonProperty("keyValues")] public List<string> KeyValues = new();
    [JsonProperty("metaValues")] public List<string> MetaValues = new();
    [JsonProperty("keyValuesLong")] public List<string> KeyValuesLong = new();
    [JsonProperty("metaValuesLong")] public List<string> MetaValuesLong = new();
    [JsonProperty("keyValuesDouble")] public List<string> KeyValuesDouble = new();
    [JsonProperty("metaValuesDouble")] public List<string> MetaValuesDouble = new();

    /// <summary>True when the button physically exists on this keypad.</summary>
    [JsonIgnore] public bool IsPresent => PinOne != 255 || PinTwo != 255;

    internal InputType TypeAt(PressKind kind)
    {
        var i = (int)kind;
        return i < Types.Count && int.TryParse(Types[i], out var v) ? (InputType)v : InputType.Unset;
    }

    internal (List<string> Keys, List<string> Mods) ValuesAt(PressKind kind) => kind switch
    {
        PressKind.Long => (KeyValuesLong, MetaValuesLong),
        PressKind.Double => (KeyValuesDouble, MetaValuesDouble),
        _ => (KeyValues, MetaValues),
    };
}

internal enum PressKind
{
    Single = 0,
    Long = 1,
    Double = 2,
}
