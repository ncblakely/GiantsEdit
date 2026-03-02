using System.Text;

namespace GiantsEdit.Core.Formats;

/// <summary>
/// Reads and writes the native GZP archive format used by Giants: Citizen Kabuto.
/// This is NOT standard ZIP — it has a custom header (magic $6608F101)
/// and uses LZ77 compression.
/// </summary>
public static class GzpArchive
{
    private const uint GzpMagic = 0x6608F101;
    private const int FileEntryHeaderSize = 16;

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

        _ = br.ReadInt32(); // index header field (unused)
        int entryCount = br.ReadInt32();

        for (int i = 0; i < entryCount; i++)
        {
            int size = br.ReadInt32();
            int sizeUncompressed = br.ReadInt32();
            _ = br.ReadInt32(); // unknown field
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

    /// <summary>
    /// Rebuilds a GZP archive, replacing a single entry's data (stored uncompressed)
    /// while copying all other entries byte-for-byte from the original.
    /// </summary>
    public static void ReplaceEntry(string gzpPath, string entryName, byte[] newData)
    {
        var entries = BuildIndex(gzpPath);
        string tempPath = gzpPath + ".tmp";

        using (var fs = File.Create(tempPath))
        using (var bw = new BinaryWriter(fs))
        {
            // File header (placeholder diroffset, patched later)
            bw.Write(GzpMagic);
            bw.Write(0); // diroffset placeholder

            // Track rebuilt entries: (name, offset, cmpSize, ucmpSize, cmpType)
            var rebuilt = new List<(string Name, int Offset, int CmpSize, int UcmpSize, byte CmpType)>();

            foreach (var (name, entry) in entries)
            {
                int offset = (int)fs.Position;

                if (string.Equals(name, entryName, StringComparison.OrdinalIgnoreCase))
                {
                    // Write new data uncompressed
                    int totalSize = FileEntryHeaderSize + newData.Length;
                    bw.Write(totalSize);
                    bw.Write(newData.Length);
                    bw.Write(0);               // filetime
                    bw.Write(2);
                    bw.Write(newData);
                    rebuilt.Add((name, offset, totalSize, newData.Length, 2));
                }
                else
                {
                    // Copy original entry data (header + payload) from source
                    int rawOffset = (int)entry.DataOffset - FileEntryHeaderSize;
                    int rawSize = entry.CompressedSize + FileEntryHeaderSize;

                    using var src = File.OpenRead(entry.SourcePath);
                    src.Seek(rawOffset, SeekOrigin.Begin);
                    byte[] raw = new byte[rawSize];
                    src.ReadExactly(raw);
                    bw.Write(raw);
                    rebuilt.Add((name, offset, rawSize, entry.UncompressedSize,
                        entry.IsCompressed ? (byte)1 : (byte)2));
                }
            }

            // Write directory
            int dirOffset = (int)fs.Position;
            bw.Write(0); // freeoffset (unused)
            bw.Write(rebuilt.Count);

            foreach (var (name, offset, compressedSize, uncompressedSize, compressionType) in rebuilt)
            {
                byte[] nameBytes = Encoding.ASCII.GetBytes(name + '\0');
                bw.Write(compressedSize);
                bw.Write(uncompressedSize);
                bw.Write(0);           // filetime
                bw.Write(offset);
                bw.Write(compressionType);
                bw.Write((byte)nameBytes.Length);
                bw.Write(nameBytes);
            }

            // Patch diroffset in file header
            fs.Seek(4, SeekOrigin.Begin);
            bw.Write(dirOffset);
        }

        // Atomic replace
        File.Move(tempPath, gzpPath, overwrite: true);
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
