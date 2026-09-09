using System.IO;
using System.Media;

namespace GrevHome.Presentation;

public enum ShellSound
{
    Navigate,
    Select,
    Back,
    Confirm,
    Error,
    Startup
}

/// <summary>Small dependency-free theme sound generator. Themes can replace this implementation
/// later; the default theme deliberately uses short, quiet console-style tones.</summary>
public sealed class ShellFeedbackPlayer : IDisposable
{
    private readonly object _gate = new();
    private SoundPlayer? _current;
    private MemoryStream? _stream;

    public void Play(ShellSound sound, int volumePercent)
    {
        var notes = sound switch
        {
            ShellSound.Navigate => new[] { (620d, 34) },
            ShellSound.Select => new[] { (520d, 45), (780d, 65) },
            ShellSound.Back => new[] { (600d, 45), (390d, 65) },
            ShellSound.Confirm => new[] { (520d, 45), (720d, 45), (960d, 80) },
            ShellSound.Error => new[] { (210d, 75), (165d, 95) },
            _ => new[] { (330d, 100), (520d, 120), (780d, 170) }
        };

        try
        {
            var stream = BuildWave(notes, Math.Clamp(volumePercent, 0, 100) / 100d);
            lock (_gate)
            {
                _current?.Stop();
                _current?.Dispose();
                _stream?.Dispose();
                _stream = stream;
                _current = new SoundPlayer(stream);
                _current.Play();
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException)
        {
            // Presentation feedback must never interrupt controller navigation.
        }
    }

    private static MemoryStream BuildWave(IReadOnlyList<(double Frequency, int Milliseconds)> notes, double volume)
    {
        const int sampleRate = 22050;
        var sampleCount = notes.Sum(note => sampleRate * note.Milliseconds / 1000);
        var dataLength = sampleCount * sizeof(short);
        var stream = new MemoryStream(44 + dataLength);
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.ASCII, leaveOpen: true))
        {
            writer.Write("RIFF"u8.ToArray()); writer.Write(36 + dataLength); writer.Write("WAVE"u8.ToArray());
            writer.Write("fmt "u8.ToArray()); writer.Write(16); writer.Write((short)1); writer.Write((short)1);
            writer.Write(sampleRate); writer.Write(sampleRate * sizeof(short)); writer.Write((short)sizeof(short)); writer.Write((short)16);
            writer.Write("data"u8.ToArray()); writer.Write(dataLength);
            foreach (var note in notes)
            {
                var count = sampleRate * note.Milliseconds / 1000;
                for (var index = 0; index < count; index++)
                {
                    var envelope = Math.Min(1d, Math.Min(index / 90d, (count - index) / 140d));
                    var sample = Math.Sin(2 * Math.PI * note.Frequency * index / sampleRate);
                    writer.Write((short)(sample * short.MaxValue * .16 * volume * envelope));
                }
            }
        }
        stream.Position = 0;
        return stream;
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _current?.Stop();
            _current?.Dispose();
            _stream?.Dispose();
        }
    }
}
