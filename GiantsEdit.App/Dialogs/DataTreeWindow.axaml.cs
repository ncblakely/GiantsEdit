using System.Collections.ObjectModel;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GiantsEdit.Core.DataModel;

namespace GiantsEdit.App.Dialogs;

public partial class DataTreeWindow : Window
{
    private TreeNode? _rootNode;
    private TreeNode? _selectedNode;

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

    public DataTreeWindow()
    {
        InitializeComponent();

        BtnAddEntry.Click += (_, _) => AddIncludeFile();
        Closing += (_, _) => ApplyCurrentProperties();
    }

    public void LoadTree(TreeNode root, string title)
    {
        _rootNode = root;
        Title = title;
        var rootVm = new DataTreeNodeVm(root);
        var items = new ObservableCollection<DataTreeNodeVm> { rootVm };
        DataTree.ItemsSource = items;

        // Expand root node once the tree has rendered
        DataTree.Loaded += (_, _) =>
        {
            if (DataTree.ContainerFromItem(rootVm) is TreeViewItem rootItem)
                rootItem.IsExpanded = true;
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
            var vm = new LeafPropertyVm(leaf, editable);
            if (editable)
            {
                vm.DeleteRequested += () =>
                {
                    node.RemoveLeaf(leaf);
                    ShowProperties(node);
                    StatusText.Text = $"Removed: {leaf.StringValue}";
                };
            }
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
    private static readonly HashSet<string> HiddenSections = ["[sfx]", "[objdefs]", "ObjEditStart", "ObjEditEnd"];

    public DataTreeNodeVm(TreeNode node)
    {
        Model = node;

        // Show object name instead of generic "Object" for entries in the objects array
        if (node.Name == "Object")
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

    public LeafPropertyVm(TreeLeaf leaf, bool canDelete = false)
    {
        _leaf = leaf;
        Name = leaf.Name;
        _isObjectType = leaf.Name == "Type" && leaf.PropertyType == PropertyType.Int32;
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
        CanDelete = canDelete;
        DeleteCommand = new RelayCommand(() => DeleteRequested?.Invoke());
    }

    public string Name { get; }

    [ObservableProperty]
    private string _value = "";

    public bool IsReadOnly { get; }
    public bool CanDelete { get; }
    public IRelayCommand DeleteCommand { get; }

    public event Action? DeleteRequested;

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
