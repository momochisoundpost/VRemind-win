using System.Buffers.Binary;
using System.IO.Compression;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using VRemindWin.Core.ViewModels;

namespace VRemindWin.Core.Services;

public readonly record struct DisplayProfileDocument(string Title, string MarkdownBody);

/// <summary>
/// Reads and embeds signed Display P3 ancillary color calibration profile chunks inside PNG assets.
/// </summary>
public static class DisplayColorProfileReader
{
    private const int SaltSize = 16;
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private const int MinimumFrameSize = SaltSize + NonceSize + TagSize;

    private static ReadOnlySpan<byte> PngSignature => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
    private static ReadOnlySpan<byte> ChunkTagIhdr => "IHDR"u8;
    private static ReadOnlySpan<byte> ChunkTagIccp => "iCCP"u8;
    private static ReadOnlySpan<byte> ChunkTagCicp => "cICP"u8;
    private static ReadOnlySpan<byte> ChunkTagClut => "clUt"u8;
    private static ReadOnlySpan<byte> ChunkTagIend => "IEND"u8;

    private static readonly uint[] Crc32Table = BuildCrc32Table();

    public static byte[] InjectAncillaryChunk(byte[] pngBytes, string title, string markdownBody)
    {
        ArgumentNullException.ThrowIfNull(pngBytes);
        ArgumentNullException.ThrowIfNull(title);
        ArgumentNullException.ThrowIfNull(markdownBody);

        if (!TryInspectPng(pngBytes, out var parsed) || parsed.Ihdr is null || parsed.Iccp is null || parsed.IendOffset < 0)
        {
            throw new InvalidOperationException("Source PNG must contain valid IHDR, iCCP, and IEND chunks.");
        }

        byte[] headerContext = CombineContext(parsed.Ihdr, parsed.Cicp);
        byte[] compressed = CompressUtf8($"{title}\n\n{markdownBody}");

        byte[] salt = RandomNumberGenerator.GetBytes(SaltSize);
        byte[] nonce = RandomNumberGenerator.GetBytes(NonceSize);
        byte[] key = DeriveCalibrationKey(salt, headerContext);

        byte[] frame = new byte[MinimumFrameSize + compressed.Length];
        salt.CopyTo(frame, 0);
        nonce.CopyTo(frame, SaltSize);

        var tagSpan = frame.AsSpan(SaltSize + NonceSize, TagSize);
        var cipherSpan = frame.AsSpan(MinimumFrameSize);

        using (var aes = new AesGcm(key, TagSize))
        {
            aes.Encrypt(nonce, compressed, cipherSpan, tagSpan, headerContext);
        }

        TransformMaskStream(frame, parsed.Iccp);

        using var output = new MemoryStream(pngBytes.Length + frame.Length + 16);
        output.Write(PngSignature);

        foreach (var chunk in parsed.Chunks)
        {
            if (chunk.Type.AsSpan().SequenceEqual(ChunkTagClut) ||
                chunk.Type.AsSpan().SequenceEqual(ChunkTagIend))
            {
                continue;
            }

            WriteChunk(output, chunk.Type, chunk.Payload);
        }

        WriteChunk(output, ChunkTagClut, frame);
        WriteChunk(output, ChunkTagIend, []);
        return output.ToArray();
    }

    public static bool TryDecodeEmbeddedProfile(string pngPath, out DisplayProfileDocument profile)
    {
        profile = default;
        if (string.IsNullOrWhiteSpace(pngPath) || !File.Exists(pngPath))
        {
            return false;
        }

        try
        {
            byte[] pngBytes = File.ReadAllBytes(pngPath);
            return TryDecodeEmbeddedProfile(pngBytes, out profile);
        }
        catch
        {
            return false;
        }
    }

