using System.Text;

namespace AMGH.ITInventory.Infrastructure.QrCodes;

/// <summary>
/// QR Code encoder (ISO/IEC 18004): byte mode (UTF-8), error-correction level M (~15% recovery,
/// tolerant of scuffed labels), versions 1-40, automatic mask selection. Verified module-for-module
/// against an independent reference implementation and decoded with OpenCV in testing.
/// </summary>
internal static class QrEncoder
{
    // Level M: { ECC codewords per block, group-1 blocks, data codewords, group-2 blocks, data codewords }
    private static readonly int[,] BlocksM =
    {
        { 10, 1, 16, 0, 0 }, // v1
        { 16, 1, 28, 0, 0 }, // v2
        { 26, 1, 44, 0, 0 }, // v3
        { 18, 2, 32, 0, 0 }, // v4
        { 24, 2, 43, 0, 0 }, // v5
        { 16, 4, 27, 0, 0 }, // v6
        { 18, 4, 31, 0, 0 }, // v7
        { 22, 2, 38, 2, 39 }, // v8
        { 22, 3, 36, 2, 37 }, // v9
        { 26, 4, 43, 1, 44 }, // v10
        { 30, 1, 50, 4, 51 }, // v11
        { 22, 6, 36, 2, 37 }, // v12
        { 22, 8, 37, 1, 38 }, // v13
        { 24, 4, 40, 5, 41 }, // v14
        { 24, 5, 41, 5, 42 }, // v15
        { 28, 7, 45, 3, 46 }, // v16
        { 28, 10, 46, 1, 47 }, // v17
        { 26, 9, 43, 4, 44 }, // v18
        { 26, 3, 44, 11, 45 }, // v19
        { 26, 3, 41, 13, 42 }, // v20
        { 26, 17, 42, 0, 0 }, // v21
        { 28, 17, 46, 0, 0 }, // v22
        { 28, 4, 47, 14, 48 }, // v23
        { 28, 6, 45, 14, 46 }, // v24
        { 28, 8, 47, 13, 48 }, // v25
        { 28, 19, 46, 4, 47 }, // v26
        { 28, 22, 45, 3, 46 }, // v27
        { 28, 3, 45, 23, 46 }, // v28
        { 28, 21, 45, 7, 46 }, // v29
        { 28, 19, 47, 10, 48 }, // v30
        { 28, 2, 46, 29, 47 }, // v31
        { 28, 10, 46, 23, 47 }, // v32
        { 28, 14, 46, 21, 47 }, // v33
        { 28, 14, 46, 23, 47 }, // v34
        { 28, 12, 47, 26, 48 }, // v35
        { 28, 6, 47, 34, 48 }, // v36
        { 28, 29, 46, 14, 47 }, // v37
        { 28, 13, 46, 32, 47 }, // v38
        { 28, 40, 47, 7, 48 }, // v39
        { 28, 18, 47, 31, 48 }, // v40
    };
    private static readonly int[][] Alignment =
    {
        Array.Empty<int>(),
        new[] { 6, 18 },
        new[] { 6, 22 },
        new[] { 6, 26 },
        new[] { 6, 30 },
        new[] { 6, 34 },
        new[] { 6, 22, 38 },
        new[] { 6, 24, 42 },
        new[] { 6, 26, 46 },
        new[] { 6, 28, 50 },
        new[] { 6, 30, 54 },
        new[] { 6, 32, 58 },
        new[] { 6, 34, 62 },
        new[] { 6, 26, 46, 66 },
        new[] { 6, 26, 48, 70 },
        new[] { 6, 26, 50, 74 },
        new[] { 6, 30, 54, 78 },
        new[] { 6, 30, 56, 82 },
        new[] { 6, 30, 58, 86 },
        new[] { 6, 34, 62, 90 },
        new[] { 6, 28, 50, 72, 94 },
        new[] { 6, 26, 50, 74, 98 },
        new[] { 6, 30, 54, 78, 102 },
        new[] { 6, 28, 54, 80, 106 },
        new[] { 6, 32, 58, 84, 110 },
        new[] { 6, 30, 58, 86, 114 },
        new[] { 6, 34, 62, 90, 118 },
        new[] { 6, 26, 50, 74, 98, 122 },
        new[] { 6, 30, 54, 78, 102, 126 },
        new[] { 6, 26, 52, 78, 104, 130 },
        new[] { 6, 30, 56, 82, 108, 134 },
        new[] { 6, 34, 60, 86, 112, 138 },
        new[] { 6, 30, 58, 86, 114, 142 },
        new[] { 6, 34, 62, 90, 118, 146 },
        new[] { 6, 30, 54, 78, 102, 126, 150 },
        new[] { 6, 24, 50, 76, 102, 128, 154 },
        new[] { 6, 28, 54, 80, 106, 132, 158 },
        new[] { 6, 32, 58, 84, 110, 136, 162 },
        new[] { 6, 26, 54, 82, 110, 138, 166 },
        new[] { 6, 30, 58, 86, 114, 142, 170 },
    };


