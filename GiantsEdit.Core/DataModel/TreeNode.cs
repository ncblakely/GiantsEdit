namespace GiantsEdit.Core.DataModel;

/// <summary>
/// A node in the tree hierarchy. Contains named child nodes and leaves.
/// </summary>
public class TreeNode
{
    public string Name { get; set; }
    public TreeNode? Parent { get; internal set; }

    private readonly List<TreeNode> _childNodes = [];
    private readonly List<TreeLeaf> _childLeaves = [];

    public TreeNode(string name)
    {
        Name = name;
    }

    /// <summary>
    /// Total number of child nodes.
    /// </summary>
    public int NodeCount => _childNodes.Count;

    /// <summary>
    /// Total number of child leaves.
    /// </summary>
    public int LeafCount => _childLeaves.Count;

    /// <summary>
    /// Adds a child node.
    /// </summary>
    public TreeNode AddNode(string name)
    {
        var child = new TreeNode(name) { Parent = this };
        _childNodes.Add(child);
        return child;
    }

    /// <summary>
    /// Gets existing child node by name, or adds it if it doesn't exist.
    /// </summary>
    public TreeNode GetOrAddNode(string name)
    {
        var existing = FindChildNode(name);
        return existing ?? AddNode(name);
    }

    /// <summary>
    /// Adds a typed leaf to this node.
    /// </summary>
    public TreeLeaf AddLeaf(string name, PropertyType type)
    {
        var leaf = new TreeLeaf { Name = name, Parent = this, PropertyType = type };
        _childLeaves.Add(leaf);
        return leaf;
    }

    public TreeLeaf AddByte(string name, byte value)
    {
        var leaf = AddLeaf(name, PropertyType.Byte);
        leaf.ByteValue = value;
        return leaf;
    }

    public TreeLeaf AddInt32(string name, int value)
    {
        var leaf = AddLeaf(name, PropertyType.Int32);
        leaf.Int32Value = value;
        return leaf;
    }

    public TreeLeaf AddSingle(string name, float value)
    {
        var leaf = AddLeaf(name, PropertyType.Single);
        leaf.SingleValue = value;
        return leaf;
    }

    public TreeLeaf AddString(string name, string value)
    {
        var leaf = AddLeaf(name, PropertyType.String);
        leaf.MaxLength = -1;
        leaf.StringValue = value;
        return leaf;
    }

    public TreeLeaf AddStringL(string name, string value, int maxLength)
    {
        var leaf = AddLeaf(name, PropertyType.String);
        leaf.MaxLength = maxLength;
        leaf.StringValue = value;
        return leaf;
    }

    public TreeLeaf AddVoid(string name)
    {
        return AddLeaf(name, PropertyType.Void);
    }

    /// <summary>
    /// Enumerates all child nodes.
    /// </summary>
    public IEnumerable<TreeNode> EnumerateNodes() => _childNodes;

    /// <summary>
    /// Enumerates all child leaves.
    /// </summary>
    public IEnumerable<TreeLeaf> EnumerateLeaves() => _childLeaves;

    /// <summary>
    /// Finds the first child node with the given name, or null.
    /// </summary>
    public TreeNode? FindChildNode(string name)
    {
        foreach (var node in _childNodes)
            if (node.Name == name)
                return node;
        return null;
    }

    /// <summary>
    /// Finds the first child leaf with the given name, or null.
    /// </summary>
    public TreeLeaf? FindChildLeaf(string name)
    {
        foreach (var leaf in _childLeaves)
            if (leaf.Name == name)
                return leaf;
        return null;
    }

    /// <summary>
    /// Gets the first child node with the given name, throwing if not found.
    /// </summary>
    public TreeNode GetChildNode(string name)
    {
        return FindChildNode(name)
            ?? throw new KeyNotFoundException($"Child node '{name}' does not exist in '{Name}'.");
    }

    /// <summary>
    /// Gets the first child leaf with the given name, throwing if not found.
    /// </summary>
    public TreeLeaf GetChildLeaf(string name)
    {
        return FindChildLeaf(name)
            ?? throw new KeyNotFoundException($"Child leaf '{name}' does not exist in '{Name}'.");
    }

    public bool HasChildNode(string name) => FindChildNode(name) != null;
    public bool HasChildLeaf(string name) => FindChildLeaf(name) != null;

    /// <summary>
    /// Removes a child node (and all its descendants).
    /// </summary>
    public bool RemoveNode(TreeNode child)
    {
        if (_childNodes.Remove(child))
        {
            child.Parent = null;
            return true;
        }
        return false;
    }

    /// <summary>
    /// Removes a child leaf.
    /// </summary>
    public bool RemoveLeaf(TreeLeaf child)
    {
        if (_childLeaves.Remove(child))
        {
            child.Parent = null;
            return true;
        }
        return false;
    }

    /// <summary>
    /// Removes all child nodes matching the given name.
    /// </summary>
    public void RemoveNodesByName(string name)
    {
        _childNodes.RemoveAll(n => n.Name == name);
    }

    /// <summary>
    /// Recursively visits all nodes and leaves in the tree.
    /// </summary>
    public void Walk(Action<TreeNode>? onNode = null, Action<TreeLeaf>? onLeaf = null)
    {
        onNode?.Invoke(this);

        foreach (var leaf in _childLeaves)
            onLeaf?.Invoke(leaf);

        foreach (var child in _childNodes)
            child.Walk(onNode, onLeaf);
    }
}
