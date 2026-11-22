#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace RemotePad;

internal enum PadControl
{
    MoveUp, MoveDown, MoveLeft, MoveRight, LookUp, LookDown, LookLeft, LookRight,
    DpadUp, DpadDown, DpadLeft, DpadRight, A, B, X, Y, LB, RB, LT, RT, L3, R3, View, Menu, Guide, Pause
}

internal sealed class ControlLayout
{
    public static readonly PadControl[] Actions = Enum.GetValues<PadControl>();
    private readonly Key[] keys;
    private readonly Dictionary<Key, PadControl> bindings;

    public ControlLayout(IEnumerable<Key> keys)
    {
        this.keys = keys.Select(Normalize).ToArray();
        if (this.keys.Length != Actions.Length || this.keys.Any(key => !IsBindable(key))
            || this.keys.Distinct().Count() != Actions.Length)
            throw new ArgumentException("Раскладка должна содержать по одной уникальной клавише для каждого действия.");
        if (IsMouse(this[PadControl.Pause])) throw new ArgumentException("Для паузы назначь клавишу с клавиатуры.");
        bindings = Actions.ToDictionary(action => this[action]);
    }

    public Key this[PadControl control] => keys[(int)control];
    public bool TryGetAction(Key key, out PadControl control) => bindings.TryGetValue(Normalize(key), out control);

    public static Key Normalize(Key key) => (int)key switch
    {
        0xA0 or 0xA1 => (Key)0x10,
        0xA2 or 0xA3 => (Key)0x11,
        0xA4 or 0xA5 => (Key)0x12,
        _ => key
    };

    public static bool IsMouse(Key key) =>
        key is Key.MouseLeft or Key.MouseRight or Key.MouseMiddle or Key.MouseX1 or Key.MouseX2;

    public static bool IsBindable(Key key) => IsMouse(key) || (int)key is >= 0x08 and <= 0xFE
        and not (0x5B or 0x5C or 0x5D or 0xE5 or 0xE7);

    public ControlLayout WithKey(PadControl control, Key newKey)
    {
        newKey = Normalize(newKey);
        if (!IsBindable(newKey)) throw new ArgumentException("Эту клавишу нельзя назначить.");
        var copy = (Key[])keys.Clone();
        if (TryGetAction(newKey, out PadControl other)) copy[(int)other] = this[control];
        copy[(int)control] = newKey;
        return new ControlLayout(copy);
    }

    public static ControlLayout Classic() => new(new[]
    {
        Key.W, Key.S, Key.A, Key.D, Key.I, Key.K, Key.J, Key.L,
        Key.Up, Key.Down, Key.Left, Key.Right, Key.Space, Key.Control, Key.F, Key.E,
        Key.Q, Key.R, Key.Z, Key.X, Key.Shift, Key.C, Key.Tab, Key.Enter, Key.Home, Key.F8
    });

    public static ControlLayout Standard() => new(new[]
    {
        Key.W, Key.S, Key.A, Key.D, Key.Up, Key.Down, Key.Left, Key.Right,
        Key.I, Key.K, Key.J, Key.L, Key.Space, Key.X, Key.F, Key.R,
        Key.Q, Key.E, Key.MouseLeft, Key.MouseRight, Key.Shift, Key.MouseMiddle,
        Key.Tab, Key.Enter, Key.Home, Key.F8
    });
}

internal sealed record ControlSettings(ControlLayout Layout, bool MouseCamera, int Sensitivity, bool InvertY)
{
    public static ControlSettings Default() => new(ControlLayout.Standard(), true, 100, false);
}

internal static class ControlSettingsFile
{
    public static string Pathname => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "RemotePad", "controls.json");

    private sealed record StoredSettings(Dictionary<string, int> Keys, bool MouseCamera, int Sensitivity, bool InvertY);

    public static ControlSettings Load(string path)
    {
        if (!File.Exists(path)) return ControlSettings.Default();
        var file = JsonSerializer.Deserialize<StoredSettings>(File.ReadAllText(path));
        if (file?.Keys == null || file.Sensitivity is < 10 or > 300
            || !ControlLayout.Actions.All(action => file.Keys.ContainsKey(action.ToString())))
            throw new InvalidDataException("В файле настроек неполная раскладка или неверная чувствительность.");
        return new ControlSettings(new ControlLayout(ControlLayout.Actions.Select(action => (Key)file.Keys[action.ToString()])),
            file.MouseCamera, file.Sensitivity, file.InvertY);
    }

    public static void Save(string path, ControlSettings settings)
    {
        string directory = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(directory);
        string temporary = Path.Combine(directory, Path.GetRandomFileName());
        var file = new StoredSettings(ControlLayout.Actions.ToDictionary(action => action.ToString(),
            action => (int)settings.Layout[action]), settings.MouseCamera, settings.Sensitivity, settings.InvertY);
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(file, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            File.Delete(temporary);
        }
    }
}