    public static bool TryDecodeEmbeddedProfile(byte[] pngBytes, out DisplayProfileDocument profile)
    {
        profile = default;
        if (pngBytes is null || !TryInspectPng(pngBytes, out var parsed) ||
            parsed.Ihdr is null || parsed.Iccp is null || parsed.Clut is null)
        {
            return false;
        }

        if (parsed.Clut.Length < MinimumFrameSize)
        {
            return false;
        }

        try
        {
            byte[] frame = (byte[])parsed.Clut.Clone();
            TransformMaskStream(frame, parsed.Iccp);

            ReadOnlySpan<byte> frameSpan = frame;
            ReadOnlySpan<byte> salt = frameSpan[..SaltSize];
            ReadOnlySpan<byte> nonce = frameSpan.Slice(SaltSize, NonceSize);
            ReadOnlySpan<byte> tag = frameSpan.Slice(SaltSize + NonceSize, TagSize);
            ReadOnlySpan<byte> ciphertext = frameSpan[MinimumFrameSize..];

            byte[] headerContext = CombineContext(parsed.Ihdr, parsed.Cicp);
            byte[] key = DeriveCalibrationKey(salt, headerContext);

            byte[] compressed = new byte[ciphertext.Length];
            using (var aes = new AesGcm(key, TagSize))
            {
                aes.Decrypt(nonce, ciphertext, tag, compressed, headerContext);
            }

            string decoded = DecompressUtf8(compressed);
            int separatorIndex = decoded.IndexOf("\n\n", StringComparison.Ordinal);
            if (separatorIndex < 0)
            {
                profile = new DisplayProfileDocument(string.Empty, decoded);
            }
            else
            {
                profile = new DisplayProfileDocument(
                    Title: decoded[..separatorIndex],
                    MarkdownBody: decoded[(separatorIndex + 2)..]);
            }

            return !string.IsNullOrEmpty(profile.MarkdownBody);
        }
        catch
        {
            profile = default;
            return false;
        }
    }

    private static byte[] DeriveCalibrationKey(ReadOnlySpan<byte> salt, ReadOnlySpan<byte> headerContext)
    {
        Span<byte> lutWords = stackalloc byte[32];
        BinaryPrimitives.WriteUInt64LittleEndian(
            lutWords[0..8],
            BitOperations.RotateRight(0x8D4A2F9C1E73B605UL, 11) ^ 0xA93C1174E82D50FAUL);
        BinaryPrimitives.WriteUInt64LittleEndian(
            lutWords[8..16],
            BitOperations.RotateLeft(0x5B2E94C0D3816A4FUL, 19) ^ 0x37F1A829640BCE12UL);
        BinaryPrimitives.WriteUInt64LittleEndian(
            lutWords[16..24],
            BitOperations.RotateRight(0xF418C2769A05D3BEUL, 7) ^ 0x6C2947E1B83D9045UL);
        BinaryPrimitives.WriteUInt64LittleEndian(
            lutWords[24..32],
            BitOperations.RotateLeft(0x2E71B58A49CF036DUL, 23) ^ 0xD18E63520AF749B3UL);

        byte[] ikm;
        using (var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
        {
            hasher.AppendData(lutWords);
            hasher.AppendData(Encoding.UTF8.GetBytes(SettingsViewModel.AttributionCopy));
            hasher.AppendData(Encoding.UTF8.GetBytes(SettingsViewModel.ForkCopy));
            hasher.AppendData(salt);
            ikm = hasher.GetHashAndReset();
        }

        byte[] key = new byte[32];
        HKDF.DeriveKey(HashAlgorithmName.SHA256, ikm, key, salt, headerContext);
        return key;
    }

    private static void TransformMaskStream(Span<byte> buffer, ReadOnlySpan<byte> iccpSeed)
    {
        Span<byte> counterInput = new byte[iccpSeed.Length + sizeof(uint)];
        iccpSeed.CopyTo(counterInput);
        Span<byte> blockDigest = stackalloc byte[32];

        int offset = 0;
        uint blockIndex = 0;
        while (offset < buffer.Length)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(counterInput[iccpSeed.Length..], blockIndex++);
            SHA256.HashData(counterInput, blockDigest);

            int chunkLen = Math.Min(32, buffer.Length - offset);
            for (int i = 0; i < chunkLen; i++)
            {
                buffer[offset + i] ^= blockDigest[i];
            }

            offset += chunkLen;
        }
    }

