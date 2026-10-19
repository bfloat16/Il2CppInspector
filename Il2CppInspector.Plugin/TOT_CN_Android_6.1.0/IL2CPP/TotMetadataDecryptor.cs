using System.Buffers.Binary;
using System.Numerics;

namespace Il2CppInspector
{
    /// <summary>
    /// Static decryption of the 未定事件簿 (TOT) 6.1.0 Android global-metadata.dat container.
    ///
    /// Two layers are applied in order (outer then inner), matching the analysis report:
    ///   L1 - 256 independent 64-byte CBC units, unit k at file offset k*0x25EC0. Each unit is
    ///        pt_0 = D(ct_0) ^ IV, pt_i = D(ct_i) ^ ct_{i-1} (i = 1..3). D is an 11-round
    ///        AES-decryption-shaped block cipher whose four 1 KiB Td tables are byte-scrambled and
    ///        whose 16-byte InvShiftRows index table lives in libunity.so (see below).
    ///   L2 - a per-string-literal stream cipher over the 0x158..0xEBC7A stringLiteralData range.
    ///        Its keystream comes from a metadata-seeded MT19937-64 table.
    ///
    /// Binary-derived constants
    /// ------------------------
    /// D needs 4224 bytes that are only present in libunity.so (a file the plugin's Load(Stream, ...)
    /// never receives, since the CLI supplies libil2cpp.so):
    ///   T0..T3 : libunity.so file offsets 0x15DD5D8 / 0x15DD9D8 / 0x15DDDD8 / 0x15DE1D8 (1024 B each)
    ///   IDX    : libunity.so file offset 0x15DD458 (16 little-endian qwords)
    /// They are embedded as the resource "Il2CppInspector.Plugin.TOT.TotWhiteboxTables.bin"
    /// (4*1024 + 128 = 4224 bytes, sha256 3ce5c225e242c7f637c37e8a22859455a716bcc751699a3732695a50e72894bc).
    /// This is a deliberate trade-off: the alternative would be to make the caller supply libunity.so,
    /// which the plugin contract does not allow.
    ///
    /// The tail KDF also needs a hard-wired custom-ChaCha20 constant (MAGIC) that is reproduced
    /// algorithmically from constants recovered from libunity.so; no table is embedded for it.
    /// </summary>
    internal static class TotMetadataDecryptor
    {
        // ---- L1 framing ----
        private const int UnitStride = 0x25EC0;
        private const int UnitCount = 256;
        private const int UnitSize = 64;
        private static readonly byte[] Iv = Convert.FromHexString("a55ab39932c7a7ebb53e9bfeb0a22c51");

        // ---- tail key material ----
        private const int TailSize = 0x4000;
        private const int KeyScheduleSize = 0xB00;

        // ---- L2 framing ----
        internal const int LiteralDataOffset = 0x158;
        internal const int LiteralIndexOffset = 0xEBC7C;
        internal const int LiteralCount = 36670;
        internal const int LiteralBlobSize = 965410; // 0xEBC7A - 0x158
        private const int LiteralIndexStride = 8;
        private const int LiteralClassPeriod = 10240;
        private const int TableBytePeriod = 0x5000; // 20480
        private const int MtWordCount = 2560;
        private const int MtDiscard = 6;

        private static readonly uint[] T0 = new uint[256];
        private static readonly uint[] T1 = new uint[256];
        private static readonly uint[] T2 = new uint[256];
        private static readonly uint[] T3 = new uint[256];
        private static readonly int[] Index = new int[16];
        private static readonly byte[] InvSbox = new byte[256];
        private static readonly byte[] Magic = BuildMagic();

        // Round keys depend on the file tail; cache the last derived set for repeated loads.
        private static byte[] cachedTail;
        private static byte[] cachedRoundKeys;