    private const int MaxVersion = 40;

    /// <summary>Encodes <paramref name="text"/>. <paramref name="forceMask"/> is for tests only.</summary>
    public static bool[,] Encode(string text, int? forceMask = null)
    {
        ArgumentNullException.ThrowIfNull(text);
        var data = Encoding.UTF8.GetBytes(text);

        var version = 0;
        for (var v = 1; v <= MaxVersion; v++)
        {
            var countBits = v <= 9 ? 8 : 16;
            if (4 + countBits + 8 * data.Length <= DataCodewords(v) * 8) { version = v; break; }
        }
        if (version == 0) throw new ArgumentException("The text is too long for a QR code.", nameof(text));

        var codewords = AddErrorCorrection(BuildDataCodewords(data, version), version);
        var size = 17 + 4 * version;
        var modules = new bool[size, size];
        var isFunction = new bool[size, size];
        DrawFunctionPatterns(modules, isFunction, version);
        PlaceCodewords(modules, isFunction, codewords, version);

        var bestMask = forceMask ?? -1;
        if (bestMask < 0)
        {
            var bestPenalty = int.MaxValue;
            for (var mask = 0; mask < 8; mask++)
            {
                ApplyMask(modules, isFunction, mask);
                DrawFormatBits(modules, mask);
                var penalty = Penalty(modules);
                if (penalty < bestPenalty) { bestPenalty = penalty; bestMask = mask; }
                ApplyMask(modules, isFunction, mask); // undo (XOR)
            }
        }
        ApplyMask(modules, isFunction, bestMask);
        DrawFormatBits(modules, bestMask);
        return modules;
    }

    private static int DataCodewords(int v) => BlocksM[v - 1, 1] * BlocksM[v - 1, 2] + BlocksM[v - 1, 3] * BlocksM[v - 1, 4];

    // ------------------------------------------------------------------ data

    private static byte[] BuildDataCodewords(byte[] data, int version)
    {
        var capacityBits = DataCodewords(version) * 8;
        var bits = new List<bool>(capacityBits);
        void Append(int value, int count) { for (var i = count - 1; i >= 0; i--) bits.Add(((value >> i) & 1) != 0); }

        Append(0b0100, 4);                                  // byte mode
        Append(data.Length, version <= 9 ? 8 : 16);         // character count
        foreach (var b in data) Append(b, 8);
        Append(0, Math.Min(4, capacityBits - bits.Count));  // terminator
        Append(0, (8 - bits.Count % 8) % 8);                // byte align
        for (var pad = 0xEC; bits.Count < capacityBits; pad ^= 0xEC ^ 0x11) Append(pad, 8);

        var result = new byte[bits.Count / 8];
        for (var i = 0; i < bits.Count; i++) if (bits[i]) result[i >> 3] |= (byte)(0x80 >> (i & 7));
        return result;
    }

    private static byte[] AddErrorCorrection(byte[] data, int version)
    {
        int ecc = BlocksM[version - 1, 0], g1 = BlocksM[version - 1, 1], d1 = BlocksM[version - 1, 2],
            g2 = BlocksM[version - 1, 3], d2 = BlocksM[version - 1, 4];
        var generator = ReedSolomonGenerator(ecc);
        var dataBlocks = new List<byte[]>();
        var eccBlocks = new List<byte[]>();
        var offset = 0;
        for (var i = 0; i < g1 + g2; i++)
        {
            var len = i < g1 ? d1 : d2;
            var block = data.AsSpan(offset, len).ToArray();
            offset += len;
            dataBlocks.Add(block);
            eccBlocks.Add(ReedSolomonRemainder(block, generator));
        }

        var result = new List<byte>(data.Length + ecc * (g1 + g2));
        for (var i = 0; i < Math.Max(d1, d2); i++)
            foreach (var block in dataBlocks) if (i < block.Length) result.Add(block[i]);
        for (var i = 0; i < ecc; i++)
            foreach (var block in eccBlocks) result.Add(block[i]);
        return result.ToArray();
    }

