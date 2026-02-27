using System.IO.Compression;

namespace GiantsEdit.Core.Formats;

/// <summary>
/// Reads and writes GZP archives (standard ZIP format) used by Giants: Citizen Kabuto.
/// The game engine uses .gzp extension but the format is standard ZIP/deflate.
/// </summary>
public static class GzpArchive
{
    /// <summary>
    /// Lists all entries in a GZP archive.
    /// </summary>
    public static IReadOnlyList<string> ListEntries(string path)
    {
        using var archive = ZipFile.OpenRead(path);
        return archive.Entries.Select(e => e.FullName).ToList();
    }

    /// <summary>
    /// Extracts a single file from the archive by name.
    /// Returns null if not found.
    /// </summary>
    public static byte[]? ExtractFile(string archivePath, string entryName)
    {
        using var archive = ZipFile.OpenRead(archivePath);
        var entry = archive.Entries.FirstOrDefault(e =>
            e.FullName.Equals(entryName, StringComparison.OrdinalIgnoreCase));

        if (entry == null) return null;

        using var stream = entry.Open();
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return ms.ToArray();
    }

    /// <summary>
    /// Extracts a single file from an in-memory archive.
    /// </summary>
    public static byte[]? ExtractFile(byte[] archiveData, string entryName)
    {
        using var ms = new MemoryStream(archiveData);
        using var archive = new ZipArchive(ms, ZipArchiveMode.Read);
        var entry = archive.Entries.FirstOrDefault(e =>
            e.FullName.Equals(entryName, StringComparison.OrdinalIgnoreCase));

        if (entry == null) return null;

        using var stream = entry.Open();
        using var output = new MemoryStream();
        stream.CopyTo(output);
        return output.ToArray();
    }

    /// <summary>
    /// Extracts all files from a GZP archive to a directory.
    /// </summary>
    public static void ExtractAll(string archivePath, string outputDir)
    {
        ZipFile.ExtractToDirectory(archivePath, outputDir, overwriteFiles: true);
    }

    /// <summary>
    /// Creates a GZP archive from a list of files.
    /// </summary>
    /// <param name="outputPath">Path for the output .gzp file.</param>
    /// <param name="files">Pairs of (entryName, fileData).</param>
    public static void Create(string outputPath, IEnumerable<(string EntryName, byte[] Data)> files)
    {
        using var fs = File.Create(outputPath);
        using var archive = new ZipArchive(fs, ZipArchiveMode.Create);
        foreach (var (name, data) in files)
        {
            var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
            using var stream = entry.Open();
            stream.Write(data);
        }
    }

    /// <summary>
    /// Creates a GZP archive from a directory.
    /// </summary>
    public static void CreateFromDirectory(string sourceDir, string outputPath)
    {
        ZipFile.CreateFromDirectory(sourceDir, outputPath, CompressionLevel.Optimal, includeBaseDirectory: false);
    }

    /// <summary>
    /// Adds or replaces a file within an existing GZP archive.
    /// </summary>
    public static void AddOrReplaceFile(string archivePath, string entryName, byte[] data)
    {
        using var archive = ZipFile.Open(archivePath, ZipArchiveMode.Update);

        // Remove existing entry if present
        var existing = archive.Entries.FirstOrDefault(e =>
            e.FullName.Equals(entryName, StringComparison.OrdinalIgnoreCase));
        existing?.Delete();

        var entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
        using var stream = entry.Open();
        stream.Write(data);
    }
}