        static TotMetadataDecryptor()
        {
            using var stream =
                typeof(TotMetadataDecryptor).Assembly.GetManifestResourceStream("Il2CppInspector.Plugin.TOT.TotWhiteboxTables.bin")
                ?? throw new InvalidDataException("TOT white-box table resource is missing.");
            var tables = new byte[4224];
            stream.ReadExactly(tables);

            for (var i = 0; i < 256; i++)
            {
                T0[i] = BinaryPrimitives.ReadUInt32LittleEndian(tables.AsSpan(i * 4));
                T1[i] = BinaryPrimitives.ReadUInt32LittleEndian(tables.AsSpan(1024 + i * 4));
                T2[i] = BinaryPrimitives.ReadUInt32LittleEndian(tables.AsSpan(2048 + i * 4));
                T3[i] = BinaryPrimitives.ReadUInt32LittleEndian(tables.AsSpan(3072 + i * 4));
            }

            for (var i = 0; i < 16; i++)
            {
                Index[i] = (int)BinaryPrimitives.ReadUInt64LittleEndian(tables.AsSpan(4096 + i * 8));
            }

            BuildInvSbox();
        }

        /// <summary>Decrypts the container and returns the fully plaintext bytes.</summary>
        internal static byte[] Decrypt(byte[] data, EventHandler<string> status)
        {
            var output = DecryptOuter(data, status);
            status?.Invoke(null, "Decrypting TOT global-metadata.dat inner stringLiteral layer (L2)");
            InnerDecrypt(output);
            return output;
        }

        /// <summary>Removes only the outer (L1) layer; exposed for verification.</summary>
        internal static byte[] DecryptOuter(byte[] data, EventHandler<string> status = null)
        {
            if (data.Length < UnitStride * UnitCount || data.Length < TailSize)
            {
                throw new InvalidDataException("TOT metadata is too small to contain the encrypted units.");
            }

            status?.Invoke(null, "Decrypting TOT global-metadata.dat outer layer (L1)");
            var output = (byte[])data.Clone();
            var roundKeys = RoundKeysForTail(data.AsSpan(data.Length - TailSize));
            OuterDecrypt(output, roundKeys);
            return output;
        }

        // ------------------------------------------------------------------ L1

        private static void OuterDecrypt(byte[] data, byte[] roundKeys)
        {
            Span<byte> cipherBlock = stackalloc byte[16];
            Span<byte> plainBlock = stackalloc byte[16];
            Span<byte> previous = stackalloc byte[16];
            Span<byte> decrypted = stackalloc byte[16];

            for (var unit = 0; unit < UnitCount; unit++)
            {
                var unitOffset = unit * UnitStride;
                Iv.CopyTo(previous);
                for (var block = 0; block < 4; block++)
                {
                    var offset = unitOffset + block * 16;
                    data.AsSpan(offset, 16).CopyTo(cipherBlock);
                    D(cipherBlock, decrypted, roundKeys);
                    for (var i = 0; i < 16; i++)
                    {
                        plainBlock[i] = (byte)(decrypted[i] ^ previous[i]);
                    }

                    plainBlock.CopyTo(data.AsSpan(offset, 16));
                    cipherBlock.CopyTo(previous);
                }
            }
        }

        /// <summary>The white-box block cipher D (sub_FE3FF4): 11-round AES-decryption shape.</summary>
        private static void D(ReadOnlySpan<byte> ct, Span<byte> output, byte[] roundKeys)
        {
            Span<byte> state = stackalloc byte[16];
            Span<byte> next = stackalloc byte[16];

            for (var i = 0; i < 16; i++)
            {
                state[i] = (byte)(ct[i] ^ roundKeys[i]);
            }

            for (var round = 1; round <= 9; round++)
            {
                var rk = round * 16;
                for (var c = 0; c < 4; c++)
                {
                    var word = T0[state[Index[4 * c]]] ^ T1[state[Index[4 * c + 1]]] ^ T2[state[Index[4 * c + 2]]] ^ T3[state[Index[4 * c + 3]]];
                    next[4 * c] = (byte)(word ^ roundKeys[rk + 4 * c]);
                    next[4 * c + 1] = (byte)((word >> 8) ^ roundKeys[rk + 4 * c + 1]);
                    next[4 * c + 2] = (byte)((word >> 16) ^ roundKeys[rk + 4 * c + 2]);
                    next[4 * c + 3] = (byte)((word >> 24) ^ roundKeys[rk + 4 * c + 3]);
                }

                next.CopyTo(state);
            }

            for (var i = 0; i < 16; i++)
            {
                output[i] = (byte)(InvSbox[state[Index[i]]] ^ roundKeys[160 + i]);
            }
        }

