namespace GiantsEdit.Core.DataModel;

/// <summary>
/// Entry in the object inclusion list: maps a numeric object ID to
/// a friendly name, a 3D model file path, and the required include file.
/// Ported from Delphi's inclist.pas tIncludelistEntry.
/// </summary>
public readonly record struct ObjectCatalogEntry(int Id, string Name, string ModelPath, string IncludeFile);

/// <summary>
/// Provides lookup of game object definitions by ID.
/// In the original Delphi code, this was a massive 950-entry compile-time constant array.
/// Here it is loaded from an external JSON file for maintainability.
/// </summary>
public class ObjectCatalog
{
    private readonly List<ObjectCatalogEntry> _entries = [];
    private readonly Dictionary<int, List<ObjectCatalogEntry>> _byId = [];

    public IReadOnlyList<ObjectCatalogEntry> Entries => _entries;

    public void Add(ObjectCatalogEntry entry)
    {
        _entries.Add(entry);
        if (!_byId.TryGetValue(entry.Id, out var list))
        {
            list = [];
            _byId[entry.Id] = list;
        }
        list.Add(entry);
    }

    /// <summary>
    /// Gets all catalog entries for a given object ID.
    /// Multiple entries can exist for the same ID (different include contexts).
    /// </summary>
    public IReadOnlyList<ObjectCatalogEntry> GetById(int id)
    {
        return _byId.TryGetValue(id, out var list)
            ? list
            : [];
    }

    /// <summary>
    /// Gets the required include file for an object ID, or null if none is needed.
    /// </summary>
    public string? GetRequiredInclude(int id)
    {
        if (!_byId.TryGetValue(id, out var list) || list.Count == 0)
            return null;
        var inc = list[0].IncludeFile;
        return string.IsNullOrEmpty(inc) ? null : inc;
    }

    /// <summary>
    /// Loads the catalog from tab-separated lines (id\tname\tmodel\tincludefile).
    /// </summary>
    public static ObjectCatalog LoadFromTsv(IEnumerable<string> lines)
    {
        var catalog = new ObjectCatalog();
        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith('#'))
                continue;

            var parts = line.Split('\t');
            if (parts.Length >= 3 && int.TryParse(parts[0].Trim(), out int id))
            {
                string includeFile = parts.Length >= 4 ? parts[3].Trim() : "";
                catalog.Add(new ObjectCatalogEntry(id, parts[1].Trim(), parts[2].Trim(), includeFile));
            }
        }
        return catalog;
    }

    /// <summary>
    /// Loads the catalog from a TSV file.
    /// </summary>
    public static ObjectCatalog LoadFromFile(string path)
    {
        return LoadFromTsv(File.ReadAllLines(path));
    }
}
