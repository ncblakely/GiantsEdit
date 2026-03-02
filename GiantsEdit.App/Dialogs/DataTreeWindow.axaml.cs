using System.Collections.ObjectModel;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GiantsEdit.Core.DataModel;
using GiantsEdit.Core.Formats;

namespace GiantsEdit.App.Dialogs;

public partial class DataTreeWindow : Window
{
    private TreeNode? _rootNode;
    private TreeNode? _selectedNode;
    private Action<string>? _browseCallback;
    private ObservableCollection<DataTreeNodeVm>? _items;
    private readonly IReadOnlyList<string> _allObjNames = ObjectNames.GetAllDisplayNames();

    /// <summary>
    /// Fired when a node is selected in the tree view.
    /// </summary>
    public event Action<TreeNode>? NodeSelected;

    /// <summary>
    /// Max include file entries — each is a 32-byte fixed string in the binary format.
    /// </summary>
    private const int MaxIncludeFiles = 32;

    /// <summary>
    /// Nodes whose leaves can be added/removed via the list edit panel.
    /// </summary>
    private static readonly HashSet<string> EditableListNodes = ["[includefiles]"];

    /// <summary>
    /// Leaf names that represent an object type ID and should show a browse dropdown.
    /// </summary>
    private static readonly HashSet<string> ObjectTypeLeafNames = ["Type", "Character"];

    public DataTreeWindow()
    {
        InitializeComponent();

        BtnAddEntry.Click += (_, _) => AddIncludeFile();
        Closing += (_, _) => ApplyCurrentProperties();

        // Object type search popup
        LstObjTypes.ItemsSource = _allObjNames;
        TxtObjTypeSearch.TextChanged += (_, _) =>
        {
            var filter = TxtObjTypeSearch.Text;
            LstObjTypes.ItemsSource = string.IsNullOrEmpty(filter)
                ? _allObjNames
                : ObjectNames.Search(filter);
        };
        LstObjTypes.SelectionChanged += (_, _) =>
        {
            if (_browseCallback != null && LstObjTypes.SelectedItem is string selected)
            {
                _browseCallback(selected);
                ObjTypePopup.IsOpen = false;
                _browseCallback = null;
            }
        };
    }

    private void OpenObjTypeBrowser(LeafPropertyVm leaf)
    {
        OpenObjTypeBrowserCore(v => leaf.Value = v);
    }

    private void OpenObjTypeBrowser(OptionalLeafVm leaf)
    {
        OpenObjTypeBrowserCore(v => leaf.Value = v);
    }

    private void OpenObjTypeBrowserCore(Action<string> setValue)
    {
        _browseCallback = setValue;
        TxtObjTypeSearch.Text = "";
        LstObjTypes.ItemsSource = _allObjNames;
        LstObjTypes.SelectedItem = null;
        ObjTypePopup.PlacementTarget = this;
        ObjTypePopup.IsOpen = true;
        TxtObjTypeSearch.Focus();
    }

    public void LoadTree(TreeNode root, string title)
    {
        Title = title;
        var items = new ObservableCollection<DataTreeNodeVm> { new(root) };
        InitTree(items);
    }

    public void LoadForest(IEnumerable<TreeNode> roots, string title)
    {
        Title = title;
        var items = new ObservableCollection<DataTreeNodeVm>();
        foreach (var root in roots)
            items.Add(new DataTreeNodeVm(root));
        InitTree(items);
    }

    private void InitTree(ObservableCollection<DataTreeNodeVm> items)
    {
        _items = items;
        _rootNode = items.Count == 1 ? items[0].Model : null;
        DataTree.ItemsSource = items;

        DataTree.Loaded += (_, _) =>
        {
            foreach (var vm in items)
                if (DataTree.ContainerFromItem(vm) is TreeViewItem item)
                    item.IsExpanded = true;
        };

        DataTree.SelectionChanged += (_, _) =>
        {
            ApplyCurrentProperties();
            if (DataTree.SelectedItem is DataTreeNodeVm vm)
            {
                _selectedNode = vm.Model;
                ShowProperties(vm.Model);
                StatusText.Text = vm.Name;
                ListEditPanel.IsVisible = EditableListNodes.Contains(vm.Model.Name);
                NodeSelected?.Invoke(vm.Model);
            }
        };
    }

    /// <summary>
    /// Expands the tree path to the given node and selects it.
    /// </summary>
    public void SelectNode(TreeNode target)
    {
        if (_items == null) return;
        var path = FindPath(_items, target);
        if (path == null) return;

        // Expand each ancestor, then select the final node
        DataTree.Loaded += (_, _) => ExpandAndSelect(path, 0);
        // If already loaded, try immediately
        ExpandAndSelect(path, 0);
    }