        // ---------------------------------------------------------- round keys

        private static byte[] RoundKeysForTail(ReadOnlySpan<byte> tail)
        {
            var tailBytes = tail.ToArray();
            if (cachedRoundKeys != null && tailBytes.AsSpan().SequenceEqual(cachedTail))
            {
                return cachedRoundKeys;
            }

            var ks = KeySchedule(tailBytes);
            var rk = new byte[176];
            for (var r = 0; r < 11; r++)
            {
                for (var i = 0; i < 16; i++)
                {
                    var offset = 256 * r + 16 * i;
                    byte value = 0;
                    for (var j = 0; j < 16; j++)
                    {
                        value ^= (byte)(ks[offset + j] ^ Magic[offset + j]);
                    }

                    rk[r * 16 + i] = value;
                }
            }

            cachedTail = tailBytes;
            cachedRoundKeys = rk;
            return rk;
        }

        /// <summary>
        /// ks[j] = MAGIC[j] ^ dataB[j] ^ dataC[j] ^ k16B[j%16] ^ k16C[j%16] ^ CORR[j].
        /// The gate (tail[0xC8..0xCC) == FC2E 2CFE) selects the valid path; otherwise ks == MAGIC.
        /// </summary>
        internal static byte[] KeySchedule(byte[] tail)
        {
            if (tail.Length != TailSize)
            {
                throw new ArgumentException("TOT key-schedule tail must be exactly 0x4000 bytes.", nameof(tail));
            }

            if (BinaryPrimitives.ReadUInt16LittleEndian(tail.AsSpan(0xC8)) != TotMetadataDetector.GateA || BinaryPrimitives.ReadUInt16LittleEndian(tail.AsSpan(0xCA)) != TotMetadataDetector.GateB)
            {
                return FallbackKeySchedule();
            }

            var offset = BinaryPrimitives.ReadUInt16LittleEndian(tail.AsSpan(0xD2));
            if (offset + 0x10 + KeyScheduleSize > TailSize || 0x3010 + KeyScheduleSize > TailSize)
            {
                return FallbackKeySchedule();
            }

            var dataB = tail.AsSpan(offset + 0x10, KeyScheduleSize);
            var dataC = tail.AsSpan(0x3010, KeyScheduleSize);
            var ks = FallbackKeySchedule();
            for (var j = 0; j < KeyScheduleSize; j++)
            {
                ks[j] ^= (byte)(dataB[j] ^ dataC[j] ^ tail[offset + j % 16] ^ tail[0x3000 + j % 16]);
            }

            // CORR cancels the fallback residual on the valid path, exactly as in the reference.
            ks[0xAF8] ^= 0xA0;
            ks[0xAF9] ^= 0x76;
            ks[0xAFC] ^= 0x03;
            return ks;
        }

        /// <summary>The fallback key schedule (ks == MAGIC) is the residual-adjusted ChaCha constant.</summary>
        private static byte[] FallbackKeySchedule()
        {
            var ks = (byte[])Magic.Clone();
            ks[0xAF8] ^= 0xA0;
            ks[0xAF9] ^= 0x76;
            ks[0xAFC] ^= 0x03;
            return ks;
        }

