// Reproducible C++ -> C# port of the CSO-Paker cipher. See THIRD_PARTY.md.
const fs = require('fs');
const source = fs.readFileSync('artifacts/reference-pak/sources/snow.cpp', 'utf8');
const tables = source.slice(source.indexOf('static uint32_t'), source.indexOf('#define LOBYTE'))
  .replace(/static (?:const )?uint32_t (\w+)\[\]/g, 'static readonly uint[] $1');
let methods = source.slice(source.indexOf('void SnowCipher::SetKey'), source.indexOf('void SnowCipher::DecryptBufferImpl'));
methods = methods.replace(/\/\/[^\r\n]*/g, '')
  .replace('void SnowCipher::SetKey(const uint8_t* key)', 'public void SetKey(byte[] key)')
  .replace('void SnowCipher::DecryptBlock()', 'void DecryptBlock()')
  .replace(/(?:unsigned int|signed int|int|uint8_t) (v\d+);/g, 'uint $1;')
  .replace(/const auto castedKey = reinterpret_cast<const char\*>\(key\);/, 'if (key.Length < 16) throw new ArgumentException("Cipher key too short.");')
  .replace(/LE_DecodeSigned32\(castedKey(?: \+ (\d+))?\)/g, (_, offset) => `DecodeSigned(key, ${offset || 0})`)
  .replace(/this->/g, '')
  .replace(/\(uint8_t\)/g, '(byte)')
  .replace(/\(uint16_t\)/g, '(ushort)')
  .replace(/\(uint64_t\)/g, '(uint)')
  .replace('while (1)', 'while (true)')
  .replace(/LOBYTE\((v\d+)\) = ([^;]+);/g, '$1 = ($1 & 0xffffff00u) | (byte)($2);')
  .replace('v22 = v19 + v101;', 'v22 = (byte)(v19 + v101);');
const prefix = `// SPDX-License-Identifier: GPL-3.0-only
// Adapted from CSO-Paker (Luís Leite / overl4y); see THIRD_PARTY.md.
// C# port: managed arrays and bounds-checked byte access, 2026.
using System.Buffers.Binary;
namespace CSNZWorkshop.Archives;
internal sealed class SnowCipher
{
    readonly uint[] m_State = new uint[20];
    readonly uint[] m_Buffer = new uint[16];
    int m_BlocksAvailable;
    static byte GetByteFrom(uint value, int position) => (byte)(value >> (position * 8));
    // Preserve signed-char extension from the original cipher, including its nonstandard key decoding.
    #pragma warning disable CS0675 // Signed extension is required by this file format.
    static uint DecodeSigned(byte[] key, int offset) => unchecked((uint)(
        (sbyte)key[offset + 3] | (((sbyte)key[offset + 2] | (((sbyte)key[offset + 1] | ((sbyte)key[offset] << 8)) << 8)) << 8)));
    #pragma warning restore CS0675
    public uint DecryptWord(uint value)
    {
        if (m_BlocksAvailable == 16) { DecryptBlock(); m_BlocksAvailable = 0; }
        return unchecked(value - m_Buffer[m_BlocksAvailable++]);
    }
    public void Decrypt(Span<byte> data)
    {
        if (data.Length % 4 != 0) throw new ArgumentException("Cipher requires complete words.");
        for (int i = 0; i < data.Length; i += 4)
            BinaryPrimitives.WriteUInt32LittleEndian(data[i..], DecryptWord(BinaryPrimitives.ReadUInt32LittleEndian(data[i..])));
    }
`;
fs.mkdirSync('CSNZWorkshop/Archives', {recursive:true});
fs.writeFileSync('CSNZWorkshop/Archives/SnowCipher.cs', prefix + tables + methods + '\n}\n');
