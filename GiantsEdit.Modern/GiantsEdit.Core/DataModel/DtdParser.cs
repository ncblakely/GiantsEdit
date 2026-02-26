namespace GiantsEdit.Core.DataModel;

/// <summary>
/// Parses DTD schema files that define the structure of tree data.
/// Ported from Delphi's dtd.pas LoadDTD function.
/// </summary>
/// <remarks>
/// DTD file format example:
/// <code>
/// Node MapData {
///   Node FileStart "[FileStart]" once
///   Leaf string "GtiName" once
/// }
/// </code>
/// </remarks>
public static class DtdParser
{
    /// <summary>
    /// Loads a DTD schema from a file path.
    /// </summary>
    public static List<DtdNode> LoadFromFile(string path)
    {
        var lines = File.ReadAllLines(path);
        return Parse(lines);
    }

    /// <summary>
    /// Parses DTD schema from lines of text.
    /// </summary>
    public static List<DtdNode> Parse(IEnumerable<string> lines)
    {
        var result = new List<DtdNode>();
        DtdNode? current = null;

        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();
            if (string.IsNullOrEmpty(line))
                continue;

            if (line.EndsWith('{'))
            {
                // Node header: "Node MapData {"
                if (!line.StartsWith("Node "))
                    continue;

                var name = line[5..^1].Trim();
                current = new DtdNode { Name = name };
                result.Add(current);
            }
            else if (line == "}")
            {
                current = null;
            }
            else if (current != null && line.StartsWith("Node "))
            {
                var subNode = ParseSubNode(line[5..].Trim());
                if (subNode != null)
                    current.SubNodes.Add(subNode);
            }
            else if (current != null && line.StartsWith("Leaf "))
            {
                var subLeaf = ParseSubLeaf(line[5..].Trim());
                if (subLeaf != null)
                    current.SubLeaves.Add(subLeaf);
            }
        }

        Link(result);
        return result;
    }

    private static DtdSubNode? ParseSubNode(string text)
    {
        // Format: NodeTypeName ["DisplayName"] count
        // Examples:
        //   "FileStart \"[FileStart]\" once"
        //   "Objects \"<Objects>\" once"
        //   "VoPath any"

        var (typeName, rest) = SplitFirstToken(text);
        if (typeName == null || rest == null) return null;

        var (displayName, countStr) = ParseNameAndCount(rest, typeName);

        return new DtdSubNode
        {
            NodeTypeName = typeName,
            Name = displayName,
            Count = ParseCount(countStr)
        };
    }

    private static DtdSubLeaf? ParseSubLeaf(string text)
    {
        // Format: TypeName "DisplayName" count
        // Examples:
        //   "string \"Box\" once"
        //   "single \"p0\" once"

        var (typeName, rest) = SplitFirstToken(text);
        if (typeName == null || rest == null) return null;

        var (displayName, countStr) = ParseNameAndCount(rest, typeName);

        return new DtdSubLeaf
        {
            LeafTypeName = typeName,
            Name = displayName,
            Count = ParseCount(countStr)
        };
    }

    private static (string displayName, string countStr) ParseNameAndCount(string text, string fallbackName)
    {
        text = text.Trim();

        if (text.StartsWith('"'))
        {
            int endQuote = text.IndexOf('"', 1);
            if (endQuote < 0)
                return (fallbackName, text);

            string displayName = text[1..endQuote];
            string countStr = text[(endQuote + 1)..].Trim();
            return (displayName, countStr);
        }
        else
        {
            return (fallbackName, text);
        }
    }

    private static (string? first, string? rest) SplitFirstToken(string text)
    {
        int i = 0;
        while (i < text.Length && text[i] > ' ')
            i++;

        if (i >= text.Length)
            return (text, null);

        return (text[..i].Trim(), text[i..].Trim());
    }

    private static DtdCount ParseCount(string s) => s.Trim() switch
    {
        "any" => DtdCount.Any,
        "once" => DtdCount.Once,
        "optional" => DtdCount.Optional,
        "multiple" => DtdCount.Multiple,
        _ => DtdCount.Any
    };

    /// <summary>
    /// Second pass: resolve node type references and leaf basic types.
    /// </summary>
    private static void Link(List<DtdNode> nodes)
    {
        var lookup = new Dictionary<string, DtdNode>();
        foreach (var node in nodes)
        {
            // First definition wins (matches Delphi behavior).
            lookup.TryAdd(node.Name, node);
        }

        foreach (var node in nodes)
        {
            foreach (var sub in node.SubNodes)
            {
                lookup.TryGetValue(sub.NodeTypeName, out var target);
                sub.Node = target;
            }

            foreach (var sub in node.SubLeaves)
            {
                sub.BasicType = sub.LeafTypeName.ToLowerInvariant() switch
                {
                    "byte" => DtdBasicType.Byte,
                    "int32" => DtdBasicType.Int32,
                    "single" => DtdBasicType.Single,
                    "string" => DtdBasicType.String,
                    "void" => DtdBasicType.Void,
                    _ => DtdBasicType.Invalid
                };
            }
        }
    }
}
