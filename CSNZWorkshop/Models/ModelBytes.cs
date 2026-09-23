using System.Buffers.Binary;
using System.Numerics;
using System.Text;

namespace CSNZWorkshop.Models;

public sealed class ModelBytes(byte[] data)
{
    public byte[] Data { get; } = data;
    public void Range(long offset, long count)
    {
        if (offset < 0 || count < 0 || offset > Data.Length || count > Data.Length - offset)
            throw new InvalidDataException($"MDL truncado o rango fuera del archivo ({offset}, {count}).");
    }
    public int Table(int count, int offset, int stride, int maximum)
    {
        if (count < 0 || count > maximum) throw new InvalidDataException("Cantidad MDL fuera de los límites del visor.");
        if (count > 0) { if (offset < 76) throw new InvalidDataException("Tabla MDL solapada con la cabecera."); Range(offset, (long)count * stride); }
        return count;
    }
    public int Int(int offset) { Range(offset, 4); return BinaryPrimitives.ReadInt32LittleEndian(Data.AsSpan(offset)); }
    public short Short(int offset) { Range(offset, 2); return BinaryPrimitives.ReadInt16LittleEndian(Data.AsSpan(offset)); }
    public ushort UShort(int offset) { Range(offset, 2); return BinaryPrimitives.ReadUInt16LittleEndian(Data.AsSpan(offset)); }
    public float Float(int offset)
    {
        float result = BitConverter.Int32BitsToSingle(Int(offset));
        if (!float.IsFinite(result) || Math.Abs(result) > 1e8) throw new InvalidDataException("Coordenada MDL no válida.");
        return result;
    }
    public Vector3 Vector(int offset) => new(Float(offset), Float(offset + 4), Float(offset + 8));
    public string Text(int offset, int count)
    {
        Range(offset, count); int end = Array.IndexOf(Data, (byte)0, offset, count);
        return Encoding.Latin1.GetString(Data, offset, (end < 0 ? offset + count : end) - offset);
    }
}
