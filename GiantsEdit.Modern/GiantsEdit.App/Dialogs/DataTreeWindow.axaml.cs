using System.Collections.ObjectModel;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using GiantsEdit.Core.DataModel;

namespace GiantsEdit.App.Dialogs;

public partial class DataTreeWindow : Window
{
    private TreeNode? _rootNode;

    public DataTreeWindow()
    {
        InitializeComponent();
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
            if (DataTree.SelectedItem is DataTreeNodeVm vm)
            {
                ShowProperties(vm.Model);
                StatusText.Text = vm.Name;
            }
        };
    }

    private void ShowProperties(TreeNode node)
    {
        var props = new ObservableCollection<LeafPropertyVm>();
        foreach (var leaf in node.EnumerateLeaves())
            props.Add(new LeafPropertyVm(leaf));
        PropertiesList.ItemsSource = props;
    }
}

/// <summary>
/// View model for a tree node displayed in the DataTreeWindow.
/// </summary>
public class DataTreeNodeVm
{
    private static readonly HashSet<string> HiddenSections = ["[sfx]", "[objdefs]"];

    public DataTreeNodeVm(TreeNode node)
    {
        Model = node;
        Name = node.Name;

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

    public LeafPropertyVm(TreeLeaf leaf)
    {
        _leaf = leaf;
        Name = leaf.Name;
        Value = leaf.PropertyType switch
        {
            PropertyType.Byte => leaf.ByteValue.ToString(),
            PropertyType.Int32 => leaf.Int32Value.ToString(),
            PropertyType.Single => leaf.SingleValue.ToString("F4"),
            PropertyType.String => leaf.StringValue,
            PropertyType.Void => "(void)",
            _ => "?"
        };
        IsReadOnly = leaf.PropertyType == PropertyType.Void;
    }

    public string Name { get; }

    [ObservableProperty]
    private string _value = "";

    public bool IsReadOnly { get; }

    /// <summary>
    /// Applies the edited value back to the underlying leaf.
    /// </summary>
    public void Apply()
    {
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
