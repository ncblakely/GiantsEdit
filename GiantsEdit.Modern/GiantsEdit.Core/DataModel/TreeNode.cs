namespace GiantsEdit.Core.DataModel;

/// <summary>
/// A node in the tree hierarchy. Contains named child nodes and leaves,
/// organized by DTD rule category.
/// Ported from Delphi's tNode class.
/// </summary>
/// <remarks>
/// The original Delphi tNode stores children as array-of-array:
///   node: array of array of tNode
///   leaf: array of array of tLeaf
/// The outer index maps to a DTD rule slot; the inner is instances of that slot.
/// When no rule is present, a single slot (index 0) is used.
/// </remarks>
public class TreeNode
{
    public string Name { get; set; }
    public string Help { get; set; } = string.Empty;
    public int State { get; set; }
    public TreeNode? Parent { get; internal set; }
    public DtdNode? Rule { get; }

    // Children organized by DTD rule slot.
    // Outer list index = rule slot, inner list = instances in that slot.
    private readonly List<List<TreeNode>> _childNodes;
    private readonly List<List<TreeLeaf>> _childLeaves;

    public TreeNode(string name, DtdNode? rule = null)
    {
        Name = name;
        Rule = rule;

        int nodeSlots = rule?.SubNodes.Count ?? 1;
        int leafSlots = rule?.SubLeaves.Count ?? 1;

        _childNodes = new List<List<TreeNode>>(nodeSlots);
        for (int i = 0; i < nodeSlots; i++)
            _childNodes.Add([]);

        _childLeaves = new List<List<TreeLeaf>>(leafSlots);
        for (int i = 0; i < leafSlots; i++)
            _childLeaves.Add([]);
    }

    /// <summary>
    /// Total number of child nodes across all slots.
    /// </summary>
    public int NodeCount
    {
        get
        {
            int count = 0;
            foreach (var slot in _childNodes)
                count += slot.Count;
            return count;
        }
    }

    /// <summary>
    /// Total number of child leaves across all slots.
    /// </summary>
    public int LeafCount
    {
        get
        {
            int count = 0;
            foreach (var slot in _childLeaves)
                count += slot.Count;
            return count;
        }
    }

    /// <summary>
    /// Count of child nodes in the named DTD slot.
    /// </summary>
    public int NodeCount_ByName(string name)
    {
        if (Rule == null)
            throw new InvalidOperationException("Cannot query by name without a DTD rule.");
        int slot = FindNodeSlot(name);
        return slot >= 0 ? _childNodes[slot].Count : -1;
    }

    /// <summary>
    /// Count of child leaves in the named DTD slot.
    /// </summary>
    public int LeafCount_ByName(string name)
    {
        if (Rule == null)
            throw new InvalidOperationException("Cannot query by name without a DTD rule.");
        int slot = FindLeafSlot(name);
        return slot >= 0 ? _childLeaves[slot].Count : -1;
    }

    /// <summary>
    /// Adds a child node. If a DTD rule is present, validates the name against it.
    /// </summary>
    public TreeNode AddNode(string name)
    {
        if (Rule != null)
        {
            int slot = FindNodeSlot(name);
            if (slot < 0)
                throw new InvalidOperationException(
                    $"DTD error: cannot add child node '{name}' to '{Name}' — not defined in schema.");

            var ruleEntry = Rule.SubNodes[slot];
            if (_childNodes[slot].Count > 0 &&
                ruleEntry.Count is DtdCount.Once or DtdCount.Optional)
                throw new InvalidOperationException(
                    $"DTD error: child node '{name}' in '{Name}' already exists and is limited to one instance.");

            var child = new TreeNode(name, ruleEntry.Node)
            {
                State = TreeState.Visible,
                Parent = this
            };
            _childNodes[slot].Add(child);
            return child;
        }
        else
        {
            var child = new TreeNode(name)
            {
                State = TreeState.Visible,
                Parent = this
            };
            _childNodes[0].Add(child);
            return child;
        }
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
        var leaf = AddLeafInternal(name);
        leaf.PropertyType = type;
        leaf.State = TreeState.Visible;
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
    /// Enumerates all child nodes across all slots, in slot order.
    /// </summary>
    public IEnumerable<TreeNode> EnumerateNodes()
    {
        foreach (var slot in _childNodes)
            foreach (var node in slot)
                yield return node;
    }

    /// <summary>
    /// Enumerates all child leaves across all slots, in slot order.
    /// </summary>
    public IEnumerable<TreeLeaf> EnumerateLeaves()
    {
        foreach (var slot in _childLeaves)
            foreach (var leaf in slot)
                yield return leaf;
    }

    /// <summary>
    /// Finds the first child node with the given name, or null.
    /// </summary>
    public TreeNode? FindChildNode(string name)
    {
        foreach (var slot in _childNodes)
            foreach (var node in slot)
                if (node.Name == name)
                    return node;
        return null;
    }

    /// <summary>
    /// Finds the first child leaf with the given name, or null.
    /// </summary>
    public TreeLeaf? FindChildLeaf(string name)
    {
        foreach (var slot in _childLeaves)
            foreach (var leaf in slot)
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
        foreach (var slot in _childNodes)
        {
            if (slot.Remove(child))
            {
                child.Parent = null;
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Removes a child leaf.
    /// </summary>
    public bool RemoveLeaf(TreeLeaf child)
    {
        foreach (var slot in _childLeaves)
        {
            if (slot.Remove(child))
            {
                child.Parent = null;
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Provides direct access to child node slots for advanced iteration (e.g., ScanNode).
    /// </summary>
    public IReadOnlyList<List<TreeNode>> NodeSlots => _childNodes;

    /// <summary>
    /// Provides direct access to child leaf slots for advanced iteration (e.g., ScanLeaf).
    /// </summary>
    public IReadOnlyList<List<TreeLeaf>> LeafSlots => _childLeaves;

    /// <summary>
    /// Recursively visits all nodes and leaves in the tree.
    /// </summary>
    public void Walk(Action<TreeNode>? onNode = null, Action<TreeLeaf>? onLeaf = null)
    {
        onNode?.Invoke(this);

        foreach (var leaf in EnumerateLeaves())
            onLeaf?.Invoke(leaf);

        foreach (var child in EnumerateNodes())
            child.Walk(onNode, onLeaf);
    }

    private TreeLeaf AddLeafInternal(string name)
    {
        var leaf = new TreeLeaf { Name = name, Parent = this };

        if (Rule != null)
        {
            int slot = FindLeafSlot(name);
            if (slot < 0)
                throw new InvalidOperationException(
                    $"DTD error: cannot add leaf '{name}' to '{Name}' — not defined in schema.");

            var ruleEntry = Rule.SubLeaves[slot];
            if (_childLeaves[slot].Count >= 1 &&
                ruleEntry.Count is DtdCount.Once or DtdCount.Optional)
                throw new InvalidOperationException(
                    $"DTD error: leaf '{name}' in '{Name}' already exists and is limited to one instance.");

            _childLeaves[slot].Add(leaf);
        }
        else
        {
            _childLeaves[0].Add(leaf);
        }

        return leaf;
    }

    private int FindNodeSlot(string name)
    {
        if (Rule == null) return -1;
        for (int i = 0; i < Rule.SubNodes.Count; i++)
            if (Rule.SubNodes[i].Name == name)
                return i;
        return -1;
    }

    private int FindLeafSlot(string name)
    {
        if (Rule == null) return -1;
        for (int i = 0; i < Rule.SubLeaves.Count; i++)
            if (Rule.SubLeaves[i].Name == name)
                return i;
        return -1;
    }
}
