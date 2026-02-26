using System.Globalization;

namespace GiantsEdit.Core.Formats;

/// <summary>
/// A vertex in a map object mesh.
/// </summary>
public struct MapObjVertex
{
    public float X, Y, Z;
    public byte R, G, B;
}

/// <summary>
/// A triangle in a map object mesh, defined by 3 vertices.
/// </summary>
public struct MapObjTriangle
{
    public MapObjVertex V0, V1, V2;
}

/// <summary>
/// A parsed map object: a list of triangles with vertex colors.
/// </summary>
public class MapObject
{
    public string Name { get; set; } = string.Empty;
    public List<MapObjTriangle> Triangles { get; } = [];
}

/// <summary>
/// Parses the text-based map object geometry format (Mapobj.txt).
/// Ported from Delphi's MapObjReader.pas.
/// </summary>
/// <remarks>
/// Format:
///   [BlockName]
///   &lt;Objects&gt;    integer IDs or "All"
///   &lt;Colors&gt;     r,g,b float entries (0-1 range)
///   &lt;Vertices&gt;   x,y,z,colorindex entries
///   &lt;Triangles&gt;  v0,v1,v2 vertex index entries
/// </remarks>
public class MapObjectReader
{
    /// <summary>
    /// Maps object coordinate/ID to the index of the MapObject in the result list.
    /// Range: roughly -256..4095. -1 means unassigned.
    /// </summary>
    public Dictionary<int, int> ObjectWrap { get; } = [];

    public List<MapObject> Objects { get; } = [];

    public void Load(IEnumerable<string> lines)
    {
        Objects.Clear();
        ObjectWrap.Clear();

        string currentBlock = string.Empty;
        string currentSection = string.Empty;
        var colors = new List<(byte R, byte G, byte B)>();
        var vertices = new List<MapObjVertex>();
        var triangleIndices = new List<(int V0, int V1, int V2)>();
        var objectIds = new List<int>();
        bool allObjects = false;

        foreach (var rawLine in lines)
        {
            string line = rawLine.Trim();
            if (string.IsNullOrEmpty(line) || line.StartsWith(';'))
                continue;

            if (line.StartsWith('['))
            {
                // Flush previous block
                if (currentBlock.Length > 0)
                    FlushBlock(currentBlock, objectIds, allObjects, colors, vertices, triangleIndices);

                currentBlock = line.Trim('[', ']');
                currentSection = string.Empty;
                colors.Clear();
                vertices.Clear();
                triangleIndices.Clear();
                objectIds.Clear();
                allObjects = false;
            }
            else if (line.StartsWith('<'))
            {
                currentSection = line.Trim('<', '>');
            }
            else
            {
                switch (currentSection)
                {
                    case "Objects":
                        if (line.Equals("All", StringComparison.OrdinalIgnoreCase))
                            allObjects = true;
                        else if (int.TryParse(line, out int id))
                            objectIds.Add(id);
                        break;

                    case "Colors":
                        ParseColor(line, colors);
                        break;

                    case "Vertices":
                        ParseVertex(line, colors, vertices);
                        break;

                    case "Triangles":
                        ParseTriangle(line, triangleIndices);
                        break;
                }
            }
        }

        // Flush last block
        if (currentBlock.Length > 0)
            FlushBlock(currentBlock, objectIds, allObjects, colors, vertices, triangleIndices);
    }

    public static MapObjectReader LoadFromFile(string path)
    {
        var reader = new MapObjectReader();
        reader.Load(File.ReadAllLines(path));
        return reader;
    }

    private void FlushBlock(
        string blockName,
        List<int> objectIds,
        bool allObjects,
        List<(byte R, byte G, byte B)> colors,
        List<MapObjVertex> vertices,
        List<(int V0, int V1, int V2)> triangleIndices)
    {
        var obj = new MapObject { Name = blockName };
        foreach (var (v0, v1, v2) in triangleIndices)
        {
            if (v0 < vertices.Count && v1 < vertices.Count && v2 < vertices.Count)
                obj.Triangles.Add(new MapObjTriangle { V0 = vertices[v0], V1 = vertices[v1], V2 = vertices[v2] });
        }

        int idx = Objects.Count;
        Objects.Add(obj);

        if (allObjects)
        {
            for (int i = -256; i <= 4095; i++)
                ObjectWrap.TryAdd(i, idx);
        }
        else
        {
            foreach (int id in objectIds)
                ObjectWrap[id] = idx;
        }
    }

    private static void ParseColor(string line, List<(byte R, byte G, byte B)> colors)
    {
        var parts = line.Split(',');
        if (parts.Length >= 3)
        {
            float r = ParseFloat(parts[0]);
            float g = ParseFloat(parts[1]);
            float b = ParseFloat(parts[2]);
            colors.Add(((byte)(r * 255), (byte)(g * 255), (byte)(b * 255)));
        }
    }

    private static void ParseVertex(string line, List<(byte R, byte G, byte B)> colors, List<MapObjVertex> vertices)
    {
        var parts = line.Split(',');
        if (parts.Length >= 4)
        {
            float x = ParseFloat(parts[0]);
            float y = ParseFloat(parts[1]);
            float z = ParseFloat(parts[2]);
            int ci = int.Parse(parts[3].Trim());
            var c = ci >= 0 && ci < colors.Count ? colors[ci] : ((byte)255, (byte)255, (byte)255);
            vertices.Add(new MapObjVertex { X = x, Y = y, Z = z, R = c.Item1, G = c.Item2, B = c.Item3 });
        }
    }

    private static void ParseTriangle(string line, List<(int V0, int V1, int V2)> triangles)
    {
        var parts = line.Split(',');
        if (parts.Length >= 3)
        {
            int v0 = int.Parse(parts[0].Trim());
            int v1 = int.Parse(parts[1].Trim());
            int v2 = int.Parse(parts[2].Trim());
            triangles.Add((v0, v1, v2));
        }
    }

    private static float ParseFloat(string s)
    {
        return float.Parse(s.Trim(), CultureInfo.InvariantCulture);
    }
}
