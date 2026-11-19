using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace KeysToPad;

/// <summary>What a key or mouse button turns into on the controller.</summary>
public enum Target
{
    None,
    A, B, X, Y, LB, RB, LT, RT, Back, Start, Guide, LeftStickClick, RightStickClick,
    DpadUp, DpadDown, DpadLeft, DpadRight,
    LeftStickUp, LeftStickDown, LeftStickLeft, LeftStickRight,
    RightStickUp, RightStickDown, RightStickLeft, RightStickRight,
    /// <summary>Held: the left stick is pushed only part of the way (walk instead of run).</summary>
    Walk,
}

public enum StickSide { None, Left, Right }

public enum MouseInput { Left, Right, Middle, Back, Forward, WheelUp, WheelDown }

/// <summary>The settings file exactly as written (settings.json beside the program).</summary>
public sealed class Settings
{
    public string ToggleHotkey { get; set; } = "Ctrl+Alt+G";
    public bool StartInControllerMode { get; set; }
    public bool Beep { get; set; } = true;
    public bool BlockAllKeys { get; set; }
    public double WalkScale { get; set; } = 0.5;
    public int WheelPulseMs { get; set; } = 60;
    public Dictionary<string, string> Keys { get; set; } = new();
    public Dictionary<string, string> MouseButtons { get; set; } = new();
    public MouseSettings Mouse { get; set; } = new();

    static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        // A misspelt setting is an error, not something quietly ignored.
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    public static Settings Parse(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<Settings>(json, Options) ?? throw new FormatException("The settings file is empty.");
        }
        catch (JsonException error)
        {
            throw new FormatException($"The settings file could not be read: {error.Message}");
        }
    }

    /// <summary>The file written on first run. Kept as text so it can carry explanations.</summary>
    public const string DefaultFile = """
{
  // KeysToPad settings. Save this file and the change applies straight away (no restart).

  // Press this to switch controller mode on and off.
  // Examples: "Ctrl+Alt+G", "ScrollLock", "F9", "Ctrl+Shift+F12".
  "toggleHotkey": "Ctrl+Alt+G",

  // true = controller mode is already on when KeysToPad starts.
  "startInControllerMode": false,

  // A short high beep when controller mode turns on, a low one when it turns off.
  "beep": true,

  // false = only the keys listed under "keys" are kept from Windows while controller mode is on.
  // true  = every key is kept from Windows (nothing reaches the game as a keyboard press).
  "blockAllKeys": false,

  // Keyboard key -> controller. Several keys may point at the same button.
  // Buttons: A B X Y LB RB LT RT Back Start Guide LeftStickClick RightStickClick
  //          DpadUp DpadDown DpadLeft DpadRight
  // Sticks:  LeftStickUp LeftStickDown LeftStickLeft LeftStickRight (and RightStick...)
  // Other:   Walk (hold it and the left stick is only pushed part of the way), None
  // Key names: letters, digits, F1-F24, Space, Enter, Tab, Esc, Backspace, CapsLock,
  //   LeftShift RightShift LeftCtrl RightCtrl LeftAlt RightAlt (or Shift, Ctrl, Alt for both sides),
  //   Up Down Left Right, Insert Delete Home End PageUp PageDown, Num0-Num9,
  //   Comma Period Slash Semicolon Quote LeftBracket RightBracket Backslash Minus Equals Backtick.
  "keys": {
    "W": "LeftStickUp",
    "S": "LeftStickDown",
    "A": "LeftStickLeft",
    "D": "LeftStickRight",
    "LeftAlt": "Walk",

    "Space": "A",
    "LeftCtrl": "B",
    "C": "B",
    "R": "X",
    "F": "Y",
    "Q": "LB",
    "E": "RB",
    "LeftShift": "LeftStickClick",
    "V": "RightStickClick",

    "Tab": "Back",
    "Esc": "Start",
    "Home": "Guide",

    "Up": "DpadUp",
    "Down": "DpadDown",
    "Left": "DpadLeft",
    "Right": "DpadRight",
    "1": "DpadUp",
    "2": "DpadRight",
    "3": "DpadDown",
    "4": "DpadLeft"
  },

  // Mouse button -> controller. Names: Left Right Middle Back Forward WheelUp WheelDown.
  "mouseButtons": {
    "Left": "RT",
    "Right": "LT",
    "Middle": "RightStickClick",
    "Back": "B",
    "Forward": "Y",
    "WheelUp": "Y",
    "WheelDown": "Y"
  },

  // How far the left stick is pushed while a Walk key is held (0.5 = half way).
  "walkScale": 0.5,

  // How long one notch of the mouse wheel holds its button, in milliseconds.
  "wheelPulseMs": 60,

  "mouse": {
    // Which stick the mouse moves: "Right", "Left" or "None" (None leaves the mouse alone).
    "stick": "Right",

    // Higher = the stick is pushed further for the same hand speed. Tune this first.
    "sensitivity": 1.0,

    // Up/down compared with left/right (0.8 = up/down is 20% slower).
    "verticalRatio": 1.0,

    // Shape of the response. 1.0 = straight. Below 1 (try 0.6) boosts small, slow movements,
    // which helps in games that make the stick sluggish near the middle.
    "curve": 1.0,

    // The game's own dead zone: the part of the stick it ignores around the middle.
    // The stick jumps past it so small mouse movements still register.
    // If the aim creeps or feels floaty, lower this; if tiny movements do nothing, raise it.
    "deadZone": 0.2,

    // Smoothing in milliseconds. Lower = more direct but jittery, higher = smoother but laggy.
    "smoothingMs": 10,

    // A stick has a top speed and a mouse does not. When you move faster than the stick can,
    // the extra movement is kept and played out for up to this long afterwards. 0 = throw it away.
    "carryOverMs": 100,

    "invertY": false
  }
}
""";
}

