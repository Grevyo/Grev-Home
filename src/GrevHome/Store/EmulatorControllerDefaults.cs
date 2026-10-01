using System.Text;

namespace GrevHome.Store;

/// <summary>
/// Native controller defaults for the standalone emulators, so a game launched from Grev Home is
/// playable on an XInput/SDL pad without opening the emulator's own settings first.
///
/// Every file uses the emulator's own documented format (sources in docs/STANDALONE_EMULATORS.md).
/// A default is only written when the emulator has no controller mapping of its own yet: a missing
/// file, or one still holding keyboard/null defaults. Anything a person mapped is never replaced.
///
/// Xenia (XInput), xemu (input.auto_bind) and Vita3K (SDL game controller) bind pads natively and
/// need nothing here. Dolphin's GameCube pad is written by StandaloneEmulatorCatalog.
/// </summary>
public static class EmulatorControllerDefaults
{
    private static readonly string NewLine = Environment.NewLine;

    public static void Apply(string appId, string binaryRoot, string dataRoot)
    {
        switch (appId)
        {
            case "dolphin":
                WriteUnlessMapped(Path.Combine(dataRoot, "Config", "WiimoteNew.ini"), DolphinWiimote(),
                    existing => existing.Contains("XInput/", StringComparison.OrdinalIgnoreCase) ||
                                existing.Contains("SDL/", StringComparison.OrdinalIgnoreCase));
                break;
            case "azahar":
                ApplyAzahar(Path.Combine(binaryRoot, "user", "config", "qt-config.ini"));
                break;
            case "rpcs3":
                WriteUnlessMapped(Path.Combine(binaryRoot, "config", "input_configs", "global", "Default.yml"), Rpcs3Input(),
                    existing => !Rpcs3PlayerOneIsUnbound(existing));
                break;
            case "cemu":
                WriteUnlessMapped(Path.Combine(binaryRoot, "controllerProfiles", "controller0.xml"), CemuGamePad(),
                    _ => true);
                break;
        }
    }

    // Dolphin: Wii Remote + Nunchuk on one pad. Pointer on the right stick, shake on B.
    // Key names follow Dolphin's WiimoteNew.ini; device expressions match GCPadNew.ini's.
    internal static string DolphinWiimote() => string.Join(NewLine,
        "[Wiimote1]",
        "Device = XInput/0/Gamepad",
        "Source = 1",
        "Buttons/A = `Button A`",
        "Buttons/B = `Trigger R`",
        "Buttons/1 = `Button X`",
        "Buttons/2 = `Button Y`",
        "Buttons/- = `Back`",
        "Buttons/+ = `Start`",
        "Buttons/Home = `Thumb R`",
        "D-Pad/Up = `Pad N`",
        "D-Pad/Down = `Pad S`",
        "D-Pad/Left = `Pad W`",
        "D-Pad/Right = `Pad E`",
        "IR/Up = `Right Y+`",
        "IR/Down = `Right Y-`",
        "IR/Left = `Right X-`",
        "IR/Right = `Right X+`",
        "Shake/X = `Button B`",
        "Shake/Y = `Button B`",
        "Shake/Z = `Button B`",
        "Extension = Nunchuk",
        "Nunchuk/Buttons/C = `Shoulder L`",
        "Nunchuk/Buttons/Z = `Trigger L`",
        "Nunchuk/Stick/Up = `Left Y+`",
        "Nunchuk/Stick/Down = `Left Y-`",
        "Nunchuk/Stick/Left = `Left X-`",
        "Nunchuk/Stick/Right = `Left X+`",
        "Nunchuk/Shake/X = `Thumb L`",
        "Nunchuk/Shake/Y = `Thumb L`",
        "Nunchuk/Shake/Z = `Thumb L`",
        "");

    // RPCS3 fills the full XInput mapping itself (pad_thread::InitPadConfig -> init_config) when
    // a player's handler is XInput, so only the handler and device need to be declared.
    internal static string Rpcs3Input() => string.Join(NewLine,
        "Player 1 Input:",
        "  Handler: XInput",
        "  Device: XInput Pad #1",
        "");

    internal static bool Rpcs3PlayerOneIsUnbound(string yaml)
    {
        var lines = yaml.Split('\n').Select(line => line.TrimEnd('\r')).ToArray();
        var player = Array.FindIndex(lines, line => line.Trim() == "Player 1 Input:");
        if (player < 0) return true;
        for (var index = player + 1; index < lines.Length && (lines[index].StartsWith(' ') || lines[index].Length == 0); index++)
        {
            var trimmed = lines[index].Trim();
            if (trimmed.StartsWith("Handler:", StringComparison.Ordinal))
            {
                var handler = trimmed["Handler:".Length..].Trim().Trim('"');
                return handler.Length == 0 || handler.Equals("Null", StringComparison.OrdinalIgnoreCase);
            }
        }
        return true;
    }

