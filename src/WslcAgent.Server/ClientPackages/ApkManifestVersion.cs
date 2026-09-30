using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace WslcAgent.Server.ClientPackages;

/// <summary>
/// <c>versionName</c> and <c>versionCode</c> of an APK, read from its binary
/// <c>AndroidManifest.xml</c> (the Android resource XML format): the string
/// pool, the resource map and the <c>&lt;manifest&gt;</c> start element are
/// enough, nothing else in the document is decoded.
/// </summary>
public static class ApkManifestVersion
{
    private const ushort ChunkXml = 0x0003;
    private const ushort ChunkStringPool = 0x0001;
    private const ushort ChunkResourceMap = 0x0180;
    private const ushort ChunkStartElement = 0x0102;
    private const uint Utf8Flag = 1 << 8;
    private const byte TypeString = 0x03;
    private const byte TypeIntDec = 0x10;
    private const byte TypeIntHex = 0x11;
    private const uint AttrVersionCode = 0x0101021B;
    private const uint AttrVersionName = 0x0101021C;

    /// <summary>(versionName, versionCode), or ("", 0) when the file is not a readable APK.</summary>
    public static (string Version, int Build) Read(string path)
    {
        byte[] manifest;
        try
        {
            using var zip = ZipFile.OpenRead(path);
            var entry = zip.GetEntry("AndroidManifest.xml");
            if (entry is null)
            {
                return ("", 0);
            }

            using var stream = entry.Open();
            using var memory = new MemoryStream();
            stream.CopyTo(memory);
            manifest = memory.ToArray();
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            return ("", 0);
        }

        try
        {
            return Parse(manifest);
        }
        catch (Exception ex) when (ex is ArgumentOutOfRangeException or IndexOutOfRangeException or DecoderFallbackException)
        {
            return ("", 0);
        }
    }

    private static (string, int) Parse(ReadOnlySpan<byte> data)
    {
        if (data.Length < 8)
        {
            return ("", 0);
        }

        var offset = 0;
        if (U16(data, 0) == ChunkXml)
        {
            offset = U16(data, 2);
        }

        var strings = Array.Empty<string>();
        var resourceIds = Array.Empty<uint>();
        while (offset + 8 <= data.Length)
        {
            var type = U16(data, offset);
            var headerSize = U16(data, offset + 2);
            var size = (int)U32(data, offset + 4);
            if (size < 8 || offset + size > data.Length)
            {
                break;
            }

            var chunk = data.Slice(offset, size);
            switch (type)
            {
                case ChunkStringPool:
                    strings = StringPool(chunk);
                    break;
                case ChunkResourceMap:
                    var count = Math.Max((size - headerSize) / 4, 0);
                    resourceIds = new uint[count];
                    for (var i = 0; i < count; i++)
                    {
                        resourceIds[i] = U32(chunk, headerSize + i * 4);
                    }

                    break;
                case ChunkStartElement:
                    if (ElementName(chunk, strings) == "manifest")
                    {
                        return ManifestAttributes(chunk, strings, resourceIds);
                    }

                    break;
            }

            offset += size;
        }

        return ("", 0);
    }

    private static string[] StringPool(ReadOnlySpan<byte> chunk)
    {
        if (chunk.Length < 28)
        {
            return [];
        }

        var headerSize = U16(chunk, 2);
        var count = (int)U32(chunk, 8);
        var flags = U32(chunk, 16);
        var stringsStart = (int)U32(chunk, 20);
        if (count <= 0 || headerSize + count * 4 > chunk.Length)
        {
            return [];
        }

        var utf8 = (flags & Utf8Flag) != 0;
        var result = new string[count];
        for (var i = 0; i < count; i++)
        {
            var position = stringsStart + (int)U32(chunk, headerSize + i * 4);
            result[i] = position < chunk.Length ? (utf8 ? Utf8String(chunk, position) : Utf16String(chunk, position)) : "";
        }

        return result;
    }

    private static string Utf8String(ReadOnlySpan<byte> chunk, int position)
    {
        (position, _) = Utf8Length(chunk, position);
        (position, var byteLength) = Utf8Length(chunk, position);
        byteLength = Math.Min(byteLength, chunk.Length - position);
        return Encoding.UTF8.GetString(chunk.Slice(position, Math.Max(byteLength, 0)));
    }

    private static (int Position, int Length) Utf8Length(ReadOnlySpan<byte> data, int position)
    {
        if (position >= data.Length)
        {
            return (position, 0);
        }

        int first = data[position++];
        if ((first & 0x80) == 0)
        {
            return (position, first);
        }

        return position < data.Length ? (position + 1, ((first & 0x7F) << 8) | data[position]) : (position, 0);
    }

    private static string Utf16String(ReadOnlySpan<byte> chunk, int position)
    {
        if (position + 2 > chunk.Length)
        {
            return "";
        }

        int charLength = U16(chunk, position);
        if ((charLength & 0x8000) != 0)
        {
            charLength = ((charLength & 0x7FFF) << 16) | U16(chunk, position + 2);
            position += 4;
        }
        else
        {
            position += 2;
        }

        var byteLength = Math.Min(charLength * 2, chunk.Length - position);
        return Encoding.Unicode.GetString(chunk.Slice(position, Math.Max(byteLength, 0)));
    }

    private static string ElementName(ReadOnlySpan<byte> chunk, string[] strings) =>
        chunk.Length < 36 ? "" : PoolString(strings, U32(chunk, 20));

    private static (string, int) ManifestAttributes(ReadOnlySpan<byte> chunk, string[] strings, uint[] resourceIds)
    {
        var attributeStart = U16(chunk, 24);
        var attributeSize = U16(chunk, 26);
        var attributeCount = U16(chunk, 28);
        var version = "";
        var build = 0;
        var position = 16 + attributeStart;
        for (var i = 0; i < attributeCount && position + attributeSize <= chunk.Length; i++, position += attributeSize)
        {
            var nameIndex = U32(chunk, position + 4);
            var rawIndex = U32(chunk, position + 8);
            var dataType = chunk[position + 15];
            var value = U32(chunk, position + 16);
            var resourceId = nameIndex < resourceIds.Length ? resourceIds[nameIndex] : 0;
            var name = PoolString(strings, nameIndex);

            if (resourceId == AttrVersionName || name == "versionName")
            {
                version = dataType == TypeString
                    ? FirstNonEmpty(PoolString(strings, value), PoolString(strings, rawIndex))
                    : PoolString(strings, rawIndex);
            }
            else if (resourceId == AttrVersionCode || name == "versionCode")
            {
                build = dataType is TypeIntDec or TypeIntHex
                    ? (int)value
                    : int.TryParse(FirstNonEmpty(PoolString(strings, value), PoolString(strings, rawIndex)), out var parsed) ? parsed : 0;
            }
        }

        return (version, Math.Max(build, 0));
    }

    private static string FirstNonEmpty(string a, string b) => a.Length > 0 ? a : b;

    private static string PoolString(string[] strings, uint index) => index < strings.Length ? strings[index] : "";

    private static ushort U16(ReadOnlySpan<byte> data, int offset) => BinaryPrimitives.ReadUInt16LittleEndian(data[offset..]);

    private static uint U32(ReadOnlySpan<byte> data, int offset) => BinaryPrimitives.ReadUInt32LittleEndian(data[offset..]);
}