        /// <summary>
        /// MAGIC is a fixed 0xB00 constant: the 64-byte state of a hard-wired custom ChaCha20 followed by
        /// the same ChaCha block repeated 43 times (the counter never advances). Recovered from libunity.so.
        /// </summary>
        private static byte[] BuildMagic()
        {
            var state = new uint[16];
            var constants = Convert.FromHexString("deadcafefaceb00cdeadcafefaceb00c");
            var key = Convert.FromHexString("6e42ddb9a5b1eb263678de613dea53cb4bdf593d1d461a648aa4c64ecae00165");
            var nonce = Convert.FromHexString("cfd04ca7f78dd1d0");

            for (var i = 0; i < 4; i++)
            {
                state[i] = BinaryPrimitives.ReadUInt32LittleEndian(constants.AsSpan(i * 4));
            }

            for (var i = 0; i < 8; i++)
            {
                state[4 + i] = BinaryPrimitives.ReadUInt32LittleEndian(key.AsSpan(i * 4));
            }

            state[12] = 0;
            state[13] = 0;
            state[14] = BinaryPrimitives.ReadUInt32LittleEndian(nonce);
            state[15] = BinaryPrimitives.ReadUInt32LittleEndian(nonce.AsSpan(4));

            var stateBytes = new byte[64];
            for (var i = 0; i < 16; i++)
            {
                BinaryPrimitives.WriteUInt32LittleEndian(stateBytes.AsSpan(i * 4), state[i]);
            }

            var block = ChaChaBlock(state);
            var magic = new byte[KeyScheduleSize];
            stateBytes.CopyTo(magic, 0);
            for (var offset = 64; offset < KeyScheduleSize; offset += 64)
            {
                block.CopyTo(magic, offset);
            }

            return magic;
        }

        private static byte[] ChaChaBlock(uint[] state)
        {
            var working = (uint[])state.Clone();
            for (var i = 0; i < 10; i++)
            {
                QuarterRound(working, 0, 4, 8, 12);
                QuarterRound(working, 1, 5, 9, 13);
                QuarterRound(working, 2, 6, 10, 14);
                QuarterRound(working, 3, 7, 11, 15);
                QuarterRound(working, 0, 5, 10, 15);
                QuarterRound(working, 1, 6, 11, 12);
                QuarterRound(working, 2, 7, 8, 13);
                QuarterRound(working, 3, 4, 9, 14);
            }

            var output = new byte[64];
            for (var i = 0; i < 16; i++)
            {
                BinaryPrimitives.WriteUInt32LittleEndian(output.AsSpan(i * 4), unchecked(working[i] + state[i]));
            }

            return output;
        }

        private static void QuarterRound(uint[] s, int a, int b, int c, int d)
        {
            s[a] += s[b];
            s[d] = BitOperations.RotateLeft(s[d] ^ s[a], 16);
            s[c] += s[d];
            s[b] = BitOperations.RotateLeft(s[b] ^ s[c], 12);
            s[a] += s[b];
            s[d] = BitOperations.RotateLeft(s[d] ^ s[a], 8);
            s[c] += s[d];
            s[b] = BitOperations.RotateLeft(s[b] ^ s[c], 7);
        }

        // ------------------------------------------------------------------ L2

        private static void InnerDecrypt(byte[] data)
        {
            var seed = SeedFromMetadata(data);
            var table = BuildKeystreamTable(seed);
            var tb = new byte[TableBytePeriod];
            for (var i = 0; i < MtWordCount; i++)
            {
                BinaryPrimitives.WriteUInt64LittleEndian(tb.AsSpan(i * 8), table[i]);
            }

            for (var literal = 0; literal < LiteralCount; literal++)
            {
                var index = LiteralIndexOffset + literal * LiteralIndexStride;
                var dataIndex = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(index));
                var length = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(index + 4));
                if (dataIndex + length > LiteralBlobSize)
                {
                    throw new InvalidDataException("TOT string literal index table is out of range.");
                }

