using System.Runtime.InteropServices;

namespace GrevHome.Machine;

public sealed record DisplayMode(int Width, int Height, int RefreshRate, int BitsPerPixel)
{
    public override string ToString() => $"{Width} × {Height}  •  {RefreshRate} Hz";
}

public sealed class DisplayService
{
    private const int EnumCurrentSettings = -1;
    private const int DispChangeSuccessful = 0;
    private const uint CdsTest = 0x00000002;
    private const uint DmBitsPerPel = 0x00040000;
    private const uint DmPelsWidth = 0x00080000;
    private const uint DmPelsHeight = 0x00100000;
    private const uint DmDisplayFrequency = 0x00400000;

    public DisplayMode GetCurrentMode()
    {
        var mode = CreateDevMode();
        if (!EnumDisplaySettings(null, EnumCurrentSettings, ref mode))
        {
            throw new InvalidOperationException("Windows did not return the current primary display mode.");
        }

        return ToDisplayMode(mode);
    }

    public IReadOnlyList<DisplayMode> GetAvailableModes()
    {
        var modes = new Dictionary<(int Width, int Height, int Frequency), DisplayMode>();
        for (var index = 0; ; index++)
        {
            var mode = CreateDevMode();
            if (!EnumDisplaySettings(null, index, ref mode))
            {
                break;
            }

            if (mode.dmPelsWidth < 640 || mode.dmPelsHeight < 480 || mode.dmDisplayFrequency <= 0)
            {
                continue;
            }

            var displayMode = ToDisplayMode(mode);
            modes[(displayMode.Width, displayMode.Height, displayMode.RefreshRate)] = displayMode;
        }

        return modes.Values
            .OrderBy(mode => mode.Width * mode.Height)
            .ThenBy(mode => mode.Width)
            .ThenBy(mode => mode.RefreshRate)
            .ToArray();
    }

    public void ApplyMode(DisplayMode requestedMode)
    {
        ArgumentNullException.ThrowIfNull(requestedMode);

        var selected = FindNativeMode(requestedMode)
            ?? throw new InvalidOperationException("That display mode is no longer reported by Windows.");

        selected.dmFields = DmPelsWidth | DmPelsHeight | DmDisplayFrequency | DmBitsPerPel;
        var testResult = ChangeDisplaySettings(ref selected, CdsTest);
        if (testResult != DispChangeSuccessful)
        {
            throw new InvalidOperationException($"Windows rejected that display mode during validation (code {testResult}).");
        }

        var applyResult = ChangeDisplaySettings(ref selected, 0);
        if (applyResult != DispChangeSuccessful)
        {
            throw new InvalidOperationException($"Windows did not apply that display mode (code {applyResult}).");
        }
    }

    private static DevMode? FindNativeMode(DisplayMode requested)
    {
        for (var index = 0; ; index++)
        {
            var mode = CreateDevMode();
            if (!EnumDisplaySettings(null, index, ref mode))
            {
                return null;
            }

            if (mode.dmPelsWidth == requested.Width &&
                mode.dmPelsHeight == requested.Height &&
                mode.dmDisplayFrequency == requested.RefreshRate)
            {
                return mode;
            }
        }
    }

    private static DisplayMode ToDisplayMode(DevMode mode) => new(
        mode.dmPelsWidth,
        mode.dmPelsHeight,
        mode.dmDisplayFrequency,
        mode.dmBitsPerPel);

    private static DevMode CreateDevMode() => new()
    {
        dmDeviceName = string.Empty,
        dmFormName = string.Empty,
        dmSize = (short)Marshal.SizeOf<DevMode>()
    };

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DevMode
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName;
        public short dmSpecVersion;
        public short dmDriverVersion;
        public short dmSize;
        public short dmDriverExtra;
        public uint dmFields;
        public int dmPositionX;
        public int dmPositionY;
        public int dmDisplayOrientation;
        public int dmDisplayFixedOutput;
        public short dmColor;
        public short dmDuplex;
        public short dmYResolution;
        public short dmTTOption;
        public short dmCollate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName;
        public short dmLogPixels;
        public int dmBitsPerPel;
        public int dmPelsWidth;
        public int dmPelsHeight;
        public int dmDisplayFlags;
        public int dmDisplayFrequency;
        public int dmICMMethod;
        public int dmICMIntent;
        public int dmMediaType;
        public int dmDitherType;
        public int dmReserved1;
        public int dmReserved2;
        public int dmPanningWidth;
        public int dmPanningHeight;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumDisplaySettings(string? deviceName, int modeNum, ref DevMode devMode);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int ChangeDisplaySettings(ref DevMode devMode, uint flags);
}