public sealed class MouseSettings
{
    public string Stick { get; set; } = "Right";
    public double Sensitivity { get; set; } = 1.0;
    public double VerticalRatio { get; set; } = 1.0;
    public double Curve { get; set; } = 1.0;
    public double DeadZone { get; set; } = 0.2;
    public double SmoothingMs { get; set; } = 10;
    public double CarryOverMs { get; set; } = 100;
    public bool InvertY { get; set; }
}

public sealed class Hotkey
{
    public int Key;
    public bool Ctrl, Alt, Shift, Win;
    public string Text = "";
}

/// <summary>The settings checked and turned into what the engine uses.</summary>
public sealed class Profile
{
    public Hotkey Toggle = new();
    public bool StartInControllerMode, Beep, BlockAllKeys;
    public double WalkScale, WheelPulseSeconds;
    public Dictionary<int, Target> Keys = new();
    public Target[] MouseButtons = new Target[Enum.GetValues<MouseInput>().Length];
    public StickSide MouseStick;
    public double Sensitivity, VerticalRatio, Curve, DeadZone, SmoothingMs, CarryOverMs;
    public bool InvertY;

    public static Profile From(Settings s)
    {
        var p = new Profile
        {
            Toggle = KeyNames.ParseHotkey(s.ToggleHotkey),
            StartInControllerMode = s.StartInControllerMode,
            Beep = s.Beep,
            BlockAllKeys = s.BlockAllKeys,
            WalkScale = Range("walkScale", s.WalkScale, 0.05, 1),
            WheelPulseSeconds = Range("wheelPulseMs", s.WheelPulseMs, 10, 1000) / 1000.0,
            MouseStick = ParseEnum<StickSide>("mouse.stick", s.Mouse.Stick),
            Sensitivity = Range("mouse.sensitivity", s.Mouse.Sensitivity, 0.05, 20),
            VerticalRatio = Range("mouse.verticalRatio", s.Mouse.VerticalRatio, 0.1, 5),
            Curve = Range("mouse.curve", s.Mouse.Curve, 0.2, 3),
            DeadZone = Range("mouse.deadZone", s.Mouse.DeadZone, 0, 0.9),
            SmoothingMs = Range("mouse.smoothingMs", s.Mouse.SmoothingMs, 0, 100),
            CarryOverMs = Range("mouse.carryOverMs", s.Mouse.CarryOverMs, 0, 1000),
            InvertY = s.Mouse.InvertY,
        };
        foreach (var (name, target) in s.Keys)
        {
            var to = KeyNames.ParseTarget($"keys.{name}", target);
            foreach (int key in KeyNames.ParseKey(name)) p.Keys[key] = to;
        }
        foreach (var (name, target) in s.MouseButtons)
        {
            var button = ParseEnum<MouseInput>("mouseButtons", name);
            p.MouseButtons[(int)button] = KeyNames.ParseTarget($"mouseButtons.{name}", target);
        }
        if (p.Keys.TryGetValue(p.Toggle.Key, out _) && !(p.Toggle.Ctrl || p.Toggle.Alt || p.Toggle.Shift || p.Toggle.Win))
            throw new FormatException($"\"{s.ToggleHotkey}\" is the on/off shortcut and is also listed under \"keys\". Use a different key for one of them.");
        return p;
    }

