// ICE keys and block selection adapted from NARTools ModelHelper.cs, Copyright 2010 Da_FileServer.
// GPL-3.0-or-later. Validation and copy-only conversion added for CSNZ Workshop.
using System.Buffers.Binary;

namespace CSNZWorkshop.Models;

public static class ModelCrypt
{
    static readonly byte[] Key20 = Convert.FromHexString("32A621E0AB6BF42C93C6F196FB387568BA701386E0B371F4E39B07220CFE883A");
    static readonly byte[] Key21 = Convert.FromHexString("227A196F7B867DE08CC6F196FB387568887A788678866770D991073A1474FE22");

    public static byte[] Decode(byte[] original)
    {
        var input = new ModelBytes(original);
        input.Range(0, 244);
        if (input.Int(0) == 0x51534449)
            throw new NotSupportedException("Es un grupo de animación (IDSQ), no un modelo: ábrelo desde su archivo principal.");
        if (input.Int(0) != 0x54534449) throw new InvalidDataException("El archivo no tiene firma IDST.");
        int version = input.Int(4);
        if (version == 10) return original;
        if (version is not (20 or 21)) throw new NotSupportedException($"MDL versión {version}: conversión no implementada; no se cambia solo la cabecera.");
        if (input.Int(72) != original.Length) throw new InvalidDataException("Longitud de modelo cifrado inconsistente.");
        var ranges = new List<(int Offset, int Length)>();
        int textures = input.Table(input.Int(180), input.Int(184), 80, 1024);
        for (int i = 0; i < textures; i++)
        {
            int at = input.Int(184) + i * 80, width = input.Int(at + 68), height = input.Int(at + 72);
            if (width < 1 || height < 1 || width > 4096 || height > 4096) throw new InvalidDataException("Textura cifrada fuera de límites.");
            ranges.Add((input.Int(at + 76), checked(width * height + 768)));
        }
        int bodies = input.Table(input.Int(204), input.Int(208), 76, 256);
        for (int i = 0; i < bodies; i++)
        {
            int body = input.Int(208) + i * 76;
            int models = input.Table(input.Int(body + 64), input.Int(body + 72), 112, 256);
            for (int j = 0; j < models; j++)
            {
                int model = input.Int(body + 72) + j * 112;
                int vertices = input.Table(input.Int(model + 80), input.Int(model + 88), 12, 100000);
                if (vertices > 0) ranges.Add((input.Int(model + 88), vertices * 12));
            }
        }
        var sorted = ranges.Distinct().OrderBy(r => r.Offset).ToArray();
        long previousEnd = 244;
        foreach (var range in sorted)
        {
            input.Range(range.Offset, range.Length);
            if (range.Offset < previousEnd) throw new InvalidDataException("Bloques cifrados solapados.");
            previousEnd = (long)range.Offset + range.Length;
        }
        byte[] copy = original.ToArray();
        using var ice = new Ice(4);
        using var transform = ice.CreateDecryptor(version == 20 ? Key20 : Key21, []);
        foreach (var range in sorted)
        {
            // Nexon's scheme transforms full 1024-byte chunks; a short tail is transformed only when divisible by 8.
            for (int done = 0; done < range.Length; done += 1024)
            {
                int length = Math.Min(1024, range.Length - done);
                if (length % 8 != 0) break;
                transform.TransformBlock(copy, range.Offset + done, length, copy, range.Offset + done);
            }
        }
        // The compatible layout must still pass StudioModel.Parse before any prepared copy is exported.
        BinaryPrimitives.WriteInt32LittleEndian(copy.AsSpan(4), 10);
        return copy;
    }
}