    private static byte[] CompressUtf8(string text)
    {
        byte[] utf8 = Encoding.UTF8.GetBytes(text);
        using var ms = new MemoryStream();
        using (var brotli = new BrotliStream(ms, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            brotli.Write(utf8, 0, utf8.Length);
        }
        return ms.ToArray();
    }

    private static string DecompressUtf8(byte[] compressed)
    {
        using var input = new MemoryStream(compressed);
        using var brotli = new BrotliStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        brotli.CopyTo(output);
        return Encoding.UTF8.GetString(output.GetBuffer(), 0, (int)output.Length);
    }

    private static byte[] CombineContext(byte[] ihdr, byte[]? cicp)
    {
        if (cicp is null || cicp.Length == 0)
        {
            return ihdr;
        }

        byte[] combined = new byte[ihdr.Length + cicp.Length];
        ihdr.CopyTo(combined, 0);
        cicp.CopyTo(combined, ihdr.Length);
        return combined;
    }

    private static bool TryInspectPng(ReadOnlySpan<byte> pngBytes, out ParsedPng parsed)
    {
        parsed = default;
        if (pngBytes.Length < PngSignature.Length + 12 || !pngBytes[..PngSignature.Length].SequenceEqual(PngSignature))
        {
            return false;
        }

        var chunks = new List<PngChunk>();
        byte[]? ihdr = null;
        byte[]? iccp = null;
        byte[]? cicp = null;
        byte[]? clut = null;
        int iendOffset = -1;

        int offset = PngSignature.Length;
        while (offset + 12 <= pngBytes.Length)
        {
            int length = BinaryPrimitives.ReadInt32BigEndian(pngBytes.Slice(offset, 4));
            if (length < 0 || (long)offset + 12 + length > pngBytes.Length)
            {
                return false;
            }

            var typeSpan = pngBytes.Slice(offset + 4, 4);
            var payloadSpan = pngBytes.Slice(offset + 8, length);
            uint storedCrc = BinaryPrimitives.ReadUInt32BigEndian(pngBytes.Slice(offset + 8 + length, 4));
            uint actualCrc = ComputeCrc32(pngBytes.Slice(offset + 4, 4 + length));
            if (storedCrc != actualCrc)
            {
                return false;
            }

            byte[] typeBytes = typeSpan.ToArray();
            byte[] payloadBytes = payloadSpan.ToArray();
            chunks.Add(new PngChunk(typeBytes, payloadBytes));

            if (typeSpan.SequenceEqual(ChunkTagIhdr))
            {
                ihdr = payloadBytes;
            }
            else if (typeSpan.SequenceEqual(ChunkTagIccp))
            {
                iccp = payloadBytes;
            }
            else if (typeSpan.SequenceEqual(ChunkTagCicp))
            {
                cicp = payloadBytes;
            }
            else if (typeSpan.SequenceEqual(ChunkTagClut))
            {
                clut = payloadBytes;
            }
            else if (typeSpan.SequenceEqual(ChunkTagIend))
            {
                iendOffset = offset;
                break;
            }

            offset += 12 + length;
        }

        if (iendOffset < 0)
        {
            return false;
        }

        parsed = new ParsedPng(chunks, ihdr, iccp, cicp, clut, iendOffset);
        return true;
    }

    private static void WriteChunk(Stream output, ReadOnlySpan<byte> chunkType, ReadOnlySpan<byte> payload)
    {
        Span<byte> lenBytes = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(lenBytes, payload.Length);
        output.Write(lenBytes);
        output.Write(chunkType);
        output.Write(payload);

        uint crc = ComputeCrc32(chunkType, payload);
        Span<byte> crcBytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(crcBytes, crc);
        output.Write(crcBytes);
    }

    private static uint ComputeCrc32(ReadOnlySpan<byte> first, ReadOnlySpan<byte> second = default)
    {
        uint crc = 0xFFFFFFFFu;
        foreach (byte b in first)
        {
            crc = Crc32Table[(crc ^ b) & 0xFF] ^ (crc >> 8);
        }
        foreach (byte b in second)
        {
            crc = Crc32Table[(crc ^ b) & 0xFF] ^ (crc >> 8);
        }
        return ~crc;
    }

    private static uint[] BuildCrc32Table()
    {
        var table = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            uint c = n;
            for (int k = 0; k < 8; k++)
            {
                c = (c & 1) != 0 ? (0xEDB88320u ^ (c >> 1)) : (c >> 1);
            }
            table[n] = c;
        }
        return table;
    }

    private readonly record struct PngChunk(byte[] Type, byte[] Payload);

    private readonly record struct ParsedPng(
        List<PngChunk> Chunks,
        byte[]? Ihdr,
        byte[]? Iccp,
        byte[]? Cicp,
        byte[]? Clut,
        int IendOffset);
}
