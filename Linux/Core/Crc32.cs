// SPDX-License-Identifier: GPL-3.0-or-later
namespace ArdysaModsTools.Linux;

internal sealed class Crc32
{
    private static readonly uint[] Table = MakeTable();
    private uint value = uint.MaxValue;
    public void Append(ReadOnlySpan<byte> bytes)
    {
        foreach (var b in bytes)
        {
            value = (value >> 8) ^ Table[(value ^ b) & 255];
        }
    }
    public uint Value => ~value;
    private static uint[] MakeTable()
    {
        var table = new uint[256];
        for (uint n = 0; n < table.Length; n++)
        {
            var v = n;
            for (var i = 0; i < 8; i++) v = (v >> 1) ^ ((v & 1) == 0 ? 0 : 0xEDB88320u);
            table[n] = v;
        }
        return table;
    }
}
