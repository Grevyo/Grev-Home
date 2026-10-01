using System.IO;
using System.Xml.Linq;
using GrevHome.Store;
using GrevHome.Store.Installers;

// Emulator setup defaults: every standalone emulator gets a usable controller and no blocking
// first-run prompt, and nothing a person already mapped is ever replaced.
var root = Path.Combine(Path.GetTempPath(), "GrevHome-emulator-setup-" + Guid.NewGuid().ToString("N"));
try
{
    var games = Path.Combine(root, "Games");
    var bios = Path.Combine(root, "Bios");

    string Configure(string appId)
    {
        var spec = StandaloneEmulatorCatalog.Specs.Single(item => item.AppId == appId);
        var binary = Path.Combine(root, appId, "bin");
        var data = Path.Combine(root, appId, "data");
        Directory.CreateDirectory(binary);
        spec.Configure(binary, data, games, bios);
        return Path.Combine(root, appId);
    }

    // Cemu: Wii U GamePad on XInput user 0 with Cemu's 24 default mappings, no update prompt.
    var cemu = Configure("cemu");
    var cemuProfile = XDocument.Load(Path.Combine(cemu, "bin", "controllerProfiles", "controller0.xml"));
    Check(cemuProfile.Root?.Element("type")?.Value == "Wii U GamePad", "Cemu profile must emulate the Wii U GamePad");
    var cemuController = cemuProfile.Root!.Element("controller")!;
    Check(cemuController.Element("api")?.Value == "XInput" && cemuController.Element("uuid")?.Value == "0", "Cemu must use XInput user 0");
    var entries = cemuController.Element("mappings")!.Elements("entry").ToArray();
    Check(entries.Length == 24, "Cemu must map every GamePad button and stick direction");
    Check(entries.Select(e => e.Element("mapping")!.Value).Distinct().Count() == 24, "Cemu mappings must not repeat");
    Check(entries.Any(e => e.Element("mapping")!.Value == "1" && e.Element("button")!.Value == "13"), "Cemu A must be XInput A (kButton13)");
    Check(File.ReadAllText(Path.Combine(cemu, "bin", "settings.xml")).Contains("<check_update>false</check_update>"), "Cemu must not prompt for updates");

    // RPCS3: XInput handler for player 1, no welcome box or update prompt.
    var rpcs3 = Configure("rpcs3");
    var rpcs3Input = Path.Combine(rpcs3, "bin", "config", "input_configs", "global", "Default.yml");
    Check(File.ReadAllText(rpcs3Input).Contains("Handler: XInput"), "RPCS3 player 1 must use XInput");
    var rpcs3Gui = File.ReadAllText(Path.Combine(rpcs3, "bin", "GuiConfigs", "CurrentSettings.ini"));
    Check(rpcs3Gui.Contains("infoBoxEnabledWelcome=false") && rpcs3Gui.Contains("checkUpdateStart=false"), "RPCS3 must start without prompts");
    File.WriteAllText(rpcs3Input, "Player 1 Input:\n  Handler: Null\n  Device: Null\n");
    Configure("rpcs3");
    Check(File.ReadAllText(rpcs3Input).Contains("Handler: XInput"), "RPCS3's unbound default must be replaced");
    const string customRpcs3 = "Player 1 Input:\n  Handler: DualShock 4\n  Device: DS4 Pad #1\n";
    File.WriteAllText(rpcs3Input, customRpcs3);
    Configure("rpcs3");
    Check(File.ReadAllText(rpcs3Input) == customRpcs3, "A person's own RPCS3 controller must be kept");

    // Dolphin: GameCube pad plus Wii Remote + Nunchuk, no auto-update prompt.
    var dolphin = Configure("dolphin");
    var wiimote = Path.Combine(dolphin, "data", "Config", "WiimoteNew.ini");
    var wiimoteText = File.ReadAllText(wiimote);
    Check(wiimoteText.Contains("Device = XInput/0/Gamepad") && wiimoteText.Contains("Extension = Nunchuk") &&
          wiimoteText.Contains("IR/Up = `Right Y+`"), "Dolphin Wii Remote must be on the pad with a Nunchuk and pointer");
    Check(File.Exists(Path.Combine(dolphin, "data", "Config", "GCPadNew.ini")), "Dolphin GameCube pad must still be configured");
    Check(File.ReadAllText(Path.Combine(dolphin, "data", "Config", "Dolphin.ini")).Contains("[AutoUpdate]"), "Dolphin must not prompt for updates");
    File.WriteAllText(wiimote, "[Wiimote1]\nDevice = DInput/0/Keyboard Mouse\n");
    Configure("dolphin");
    Check(File.ReadAllText(wiimote).Contains("XInput/0/Gamepad"), "Dolphin's keyboard-only default must be replaced");

    // Azahar: SDL bindings for whichever pad is connected, merged into the existing Qt config.
    var azahar = Configure("azahar");
    var qtConfig = Path.Combine(azahar, "bin", "user", "config", "qt-config.ini");
    var qtText = File.ReadAllText(qtConfig);
    Check(qtText.Contains("[UI]") && qtText.Contains("[Controls]"), "Azahar must keep its game paths and gain controls");
    Check(qtText.Contains("profiles\\1\\button_a=\"engine:sdl,api:controller,guid:0,port:0,button:1\""), "Azahar A must be the pad's east face button");
    File.WriteAllText(qtConfig, "[UI]\nfullscreen=true\n[Controls]\nprofile=0\nprofiles\\1\\button_a=\"code:65,engine:keyboard\"\nprofiles\\size=1\n");
    Configure("azahar");
    qtText = File.ReadAllText(qtConfig);
    Check(!qtText.Contains("engine:keyboard") && qtText.Contains("engine:sdl") && qtText.Contains("fullscreen=true"), "Azahar's keyboard-only controls must be replaced, other settings kept");
    Configure("azahar");
    Check(File.ReadAllText(qtConfig).Split("[Controls]").Length == 2, "Azahar controls must not be added twice");
    File.WriteAllText(qtConfig, "[Controls]\nprofiles\\1\\button_a=\"engine:sdl,guid:abc,port:1,button:7\"\n");
    Configure("azahar");
    Check(File.ReadAllText(qtConfig).Contains("guid:abc"), "A person's own Azahar mapping must be kept");

    // Emulators that bind pads natively still configure without error.
    foreach (var appId in new[] { "xenia", "xemu", "vita3k" }) Configure(appId);

    // PCSX2: SDL pad bindings added next to the keyboard defaults, once.
    var pcsx2 = new List<string> { "[Pad1]", "Type = DualShock2", "Cross = Keyboard/X" };
    Check(PCSX2InstallerService.AddSdlPadBindings(pcsx2), "PCSX2 keyboard-only pad must gain controller bindings");
    Check(pcsx2.Contains("Cross = Keyboard/X") && pcsx2.Contains("Cross = SDL-0/FaceSouth"), "PCSX2 must keep keyboard and add controller");
    Check(pcsx2.Contains("SDL=true"), "PCSX2 SDL input source must be on");
    Check(!PCSX2InstallerService.AddSdlPadBindings(pcsx2), "PCSX2 bindings must be added only once");
    var mappedPcsx2 = new List<string> { "[Pad1]", "Cross = XInput-0/A" };
    Check(!PCSX2InstallerService.AddSdlPadBindings(mappedPcsx2) && mappedPcsx2.Count == 2, "A person's own PCSX2 controller must be kept");

    Console.WriteLine("Emulator setup tests passed: controller defaults for Cemu, RPCS3, Dolphin, Azahar and PCSX2, no first-run prompts, and existing mappings preserved.");
}
finally
{
    if (Directory.Exists(root)) Directory.Delete(root, true);
}

static void Check(bool value, string message) { if (!value) throw new Exception(message); }