    // Cemu: a Wii U GamePad on XInput user 0, using Cemu's own XInput default mapping
    // (VPADController::set_default_mapping). Numbers are VPADController::ButtonId and
    // Controller Buttons2 values, the format InputManager writes to controllerN.xml.
    private static readonly (int Mapping, int Button)[] CemuXInputMapping =
    [
        (1, 13), (2, 12), (3, 15), (4, 14),   // A B X Y
        (5, 8), (6, 9), (7, 42), (8, 43),     // L R ZL ZR
        (9, 4), (10, 5),                      // Plus Minus
        (11, 0), (12, 1), (13, 2), (14, 3),   // D-pad
        (15, 6), (16, 7),                     // Stick clicks
        (17, 39), (18, 45), (19, 44), (20, 38), // Left stick
        (21, 41), (22, 47), (23, 46), (24, 40)  // Right stick
    ];

    internal static string CemuGamePad()
    {
        var builder = new StringBuilder()
            .AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>")
            .AppendLine("<emulated_controller>")
            .AppendLine("\t<type>Wii U GamePad</type>")
            .AppendLine("\t<controller>")
            .AppendLine("\t\t<api>XInput</api>")
            .AppendLine("\t\t<uuid>0</uuid>")
            .AppendLine("\t\t<display_name>Controller 1</display_name>")
            .AppendLine("\t\t<rumble>0.5</rumble>")
            .AppendLine("\t\t<axis><deadzone>0.25</deadzone><range>1</range></axis>")
            .AppendLine("\t\t<rotation><deadzone>0.25</deadzone><range>1</range></rotation>")
            .AppendLine("\t\t<trigger><deadzone>0.25</deadzone><range>1</range></trigger>")
            .AppendLine("\t\t<mappings>");
        foreach (var (mapping, button) in CemuXInputMapping)
        {
            builder.AppendLine($"\t\t\t<entry><mapping>{mapping}</mapping><button>{button}</button></entry>");
        }
        return builder
            .AppendLine("\t\t</mappings>")
            .AppendLine("\t</controller>")
            .AppendLine("</emulated_controller>")
            .ToString();
    }

    // Azahar: SDL game controller bindings with no GUID, which Azahar resolves to whichever pad is
    // connected. Button numbers are SDL_GameControllerButton values in Azahar's own
    // xinput_to_3ds_mapping order (3DS A = SDL B, etc.); triggers and sticks are SDL axes.
    internal static string AzaharControls()
    {
        static string Button(int button) => $"\"engine:sdl,api:controller,guid:0,port:0,button:{button}\"";
        static string Trigger(int axis) => $"\"engine:sdl,api:controller,guid:0,port:0,axis:{axis},direction:+,threshold:0.500000\"";
        static string Stick(int x, int y) => $"\"engine:sdl,api:controller,guid:0,port:0,axis_x:{x},axis_y:{y},deadzone:0.100000\"";
        return string.Join(NewLine,
            "[Controls]",
            "profile=0",
            "profiles\\1\\name=default",
            $"profiles\\1\\button_a={Button(1)}",
            $"profiles\\1\\button_b={Button(0)}",
            $"profiles\\1\\button_x={Button(3)}",
            $"profiles\\1\\button_y={Button(2)}",
            $"profiles\\1\\button_up={Button(11)}",
            $"profiles\\1\\button_down={Button(12)}",
            $"profiles\\1\\button_left={Button(13)}",
            $"profiles\\1\\button_right={Button(14)}",
            $"profiles\\1\\button_l={Button(9)}",
            $"profiles\\1\\button_r={Button(10)}",
            $"profiles\\1\\button_start={Button(6)}",
            $"profiles\\1\\button_select={Button(4)}",
            $"profiles\\1\\button_zl={Trigger(4)}",
            $"profiles\\1\\button_zr={Trigger(5)}",
            $"profiles\\1\\button_home={Button(8)}",
            $"profiles\\1\\circle_pad={Stick(0, 1)}",
            $"profiles\\1\\c_stick={Stick(2, 3)}",
            "profiles\\size=1",
            "");
    }

    /// <summary>
    /// Adds Azahar's [Controls] section, or replaces one that still has no SDL binding at all
    /// (Azahar's keyboard-only first-run defaults). Other sections are kept as they are.
    /// </summary>
    internal static void ApplyAzahar(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var lines = File.Exists(path) ? File.ReadAllLines(path).ToList() : [];
        var start = lines.FindIndex(line => line.Trim().Equals("[Controls]", StringComparison.OrdinalIgnoreCase));
        if (start >= 0)
        {
            var end = lines.FindIndex(start + 1, line => line.TrimStart().StartsWith('['));
            if (end < 0) end = lines.Count;
            if (lines.Skip(start).Take(end - start).Any(line => line.Contains("engine:sdl", StringComparison.OrdinalIgnoreCase)))
                return;
            lines.RemoveRange(start, end - start);
        }

        if (lines.Count > 0 && lines[^1].Length > 0) lines.Add(string.Empty);
        lines.AddRange(AzaharControls().TrimEnd().Split(NewLine));
        File.WriteAllText(path, string.Join(NewLine, lines) + NewLine, new UTF8Encoding(false));
    }

    private static void WriteUnlessMapped(string path, string content, Func<string, bool> alreadyMapped)
    {
        if (File.Exists(path) && alreadyMapped(File.ReadAllText(path))) return;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content, new UTF8Encoding(false));
    }
}