    static double Range(string name, double value, double low, double high)
    {
        if (double.IsNaN(value) || value < low || value > high)
            throw new FormatException(string.Create(CultureInfo.InvariantCulture, $"\"{name}\" must be between {low} and {high} (it is {value})."));
        return value;
    }

    static T ParseEnum<T>(string where, string text) where T : struct, Enum
    {
        if (Enum.TryParse<T>(text?.Trim(), ignoreCase: true, out var value) && Enum.IsDefined(value) && !int.TryParse(text, out _)) return value;
        throw new FormatException($"\"{text}\" is not valid for \"{where}\". Use one of: {string.Join(", ", Enum.GetNames<T>())}.");
    }
}

public static class KeyNames
{
    static readonly Dictionary<string, int[]> Named = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Space"] = [0x20], ["Enter"] = [0x0D], ["Return"] = [0x0D], ["Tab"] = [0x09], ["Esc"] = [0x1B], ["Escape"] = [0x1B],
        ["Backspace"] = [0x08], ["CapsLock"] = [0x14], ["ScrollLock"] = [0x91], ["Pause"] = [0x13], ["PrintScreen"] = [0x2C],
        ["LeftShift"] = [0xA0], ["LShift"] = [0xA0], ["RightShift"] = [0xA1], ["RShift"] = [0xA1], ["Shift"] = [0xA0, 0xA1],
        ["LeftCtrl"] = [0xA2], ["LCtrl"] = [0xA2], ["RightCtrl"] = [0xA3], ["RCtrl"] = [0xA3], ["Ctrl"] = [0xA2, 0xA3], ["Control"] = [0xA2, 0xA3],
        ["LeftAlt"] = [0xA4], ["LAlt"] = [0xA4], ["RightAlt"] = [0xA5], ["RAlt"] = [0xA5], ["Alt"] = [0xA4, 0xA5],
        ["LeftWin"] = [0x5B], ["RightWin"] = [0x5C], ["Win"] = [0x5B, 0x5C], ["Menu"] = [0x5D],
        ["Up"] = [0x26], ["Down"] = [0x28], ["Left"] = [0x25], ["Right"] = [0x27],
        ["Insert"] = [0x2D], ["Delete"] = [0x2E], ["Home"] = [0x24], ["End"] = [0x23], ["PageUp"] = [0x21], ["PageDown"] = [0x22],
        ["NumAdd"] = [0x6B], ["NumSubtract"] = [0x6D], ["NumMultiply"] = [0x6A], ["NumDivide"] = [0x6F], ["NumDecimal"] = [0x6E],
        ["Semicolon"] = [0xBA], ["Equals"] = [0xBB], ["Comma"] = [0xBC], ["Minus"] = [0xBD], ["Period"] = [0xBE], ["Slash"] = [0xBF],
        ["Backtick"] = [0xC0], ["Tilde"] = [0xC0], ["LeftBracket"] = [0xDB], ["Backslash"] = [0xDC], ["RightBracket"] = [0xDD], ["Quote"] = [0xDE],
    };

    static readonly Dictionary<string, Target> TargetAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["LS"] = Target.LeftStickClick, ["L3"] = Target.LeftStickClick, ["RS"] = Target.RightStickClick, ["R3"] = Target.RightStickClick,
        ["View"] = Target.Back, ["Select"] = Target.Back, ["Menu"] = Target.Start,
        ["LeftBumper"] = Target.LB, ["RightBumper"] = Target.RB, ["LeftTrigger"] = Target.LT, ["RightTrigger"] = Target.RT,
        ["Up"] = Target.DpadUp, ["Down"] = Target.DpadDown, ["Left"] = Target.DpadLeft, ["Right"] = Target.DpadRight,
    };

    /// <summary>A key name to its Windows key code(s). "Shift" gives both sides.</summary>
    public static int[] ParseKey(string name)
    {
        string text = (name ?? "").Trim();
        if (text.Length == 1)
        {
            char c = char.ToUpperInvariant(text[0]);
            if (c is >= 'A' and <= 'Z' or >= '0' and <= '9') return [c];
        }
        if (Named.TryGetValue(text, out var codes)) return codes;
        if (text.Length is 2 or 3 && (text[0] is 'F' or 'f') && int.TryParse(text.AsSpan(1), out int f) && f is >= 1 and <= 24) return [0x70 + f - 1];
        if (text.StartsWith("Num", StringComparison.OrdinalIgnoreCase) && text.Length == 4 && char.IsAsciiDigit(text[3])) return [0x60 + (text[3] - '0')];
        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase) && int.TryParse(text.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int raw) && raw is > 0 and < 256) return [raw];
        throw new FormatException($"\"{name}\" is not a key name KeysToPad knows. Examples: W, 1, F5, Space, LeftShift, Up, Num0.");
    }

    public static Target ParseTarget(string where, string text)
    {
        string value = (text ?? "").Trim();
        if (TargetAliases.TryGetValue(value, out var alias)) return alias;
        if (!int.TryParse(value, out _) && Enum.TryParse<Target>(value, ignoreCase: true, out var target) && Enum.IsDefined(target)) return target;
        throw new FormatException($"\"{text}\" (for \"{where}\") is not a controller button. Use one of: {string.Join(", ", Enum.GetNames<Target>())}.");
    }

    public static Hotkey ParseHotkey(string text)
    {
        var hotkey = new Hotkey { Text = (text ?? "").Trim() };
        var parts = hotkey.Text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0) throw new FormatException("\"toggleHotkey\" is empty. Example: \"Ctrl+Alt+G\".");
        for (int i = 0; i < parts.Length - 1; i++)
        {
            switch (parts[i].ToLowerInvariant())
            {
                case "ctrl" or "control": hotkey.Ctrl = true; break;
                case "alt": hotkey.Alt = true; break;
                case "shift": hotkey.Shift = true; break;
                case "win": hotkey.Win = true; break;
                default: throw new FormatException($"\"{parts[i]}\" in \"toggleHotkey\" is not Ctrl, Alt, Shift or Win.");
            }
        }
        var keys = ParseKey(parts[^1]);
        if (keys.Length != 1) throw new FormatException($"\"toggleHotkey\" must end with one key (for example \"Ctrl+Alt+G\"), not \"{parts[^1]}\".");
        hotkey.Key = keys[0];
        return hotkey;
    }
}
