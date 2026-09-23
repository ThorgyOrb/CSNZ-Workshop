using System.Buffers.Binary;

namespace CSNZWorkshop.Archives;

public static class ResourceInspection
{
    // A signature diagnostic is not a full validation or a conversion to CS 1.6.
    public static string Inspect(Stream stream, string extension)
    {
        stream.Position = 0;
        byte[] header = new byte[(int)Math.Min(76, stream.Length)];
        stream.ReadExactly(header);
        if (extension == ".mdl")
        {
            if (header.Length < 8 || !header.AsSpan(0, 4).SequenceEqual("IDST"u8))
                return "MDL: firma no reconocida; requiere revisión.";
            int version = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(4));
            return version is 20 or 21 ? $"MDL IDST versión {version}: cifrado interno del modelo pendiente."
                : $"MDL IDST versión {version}: extraído; compatibilidad con CS 1.6 pendiente.";
        }
        if (extension == ".wav")
            return header.Length >= 12 && header.AsSpan(0, 4).SequenceEqual("RIFF"u8) && header.AsSpan(8, 4).SequenceEqual("WAVE"u8)
                ? "Cabecera RIFF/WAVE reconocida." : "WAV: firma no reconocida; requiere revisión.";
        if (extension == ".spr")
            return header.Length >= 4 && header.AsSpan(0, 4).SequenceEqual("IDSP"u8)
                ? "Cabecera de sprite IDSP reconocida." : "Sprite: firma no reconocida; requiere revisión.";
        return "Contenido extraído; sin validación específica del formato.";
    }
}
