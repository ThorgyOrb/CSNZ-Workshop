using System.Runtime.InteropServices;
using System.Text;

namespace CSNZWorkshop.Media;

// MP3 is decoded by NLayer to PCM; MCI only needs Windows' waveaudio driver.
public sealed class AudioPreview : IDisposable
{
    readonly string alias = "csnz" + Guid.NewGuid().ToString("N");
    string? path;
    bool opened;
    bool ownsFile;
    public int DurationMilliseconds { get; private set; }
    internal string? TemporaryPath => path;

    [DllImport("winmm.dll", CharSet = CharSet.Unicode)]
    static extern uint mciSendString(string command, StringBuilder? result, int length, IntPtr callback);
    [DllImport("winmm.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool mciGetErrorString(uint error, StringBuilder text, int length);

    string Command(string command)
    {
        var result = new StringBuilder(256);
        uint error = mciSendString(command, result, result.Capacity, IntPtr.Zero);
        if (error != 0)
        {
            var message = new StringBuilder(256); mciGetErrorString(error, message, message.Capacity);
            throw new InvalidOperationException("Windows no pudo reproducir este sonido: " + message);
        }
        return result.ToString();
    }

    public AudioPreview(byte[] bytes, string extension)
    {
        if (extension is not (".wav" or ".mp3")) throw new NotSupportedException("Audio no admitido.");
        if (bytes.Length > 64 * 1024 * 1024) throw new InvalidDataException("Sonido demasiado grande para la vista previa (64 MB).");
        if (extension == ".mp3") bytes = DecodeMp3(bytes);
        path = Path.Combine(Path.GetTempPath(), alias + ".wav");
        try
        {
            using (var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { ownsFile = true; file.Write(bytes); }
            Command($"open \"{path}\" type waveaudio alias {alias}");
            opened = true;
            Command($"set {alias} time format milliseconds");
            DurationMilliseconds = int.Parse(Command($"status {alias} length"), System.Globalization.CultureInfo.InvariantCulture);
        }
        catch { Dispose(); throw; }
    }

    public static byte[] DecodeMp3(byte[] bytes, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        if (bytes.Length > 64 * 1024 * 1024) throw new InvalidDataException("MP3 demasiado grande para la vista previa.");
        using var input = new MemoryStream(bytes, false);
        using var decoder = new NLayer.MpegFile(input);
        if (decoder.Channels is < 1 or > 2 || decoder.SampleRate is < 8000 or > 48000)
            throw new InvalidDataException("Formato de audio MP3 no admitido.");
        using var output = new MemoryStream(); using var writer = new BinaryWriter(output);
        writer.Write("RIFF"u8); writer.Write(0); writer.Write("WAVEfmt "u8); writer.Write(16);
        writer.Write((ushort)1); writer.Write((ushort)decoder.Channels); writer.Write(decoder.SampleRate);
        writer.Write(decoder.SampleRate * decoder.Channels * 2); writer.Write((ushort)(decoder.Channels * 2));
        writer.Write((ushort)16); writer.Write("data"u8); writer.Write(0);
        var samples = new float[8192];
        while (true)
        {
            token.ThrowIfCancellationRequested();
            int count = decoder.ReadSamples(samples, 0, samples.Length);
            if (count == 0) break;
            if (output.Length + count * 2 > 64 * 1024 * 1024) throw new InvalidDataException("Audio decodificado excede 64 MB de vista previa.");
            for (int i = 0; i < count; i++) writer.Write((short)(Math.Clamp(float.IsFinite(samples[i]) ? samples[i] : 0, -1, 1) * 32767));
        }
        if (output.Length == 44) throw new InvalidDataException("El MP3 no contiene muestras de audio.");
        int length = (int)output.Length;
        output.Position = 4; writer.Write(length - 8); output.Position = 40; writer.Write(length - 44);
        return output.ToArray();
    }

    public void Play() => Command($"play {alias} from 0");
    public void Stop() => Command($"stop {alias}");
    public bool IsPlaying => Command($"status {alias} mode").Equals("playing", StringComparison.OrdinalIgnoreCase);
    public void Dispose()
    {
        if (opened) { mciSendString($"close {alias}", null, 0, IntPtr.Zero); opened = false; }
        if (path is not null && ownsFile)
        {
            try { File.Delete(path); path = null; }
            catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }
}