                var literalClass = literal % LiteralClassPeriod;
                var offset = LiteralDataOffset + (int)dataIndex;
                for (var k = 0; k < length; k++)
                {
                    var t1 = tb[(k + 0x1400) % TableBytePeriod];
                    var t2 = tb[literalClass + (k % LiteralClassPeriod)];
                    var ks = (byte)(t1 ^ (byte)((sbyte)t2 + (sbyte)(k & 0xFF)));
                    data[offset + k] ^= ks;
                }
            }
        }

        /// <summary>
        /// seed = (TABLE[idx_hi] &lt;&lt; 32) | TABLE[idx_lo], idx_hi = u32(0x60) &amp; 0xF,
        /// idx_lo = (u32(0x14) &amp; 0xF) + 2, over 18 packer-randomised header slots.
        /// </summary>
        internal static ulong SeedFromMetadata(byte[] data)
        {
            int[] order = [0x60, 0x64, 0x68, 0x6C, 0x140, 0x144, 0x148, 0x14C, 0x100, 0x104, 0x108, 0x10C, 0xF0, 0xF4, 0x08, 0x0C, 0x10, 0x14];
            var table = new uint[order.Length];
            for (var i = 0; i < order.Length; i++)
            {
                table[i] = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(order[i]));
            }

            var high = table[BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(0x60)) & 0xF];
            var low = table[(BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(0x14)) & 0xF) + 2];
            return ((ulong)high << 32) | low;
        }

        /// <summary>
        /// The 2560-word table is MT19937-64 outputs #6..#2565. The twist is the in-place forward variant
        /// used by libunity (mt[(i+156)%312] may already have been updated this pass), NOT the textbook
        /// two-array version.
        /// </summary>
        internal static ulong[] BuildKeystreamTable(ulong seed)
        {
            const int n = 312;
            const int m = 156;
            const ulong a = 0xB5026F5AA96619E9;
            const ulong upperMask = 0xFFFFFFFF80000000;
            const ulong lowerMask = 0x7FFFFFFF;
            const ulong multiplier = 6364136223846793005;

            var mt = new ulong[n];
            mt[0] = seed;
            for (var i = 1; i < n; i++)
            {
                mt[i] = unchecked(multiplier * (mt[i - 1] ^ (mt[i - 1] >> 62)) + (ulong)i);
            }

            var index = n;
            var output = new ulong[MtDiscard + MtWordCount];
            for (var produced = 0; produced < output.Length; produced++)
            {
                if (index >= n)
                {
                    for (var i = 0; i < n; i++)
                    {
                        var x = (mt[i] & upperMask) | (mt[(i + 1) % n] & lowerMask);
                        mt[i] = mt[(i + m) % n] ^ ((x >> 1) ^ ((x & 1) != 0 ? a : 0));
                    }

                    index = 0;
                }

                output[produced] = Temper(mt[index++]);
            }

            return output[MtDiscard..];
        }

        private static ulong Temper(ulong y)
        {
            y ^= (y >> 29) & 0x5555555555555555;
            y ^= (y << 17) & 0x71D67FFFEDA60000;
            y ^= (y << 37) & 0xFFF7EEE000000000;
            y ^= y >> 43;
            return y;
        }

        // ------------------------------------------------------------- AES tables

        private static void BuildInvSbox()
        {
            var sbox = new byte[256];
            var inverse = new byte[256];
            for (var i = 1; i < 256; i++)
            {
                for (var j = 1; j < 256; j++)
                {
                    if (GfMultiply((byte)i, (byte)j) == 1)
                    {
                        inverse[i] = (byte)j;
                        break;
                    }
                }
            }

            for (var i = 0; i < 256; i++)
            {
                var x = inverse[i];
                var value = 0;
                for (var b = 0; b < 8; b++)
                {
                    var bit = ((x >> b) ^ (x >> ((b + 4) % 8)) ^ (x >> ((b + 5) % 8)) ^ (x >> ((b + 6) % 8)) ^ (x >> ((b + 7) % 8)) ^ (0x63 >> b)) & 1;
                    value |= bit << b;
                }

                sbox[i] = (byte)value;
            }

            for (var i = 0; i < 256; i++)
            {
                InvSbox[sbox[i]] = (byte)i;
            }
        }

        private static int GfMultiply(byte a, byte b)
        {
            var result = 0;
            int x = a;
            int y = b;
            for (var i = 0; i < 8; i++)
            {
                if ((y & 1) != 0)
                {
                    result ^= x;
                }

                var high = x & 0x80;
                x = (x << 1) & 0xFF;
                if (high != 0)
                {
                    x ^= 0x1B;
                }

                y >>= 1;
            }

            return result;
        }

        /// <summary>Fully standalone helper used by tests: decrypts an arbitrary container.</summary>
        internal static byte[] Decrypt(byte[] data) => Decrypt(data, null);
    }
}