    // GF(256) with polynomial x^8 + x^4 + x^3 + x^2 + 1 (0x11D)
    private static byte Multiply(int x, int y)
    {
        var z = 0;
        for (var i = 7; i >= 0; i--)
        {
            z = (z << 1) ^ ((z >> 7) * 0x11D);
            z ^= ((y >> i) & 1) * x;
        }
        return (byte)z;
    }

    private static byte[] ReedSolomonGenerator(int degree)
    {
        var result = new byte[degree];
        result[degree - 1] = 1;
        var root = 1;
        for (var i = 0; i < degree; i++)
        {
            for (var j = 0; j < result.Length; j++)
            {
                result[j] = Multiply(result[j], root);
                if (j + 1 < result.Length) result[j] ^= result[j + 1];
            }
            root = Multiply(root, 0x02);
        }
        return result;
    }

    private static byte[] ReedSolomonRemainder(byte[] data, byte[] generator)
    {
        var result = new byte[generator.Length];
        foreach (var b in data)
        {
            var factor = b ^ result[0];
            Array.Copy(result, 1, result, 0, result.Length - 1);
            result[^1] = 0;
            for (var i = 0; i < result.Length; i++) result[i] ^= Multiply(generator[i], factor);
        }
        return result;
    }

    // ------------------------------------------------------------------ matrix

    private static void Set(bool[,] m, bool[,] f, int x, int y, bool dark)
    {
        m[y, x] = dark;
        f[y, x] = true;
    }

    private static void DrawFunctionPatterns(bool[,] m, bool[,] f, int version)
    {
        var size = m.GetLength(0);
        for (var i = 0; i < size; i++)
        {
            Set(m, f, 6, i, i % 2 == 0); // timing
            Set(m, f, i, 6, i % 2 == 0);
        }

        DrawFinder(m, f, 3, 3);
        DrawFinder(m, f, size - 4, 3);
        DrawFinder(m, f, 3, size - 4);

        var positions = Alignment[version - 1];
        foreach (var cy in positions)
            foreach (var cx in positions)
            {
                var overlapsFinder = (cx == 6 && cy == 6) || (cx == 6 && cy == size - 7) || (cx == size - 7 && cy == 6);
                if (overlapsFinder) continue;
                for (var dy = -2; dy <= 2; dy++)
                    for (var dx = -2; dx <= 2; dx++)
                        Set(m, f, cx + dx, cy + dy, Math.Max(Math.Abs(dx), Math.Abs(dy)) != 1);
            }

        DrawFormatBits(m, 0, f); // reserve format areas (overwritten after masking)
        if (version >= 7) DrawVersion(m, f, version);
        Set(m, f, 8, size - 8, true); // dark module
    }

    private static void DrawFinder(bool[,] m, bool[,] f, int cx, int cy)
    {
        var size = m.GetLength(0);
        for (var dy = -4; dy <= 4; dy++)
            for (var dx = -4; dx <= 4; dx++)
            {
                int x = cx + dx, y = cy + dy;
                if (x < 0 || x >= size || y < 0 || y >= size) continue;
                var d = Math.Max(Math.Abs(dx), Math.Abs(dy));
                Set(m, f, x, y, d != 2 && d != 4);
            }
    }

    /// <summary>Error-correction level M (00) + mask, BCH(15,5), XOR 0x5412.</summary>
    private static void DrawFormatBits(bool[,] m, int mask, bool[,]? f = null)
    {
        var size = m.GetLength(0);
        var data = (0b00 << 3) | mask;
        var rem = data;
        for (var i = 0; i < 10; i++) rem = (rem << 1) ^ ((rem >> 9) * 0x537);
        var bits = ((data << 10) | rem) ^ 0x5412;
        bool Bit(int i) => ((bits >> i) & 1) != 0;
        void Put(int x, int y, bool v) { m[y, x] = v; if (f is not null) f[y, x] = true; }

        for (var i = 0; i <= 5; i++) Put(8, i, Bit(i));
        Put(8, 7, Bit(6));
        Put(8, 8, Bit(7));
        Put(7, 8, Bit(8));
        for (var i = 9; i < 15; i++) Put(14 - i, 8, Bit(i));

        for (var i = 0; i < 8; i++) Put(size - 1 - i, 8, Bit(i));
        for (var i = 8; i < 15; i++) Put(8, size - 15 + i, Bit(i));
        Put(8, size - 8, true);
    }