    private void ExpandAndSelect(List<DataTreeNodeVm> path, int depth)
    {
        if (depth >= path.Count) return;

        var vm = path[depth];
        if (depth == path.Count - 1)
        {
            DataTree.SelectedItem = vm;
            return;
        }

        if (DataTree.ContainerFromItem(vm) is TreeViewItem tvi)
        {
            tvi.IsExpanded = true;
            // After expanding, continue down the path
            tvi.ContainerPrepared += (_, _) => ExpandAndSelect(path, depth + 1);
            ExpandAndSelect(path, depth + 1);
        }
    }

    private static List<DataTreeNodeVm>? FindPath(
        IEnumerable<DataTreeNodeVm> roots, TreeNode target)
    {
        foreach (var root in roots)
        {
            var path = new List<DataTreeNodeVm>();
            if (FindPathRecursive(root, target, path))
                return path;
        }
        return null;
    }

    private static bool FindPathRecursive(
        DataTreeNodeVm current, TreeNode target, List<DataTreeNodeVm> path)
    {
        path.Add(current);
        if (current.Model == target) return true;

        foreach (var child in current.Children)
            if (FindPathRecursive(child, target, path))
                return true;

        path.RemoveAt(path.Count - 1);
        return false;
    }

    private void ApplyCurrentProperties()
    {
        if (PropertiesList.ItemsSource is ObservableCollection<LeafPropertyVm> props)
        {
            foreach (var p in props)
                p.Apply();
        }
        if (OptionalPropsList.ItemsSource is ObservableCollection<OptionalLeafVm> optProps)
        {
            foreach (var p in optProps)
                p.Apply();
        }
    }

    private void ShowProperties(TreeNode node)
    {
        if (node.Name == "<Options>")
        {
            ShowOptionsProperties(node);
            return;
        }

        PropertiesList.IsVisible = true;
        OptionalPropsList.IsVisible = false;

        bool editable = EditableListNodes.Contains(node.Name);
        var props = new ObservableCollection<LeafPropertyVm>();
        foreach (var leaf in node.EnumerateLeaves())
        {
            bool isObjType = ObjectTypeLeafNames.Contains(leaf.Name) && leaf.PropertyType == PropertyType.Int32;
            var vm = new LeafPropertyVm(leaf, canDelete: editable, isObjectType: isObjType);
            if (editable)
            {
                vm.DeleteRequested += () =>
                {
                    node.RemoveLeaf(leaf);
                    ShowProperties(node);
                    StatusText.Text = $"Removed: {leaf.StringValue}";
                };
            }
            if (isObjType)
                vm.BrowseRequested += () => OpenObjTypeBrowser(vm);
            props.Add(vm);
        }
        PropertiesList.ItemsSource = props;
    }

    private void ShowOptionsProperties(TreeNode node)
    {
        PropertiesList.IsVisible = false;
        OptionalPropsList.IsVisible = true;

        var vms = new ObservableCollection<OptionalLeafVm>();
        foreach (var def in OptionalLeafVm.AllOptionDefs)
        {
            var vm = new OptionalLeafVm(node, def);
            if (def.IsObjectType)
                vm.BrowseRequested += () => OpenObjTypeBrowser(vm);
            vms.Add(vm);
        }
        OptionalPropsList.ItemsSource = vms;
    }

    private async void AddIncludeFile()
    {
        if (_selectedNode == null || _selectedNode.Name != "[includefiles]") return;

        int currentCount = _selectedNode.LeafCount;
        if (currentCount >= MaxIncludeFiles)
        {
            StatusText.Text = $"Cannot add more than {MaxIncludeFiles} include files";
            return;
        }

        var dlg = new InputDialog("Add Include File", "Include file name (without .bin extension):");
        var result = await dlg.ShowDialog<string?>(this);

        if (!string.IsNullOrWhiteSpace(result))
        {
            if (result.Length > 31)
            {
                StatusText.Text = "Name too long (max 31 characters)";
                return;
            }
            _selectedNode.AddString("Name", result);
            ShowProperties(_selectedNode);
            StatusText.Text = $"Added include file: {result}";
        }
    }

}

/// <summary>
/// View model for a tree node displayed in the DataTreeWindow.
/// </summary>
public class DataTreeNodeVm
{
    private static readonly HashSet<string> HiddenSections =
        [BinFormatConstants.SectionSfx, BinFormatConstants.SectionObjDefs, BinFormatConstants.NodeObjEditStart, BinFormatConstants.NodeObjEditEnd];

