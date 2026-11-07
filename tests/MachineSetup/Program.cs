// Machine setup that a fresh PC needs before anything else: joining a new Wi-Fi network from the
// controller keyboard and starting Grev Home with Windows. The Windows calls themselves need real
// hardware; these check the parts that decide what gets written.

using System.Xml.Linq;
using GrevHome.Machine;

// Security classification from the WLAN API's DOT11_AUTH_ALGORITHM / DOT11_CIPHER_ALGORITHM.
Check(WifiProfileBuilder.Classify(1, 0) == (WifiSecurity.Open, WifiCipher.None), "open networks");
Check(WifiProfileBuilder.Classify(1, 1).Security == WifiSecurity.Wep, "open auth with WEP cipher is WEP");
Check(WifiProfileBuilder.Classify(4, 2) == (WifiSecurity.WpaPersonal, WifiCipher.Tkip), "WPA personal with TKIP");
Check(WifiProfileBuilder.Classify(7, 4) == (WifiSecurity.Wpa2Personal, WifiCipher.Aes), "WPA2 personal with AES");
Check(WifiProfileBuilder.Classify(9, 4).Security == WifiSecurity.Wpa3Personal, "WPA3 personal");
Check(WifiProfileBuilder.Classify(10, 4).Security == WifiSecurity.Owe, "enhanced open");
Check(WifiProfileBuilder.Classify(6, 4).Security == WifiSecurity.Enterprise, "WPA2 enterprise");
Check(!WifiProfileBuilder.IsSupported(WifiSecurity.Enterprise) && !WifiProfileBuilder.IsSupported(WifiSecurity.Wep), "enterprise and WEP are left to Windows Settings");

// Which networks can be joined from Grev Home.
var newWpa2 = new WifiNetwork("Home", null, 80, true, false, false, WifiSecurity.Wpa2Personal);
Check(newWpa2.CanJoin && newWpa2.NeedsPassword, "an unsaved WPA2 network can be joined with a password");
Check(!(newWpa2 with { CanConnect = true, ProfileName = "Home" }).CanJoin, "a saved network reconnects instead");
Check(!(newWpa2 with { IsConnected = true }).CanJoin, "the connected network is not offered");
Check(!(newWpa2 with { Security = WifiSecurity.Enterprise }).CanJoin, "enterprise networks are not offered");
Check(!(newWpa2 with { Security = WifiSecurity.Open }).NeedsPassword, "open networks need no password");

// Passwords.
Check(WifiProfileBuilder.ValidatePassword(WifiSecurity.Wpa2Personal, "short") is not null, "passphrases are at least 8 characters");
Check(WifiProfileBuilder.ValidatePassword(WifiSecurity.Wpa2Personal, new string('z', 64)) is not null, "64 characters must be hex");
Check(WifiProfileBuilder.ValidatePassword(WifiSecurity.Wpa2Personal, new string('a', 63)) is null, "63 printable characters are fine");
Check(WifiProfileBuilder.ValidatePassword(WifiSecurity.Wpa2Personal, new string('F', 64)) is null, "a 64-digit hex key is fine");
Check(WifiProfileBuilder.ValidatePassword(WifiSecurity.Wpa2Personal, "pässwörd1") is not null, "non-ASCII is rejected, as Windows would");
Check(WifiProfileBuilder.ValidatePassword(WifiSecurity.Open, null) is null, "open networks take no password");

// Profile XML Windows will accept.
XNamespace ns = "http://www.microsoft.com/networking/WLAN/profile/v1";
var xml = XDocument.Parse(WifiProfileBuilder.BuildProfileXml("Café <5G> & \"Co\"", WifiSecurity.Wpa2Personal, WifiCipher.Aes, "p&ss<word>\"1"));
Check(xml.Root!.Element(ns + "name")!.Value == "Café <5G> & \"Co\"", "SSIDs with XML characters are escaped and round-trip");
Check(xml.Descendants(ns + "hex").Single().Value == Convert.ToHexString(System.Text.Encoding.UTF8.GetBytes("Café <5G> & \"Co\"")), "the SSID hex is its UTF-8 bytes");
Check(xml.Descendants(ns + "authentication").Single().Value == "WPA2PSK" && xml.Descendants(ns + "encryption").Single().Value == "AES", "WPA2 uses WPA2PSK/AES");
Check(xml.Descendants(ns + "keyMaterial").Single().Value == "p&ss<word>\"1", "passwords with XML characters round-trip");
Check(xml.Descendants(ns + "keyType").Single().Value == "passPhrase", "passphrases are marked as such");
Check(xml.Descendants(ns + "connectionMode").Single().Value == "auto", "joined networks reconnect automatically");
var hexKey = XDocument.Parse(WifiProfileBuilder.BuildProfileXml("Home", WifiSecurity.Wpa2Personal, WifiCipher.Aes, new string('A', 64)));
Check(hexKey.Descendants(ns + "keyType").Single().Value == "networkKey", "a 64-digit hex key is a network key");
var open = XDocument.Parse(WifiProfileBuilder.BuildProfileXml("Cafe", WifiSecurity.Open, WifiCipher.None, null));
Check(open.Descendants(ns + "authentication").Single().Value == "open" && !open.Descendants(ns + "sharedKey").Any(), "open profiles carry no key");
var wpa3 = XDocument.Parse(WifiProfileBuilder.BuildProfileXml("New", WifiSecurity.Wpa3Personal, WifiCipher.Aes, "password123"));
Check(wpa3.Descendants(ns + "authentication").Single().Value == "WPA3SAE", "WPA3 uses SAE");
var tkip = XDocument.Parse(WifiProfileBuilder.BuildProfileXml("Old", WifiSecurity.WpaPersonal, WifiCipher.Tkip, "password123"));
Check(tkip.Descendants(ns + "encryption").Single().Value == "TKIP", "old WPA routers keep TKIP");
Check(Throws(() => WifiProfileBuilder.BuildProfileXml("Work", WifiSecurity.Enterprise, WifiCipher.Aes, null)), "enterprise profiles are refused");
Check(Throws(() => WifiProfileBuilder.BuildProfileXml("Home", WifiSecurity.Wpa2Personal, WifiCipher.Aes, "short")), "invalid passwords are refused before Windows sees them");

// Start with Windows.
Check(WindowsStartupService.BuildCommand(@"C:\GrevCo\GrevHome\GrevHome.exe") == "\"C:\\GrevCo\\GrevHome\\GrevHome.exe\"", "the Run entry quotes the path");
Check(new WindowsStartupService(@"C:\GrevCo\GrevHome\GrevHome.exe").IsAvailable, "an installed GrevHome.exe can start with Windows");
Check(!new WindowsStartupService(@"C:\Program Files\dotnet\dotnet.exe").IsAvailable, "a development run under dotnet cannot");
Check(WindowsStartupService.IsApprovedEnabled([2, 0, 0, 0]) && !WindowsStartupService.IsApprovedEnabled([3, 0, 0, 0]), "Task Manager's enabled/disabled byte is read");

Console.WriteLine("Machine setup tests passed: Wi-Fi security detection, password rules, WLAN profile XML and start-with-Windows.");

static bool Throws(Action action)
{
    try { action(); return false; }
    catch (Exception ex) when (ex is ArgumentException or NotSupportedException) { return true; }
}

static void Check(bool value, string message) { if (!value) throw new Exception(message); }