    private static void DrawVersion(bool[,] m, bool[,] f, int version)
    {
        var size = m.GetLength(0);
        var rem = version;
        for (var i = 0; i < 12; i++) rem = (rem << 1) ^ ((rem >> 11) * 0x1F25);
        var bits = (version << 12) | rem;
        for (var i = 0; i < 18; i++)
        {
            var bit = ((bits >> i) & 1) != 0;
            int a = size - 11 + i % 3, b = i / 3;
            Set(m, f, a, b, bit);
            Set(m, f, b, a, bit);
        }
    }

    private static void PlaceCodewords(bool[,] m, bool[,] f, byte[] codewords, int version)
    {
        var size = m.GetLength(0);
        var totalBits = codewords.Length * 8;
        var i = 0;
        for (var right = size - 1; right >= 1; right -= 2)
        {
            if (right == 6) right = 5;
            for (var vert = 0; vert < size; vert++)
                for (var j = 0; j < 2; j++)
                {
                    var x = right - j;
                    var upward = ((right + 1) & 2) == 0;
                    var y = upward ? size - 1 - vert : vert;
                    if (f[y, x]) continue;
                    if (i < totalBits) m[y, x] = ((codewords[i >> 3] >> (7 - (i & 7))) & 1) != 0;
                    i++; // remainder bits (after the last codeword) stay light
                }
        }
    }

    private static void ApplyMask(bool[,] m, bool[,] f, int mask)
    {
        var size = m.GetLength(0);
        for (var y = 0; y < size; y++)
            for (var x = 0; x < size; x++)
            {
                if (f[y, x]) continue;
                var invert = mask switch
                {
                    0 => (x + y) % 2 == 0,
                    1 => y % 2 == 0,
                    2 => x % 3 == 0,
                    3 => (x + y) % 3 == 0,
                    4 => (x / 3 + y / 2) % 2 == 0,
                    5 => x * y % 2 + x * y % 3 == 0,
                    6 => (x * y % 2 + x * y % 3) % 2 == 0,
                    _ => ((x + y) % 2 + x * y % 3) % 2 == 0
                };
                if (invert) m[y, x] = !m[y, x];
            }
    }

    // ------------------------------------------------------------------ mask penalty (ISO/IEC 18004 §7.8.3)

    private static int Penalty(bool[,] m)
    {
        var size = m.GetLength(0);
        var penalty = 0;

        for (var pass = 0; pass < 2; pass++)
            for (var a = 0; a < size; a++)
            {
                var run = 1;
                for (var b = 1; b < size; b++)
                {
                    var cur = pass == 0 ? m[a, b] : m[b, a];
                    var prev = pass == 0 ? m[a, b - 1] : m[b - 1, a];
                    if (cur == prev)
                    {
                        run++;
                        if (run == 5) penalty += 3;
                        else if (run > 5) penalty++;
                    }
                    else run = 1;
                }
            }

        for (var y = 0; y < size - 1; y++)
            for (var x = 0; x < size - 1; x++)
            {
                var c = m[y, x];
                if (c == m[y, x + 1] && c == m[y + 1, x] && c == m[y + 1, x + 1]) penalty += 3;
            }

        bool[] p1 = { true, false, true, true, true, false, true, false, false, false, false };
        bool[] p2 = { false, false, false, false, true, false, true, true, true, false, true };
        for (var pass = 0; pass < 2; pass++)
            for (var a = 0; a < size; a++)
                for (var b = 0; b + 11 <= size; b++)
                {
                    bool m1 = true, m2 = true;
                    for (var k = 0; k < 11; k++)
                    {
                        var v = pass == 0 ? m[a, b + k] : m[b + k, a];
                        if (v != p1[k]) m1 = false;
                        if (v != p2[k]) m2 = false;
                    }
                    if (m1) penalty += 40;
                    if (m2) penalty += 40;
                }

        var dark = 0;
        foreach (var v in m) if (v) dark++;
        var total = size * size;
        var k5 = (Math.Abs(dark * 20 - total * 10) + total - 1) / total - 1;
        penalty += Math.Max(0, k5) * 10;
        return penalty;
    }
}
