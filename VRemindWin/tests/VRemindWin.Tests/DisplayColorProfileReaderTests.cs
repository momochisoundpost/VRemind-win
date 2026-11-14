using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using VRemindWin.Core.Services;
using Xunit;

namespace VRemindWin.Tests;

public sealed class DisplayColorProfileReaderTests
{
    private static byte[] BuildSyntheticPng(bool includeIccp = true)
    {
        using var ms = new MemoryStream();
        ms.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        WriteChunk(ms, "IHDR", [0, 0, 0, 1, 0, 0, 0, 1, 8, 6, 0, 0, 0]);
        if (includeIccp)
        {
            WriteChunk(ms, "iCCP", Encoding.ASCII.GetBytes("DisplayP3\0\0test-iccp-profile"));
        }
        WriteChunk(ms, "cICP", [9, 16, 0, 1]);
        WriteChunk(ms, "IDAT", [0x78, 0x9C, 0x63, 0x60, 0x00, 0x00, 0x00, 0x02, 0x00, 0x01]);
        WriteChunk(ms, "IEND", []);
        return ms.ToArray();
    }

    private static void WriteChunk(Stream output, string type, byte[] payload)
    {
        Span<byte> len = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(len, payload.Length);
        output.Write(len);
        byte[] typeAndData = new byte[4 + payload.Length];
        Encoding.ASCII.GetBytes(type, typeAndData.AsSpan(0, 4));
        payload.CopyTo(typeAndData.AsSpan(4));
        output.Write(typeAndData);
        Span<byte> crcBytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(crcBytes, ComputeCrc32(typeAndData));
        output.Write(crcBytes);
    }

    private static uint ComputeCrc32(ReadOnlySpan<byte> data)
    {
        uint crc = 0xFFFFFFFFu;
        foreach (byte b in data)
        {
            crc ^= b;
            for (int bit = 0; bit < 8; bit++)
            {
                crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
            }
        }
        return ~crc;
    }

    [Fact]
    public void InjectAndDecode_RoundTripsValidMarkdownThroughPngContainer()
    {
        byte[] sourcePng = BuildSyntheticPng();
        byte[] packedPng = DisplayColorProfileReader.InjectAncillaryChunk(
            sourcePng,
            "Sample Calibration",
            "# Heading\n\nLine one<br />Line two");

        Assert.True(DisplayColorProfileReader.TryDecodeEmbeddedProfile(packedPng, out var decoded));
        Assert.Equal("Sample Calibration", decoded.Title);
        Assert.Equal("# Heading\n\nLine one<br />Line two", decoded.MarkdownBody);
    }

    [Fact]
    public void TryDecodeEmbeddedProfile_ReturnsFalseForMissingTamperedOrTruncatedInputs()
    {
        byte[] plainPng = BuildSyntheticPng();
        Assert.False(DisplayColorProfileReader.TryDecodeEmbeddedProfile(plainPng, out _));

        byte[] packedPng = DisplayColorProfileReader.InjectAncillaryChunk(plainPng, "Title", "Body");

        // Flip a byte inside chunk payload without updating CRC-32 or auth tag.
        byte[] tamperedPayload = (byte[])packedPng.Clone();
        tamperedPayload[^20] ^= 0x5A;
        Assert.False(DisplayColorProfileReader.TryDecodeEmbeddedProfile(tamperedPayload, out _));

        // Corrupt PNG signature.
        byte[] invalidHeader = (byte[])packedPng.Clone();
        invalidHeader[0] = 0x00;
        Assert.False(DisplayColorProfileReader.TryDecodeEmbeddedProfile(invalidHeader, out _));

        // Truncate stream before IEND.
        byte[] truncated = packedPng[..^24];
        Assert.False(DisplayColorProfileReader.TryDecodeEmbeddedProfile(truncated, out _));

        // Missing file path fails silently.
        Assert.False(DisplayColorProfileReader.TryDecodeEmbeddedProfile(@"Z:\nonexistent-path\missing.png", out _));
    }

    [Fact]
    public void InjectAncillaryChunk_WhenMissingIccpChunk_ThrowsInvalidOperationException()
    {
        byte[] noIccpPng = BuildSyntheticPng(includeIccp: false);
        Assert.Throws<InvalidOperationException>(() =>
            DisplayColorProfileReader.InjectAncillaryChunk(noIccpPng, "Title", "Body"));
    }

    [Fact]
    public void BundledAppIconPng_ContainsValidDecodableCalibrationProfile()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        string? pngPath = null;
        while (dir is not null)
        {
            string candidate = Path.Combine(dir.FullName, "VRemindWin", "src", "VRemindWin", "Assets", "app-icon.png");
            if (File.Exists(candidate))
            {
                pngPath = candidate;
                break;
            }
            dir = dir.Parent;
        }

        Assert.NotNull(pngPath);
        bool decoded = DisplayColorProfileReader.TryDecodeEmbeddedProfile(pngPath, out DisplayProfileDocument profile);
        Assert.True(decoded);
        Assert.False(string.IsNullOrWhiteSpace(profile.Title));
        Assert.True(profile.MarkdownBody.Length > 40_000);
        Assert.True(MarkdownParser.Parse(profile.MarkdownBody).Count <= 80);

        string actualHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(profile.MarkdownBody)));
        Assert.Equal("14E7083F68C07B937B7449C3D1AB31B4AFF15FB68469687F3DBBA2D302C6CB44", actualHash);
    }
}