    public DataTreeNodeVm(TreeNode node)
    {
        Model = node;

        // Show object name instead of generic "Object" for entries in the objects array
        if (node.Name == BinFormatConstants.NodeObject)
        {
            var typeLeaf = node.FindChildLeaf("Type");
            Name = typeLeaf != null
                ? ObjectNames.GetDisplayName(typeLeaf.Int32Value)
                : node.Name;
        }
        else
        {
            Name = node.Name;
        }

        foreach (var child in node.EnumerateNodes())
            if (!HiddenSections.Contains(child.Name))
                Children.Add(new DataTreeNodeVm(child));
    }

    public string Name { get; }
    public TreeNode Model { get; }
    public ObservableCollection<DataTreeNodeVm> Children { get; } = [];

    public override string ToString() => Name;
}

/// <summary>
/// View model for a leaf property displayed in the properties panel.
/// </summary>
public partial class LeafPropertyVm : ObservableObject
{
    private readonly TreeLeaf _leaf;
    private readonly bool _isObjectType;

    public LeafPropertyVm(TreeLeaf leaf, bool canDelete = false, bool isObjectType = false)
    {
        _leaf = leaf;
        Name = leaf.Name;
        _isObjectType = isObjectType || (leaf.Name == "Type" && leaf.PropertyType == PropertyType.Int32);
        Value = _isObjectType
            ? ObjectNames.GetDisplayName(leaf.Int32Value)
            : leaf.PropertyType switch
            {
                PropertyType.Byte => leaf.ByteValue.ToString(),
                PropertyType.Int32 => leaf.Int32Value.ToString(),
                PropertyType.Single => leaf.SingleValue.ToString("F4"),
                PropertyType.String => leaf.StringValue,
                PropertyType.Void => "(void)",
                _ => "?"
            };
        IsReadOnly = leaf.PropertyType == PropertyType.Void;
        IsObjectType = _isObjectType;
        CanDelete = canDelete;
        DeleteCommand = new RelayCommand(() => DeleteRequested?.Invoke());
        BrowseCommand = new RelayCommand(() => BrowseRequested?.Invoke());
    }

    public string Name { get; }

    [ObservableProperty]
    private string _value = "";

    public bool IsReadOnly { get; }
    public bool IsObjectType { get; }
    public bool CanDelete { get; }
    public IRelayCommand DeleteCommand { get; }
    public IRelayCommand BrowseCommand { get; }

    public event Action? DeleteRequested;
    public event Action? BrowseRequested;

    /// <summary>
    /// Applies the edited value back to the underlying leaf.
    /// </summary>
    public void Apply()
    {
        if (_isObjectType)
        {
            var parsed = ObjectNames.ParseInput(Value);
            if (parsed.HasValue)
            {
                _leaf.Int32Value = parsed.Value;
                Value = ObjectNames.GetDisplayName(parsed.Value);
            }
            return;
        }

        switch (_leaf.PropertyType)
        {
            case PropertyType.Byte when byte.TryParse(Value, out byte b):
                _leaf.ByteValue = b; break;
            case PropertyType.Int32 when int.TryParse(Value, out int i):
                _leaf.Int32Value = i; break;
            case PropertyType.Single when float.TryParse(Value, out float f):
                _leaf.SingleValue = f; break;
            case PropertyType.String:
                _leaf.StringValue = Value; break;
        }
    }
}

/// <summary>
/// Defines a known mission option that can be toggled on/off.
/// </summary>
public record OptionDef(string LeafName, PropertyType Type, string DefaultValue, bool IsObjectType = false)
{
    /// <summary>
    /// Grouped leaves that are added/removed together with the primary leaf.
    /// </summary>
    public string[]? GroupedLeaves { get; init; }
    public string[]? GroupedDefaults { get; init; }
}

/// <summary>
/// View model for an optional mission option displayed with a checkbox.
/// </summary>
public partial class OptionalLeafVm : ObservableObject
{
    private readonly TreeNode _node;
    private readonly OptionDef _def;

    public static readonly OptionDef[] AllOptionDefs =
    [
        new("Character", PropertyType.Int32, "0", IsObjectType: true)
        {
            GroupedLeaves = ["Teammembers", "MarkerID", "KabutoSize"],
            GroupedDefaults = ["0", "0", "0"]
        },
        new("Teammembers", PropertyType.Int32, "0"),
        new("MarkerID", PropertyType.Int32, "0"),
        new("KabutoSize", PropertyType.Int32, "0"),
        new("SmartieType", PropertyType.Int32, "0"),
        new("SpecialText", PropertyType.Int32, "0"),
        new("VimpMeat", PropertyType.Int32, "0"),
        new("NoNitro", PropertyType.Int32, "0"),
        new("NoJetpack", PropertyType.Int32, "0"),
        new("FailTime", PropertyType.Int32, "0"),
        new("FailFlick", PropertyType.String, ""),
        new("StartFlick", PropertyType.String, ""),
        new("EndFlick", PropertyType.String, ""),
        new("SuccessDelay 0", PropertyType.Single, "0.0000")
        {
            GroupedLeaves = ["SuccessDelay 1"],
            GroupedDefaults = ["0.0000"]
        },
        new("SuccessDelay 1", PropertyType.Single, "0.0000"),
        new("BumpClampValue", PropertyType.Single, "0.0000"),
        new("JetskiRace", PropertyType.Void, ""),
        new("ZeroVimpMeat", PropertyType.Void, ""),
        new("GrabSmartie", PropertyType.Void, ""),
    ];

