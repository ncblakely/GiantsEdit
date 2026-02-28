using System.Text;

namespace GiantsEdit.Core.Formats;

/// <summary>
/// Reads the native GZP archive format used by Giants: Citizen Kabuto.
/// This is NOT standard ZIP — it has a custom header (magic $6608F101)
/// and uses LZ77 compression.
/// </summary>
public static class GzpArchive
{
    private const uint GzpMagic = 0x6608F101;

    /// <summary>
    /// Builds an index of all files in a GZP archive.
    /// </summary>
    public static Dictionary<string, GzpArchiveEntry> BuildIndex(string gzpPath)
    {
        var entries = new Dictionary<string, GzpArchiveEntry>(StringComparer.OrdinalIgnoreCase);

        using var fs = File.OpenRead(gzpPath);
        using var br = new BinaryReader(fs);

        uint magic = br.ReadUInt32();
        if (magic != GzpMagic)
            return entries;

        int indexOffset = br.ReadInt32();
        fs.Seek(indexOffset, SeekOrigin.Begin);

        int _indexUnknown = br.ReadInt32();
        int entryCount = br.ReadInt32();

        for (int i = 0; i < entryCount; i++)
        {
            int size = br.ReadInt32();
            int sizeUncompressed = br.ReadInt32();
            int _u1 = br.ReadInt32();
            int start = br.ReadInt32();
            byte compr = br.ReadByte();
            byte nameLength = br.ReadByte();

            byte[] nameBytes = br.ReadBytes(nameLength);
            // Name is null-terminated, trim the null
            int nullIdx = Array.IndexOf(nameBytes, (byte)0);
            string name = Encoding.ASCII.GetString(nameBytes, 0, nullIdx >= 0 ? nullIdx : nameLength);

            if (!entries.ContainsKey(name))
            {
                entries[name] = new GzpArchiveEntry
                {
                    Name = name,
                    SourcePath = gzpPath,
                    DataOffset = start + 16, // skip 16-byte per-file header
                    CompressedSize = size - 16,
                    UncompressedSize = sizeUncompressed,
                    IsCompressed = (compr & 3) == 1
                };
            }
        }

        return entries;
    }

    /// <summary>
    /// Extracts a file from a GZP archive using a pre-built entry.
    /// </summary>
    public static byte[] ExtractEntry(GzpArchiveEntry entry)
    {
        using var fs = File.OpenRead(entry.SourcePath);
        fs.Seek(entry.DataOffset, SeekOrigin.Begin);
        byte[] data = new byte[entry.CompressedSize];
        fs.ReadExactly(data);

        if (entry.IsCompressed)
            return GbsDecompressor.Decompress(data, 0, data.Length, entry.UncompressedSize);

        return data;
    }

    /// <summary>
    /// Scans all .gzp files in a directory and builds a unified file index.
    /// Files in earlier archives take precedence (first found wins).
    /// </summary>
    public static Dictionary<string, GzpArchiveEntry> BuildIndexFromDirectory(string binPath)
    {
        var combined = new Dictionary<string, GzpArchiveEntry>(StringComparer.OrdinalIgnoreCase);

        if (!Directory.Exists(binPath)) return combined;

        foreach (var gzpFile in Directory.GetFiles(binPath, "*.gzp"))
        {
            var entries = BuildIndex(gzpFile);
            foreach (var (name, entry) in entries)
            {
                combined.TryAdd(name, entry);
            }
        }

        return combined;
    }
}

public class GzpArchiveEntry
{
    public required string Name { get; init; }
    public required string SourcePath { get; init; }
    public required long DataOffset { get; init; }
    public required int CompressedSize { get; init; }
    public required int UncompressedSize { get; init; }
    public required bool IsCompressed { get; init; }
}
