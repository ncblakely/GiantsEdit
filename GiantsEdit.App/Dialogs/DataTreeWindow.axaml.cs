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
    private LeafPropertyVm? _browsingLeaf;
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
            if (_browsingLeaf != null && LstObjTypes.SelectedItem is string selected)
            {
                _browsingLeaf.Value = selected;
                ObjTypePopup.IsOpen = false;
                _browsingLeaf = null;
            }
        };
    }

    private void OpenObjTypeBrowser(LeafPropertyVm leaf)
    {
        _browsingLeaf = leaf;
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
    }

    private void ShowProperties(TreeNode node)
    {
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