    public OptionalLeafVm(TreeNode node, OptionDef def)
    {
        _node = node;
        _def = def;
        Name = def.LeafName;
        HasValue = def.Type != PropertyType.Void;
        ShowBrowse = def.IsObjectType;
        BrowseCommand = new RelayCommand(() => BrowseRequested?.Invoke());

        var leaf = node.FindChildLeaf(def.LeafName);
        _isEnabled = leaf != null;
        _value = leaf != null ? FormatLeafValue(leaf) : def.DefaultValue;
    }

    public string Name { get; }
    public bool HasValue { get; }
    public bool ShowBrowse { get; }
    public IRelayCommand BrowseCommand { get; }
    public event Action? BrowseRequested;

    [ObservableProperty]
    private bool _isEnabled;

    [ObservableProperty]
    private string _value = "";

    partial void OnIsEnabledChanged(bool value)
    {
        if (value)
        {
            // Add leaf with default or current value
            AddLeaf(_def.LeafName, _def.Type, Value ?? _def.DefaultValue);
            if (_def.GroupedLeaves != null)
            {
                for (int i = 0; i < _def.GroupedLeaves.Length; i++)
                {
                    string gName = _def.GroupedLeaves[i];
                    if (_node.FindChildLeaf(gName) == null)
                    {
                        string gDefault = _def.GroupedDefaults?[i] ?? "0";
                        // Find the matching def for this grouped leaf
                        var gDef = Array.Find(AllOptionDefs, d => d.LeafName == gName);
                        AddLeaf(gName, gDef?.Type ?? PropertyType.Int32, gDefault);
                    }
                }
            }
        }
        else
        {
            RemoveLeaf(_def.LeafName);
            if (_def.GroupedLeaves != null)
            {
                foreach (string gName in _def.GroupedLeaves)
                    RemoveLeaf(gName);
            }
        }
    }

    private void AddLeaf(string name, PropertyType type, string val)
    {
        if (_node.FindChildLeaf(name) != null) return;
        switch (type)
        {
            case PropertyType.Int32:
                int iv = int.TryParse(val, out int pi) ? pi : 0;
                _node.AddInt32(name, iv);
                break;
            case PropertyType.Single:
                float fv = float.TryParse(val, out float pf) ? pf : 0f;
                _node.AddSingle(name, fv);
                break;
            case PropertyType.String:
                _node.AddString(name, val);
                break;
            case PropertyType.Void:
                _node.AddVoid(name);
                break;
        }
    }

    private void RemoveLeaf(string name)
    {
        var leaf = _node.FindChildLeaf(name);
        if (leaf != null) _node.RemoveLeaf(leaf);
    }

    private string FormatLeafValue(TreeLeaf leaf)
    {
        if (_def.IsObjectType)
            return ObjectNames.GetDisplayName(leaf.Int32Value);
        return leaf.PropertyType switch
        {
            PropertyType.Int32 => leaf.Int32Value.ToString(),
            PropertyType.Single => leaf.SingleValue.ToString("F4"),
            PropertyType.String => leaf.StringValue,
            PropertyType.Void => "",
            _ => ""
        };
    }

    public void Apply()
    {
        if (!IsEnabled) return;
        var leaf = _node.FindChildLeaf(_def.LeafName);
        if (leaf == null) return;

        if (_def.IsObjectType)
        {
            var parsed = ObjectNames.ParseInput(Value);
            if (parsed.HasValue)
            {
                leaf.Int32Value = parsed.Value;
                Value = ObjectNames.GetDisplayName(parsed.Value);
            }
            return;
        }

        switch (leaf.PropertyType)
        {
            case PropertyType.Int32 when int.TryParse(Value, out int i):
                leaf.Int32Value = i; break;
            case PropertyType.Single when float.TryParse(Value, out float f):
                leaf.SingleValue = f; break;
            case PropertyType.String:
                leaf.StringValue = Value; break;
        }
    }
}
