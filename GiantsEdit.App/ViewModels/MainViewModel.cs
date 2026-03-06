using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GiantsEdit.Core.DataModel;
using GiantsEdit.Core.Services;

namespace GiantsEdit.App.ViewModels;

/// <summary>
/// View model node for displaying TreeNode hierarchy in Avalonia TreeView.
/// </summary>
public partial class TreeNodeViewModel : ObservableObject
{
    private readonly TreeNode _node;

    public TreeNodeViewModel(TreeNode node)
    {
        _node = node;
        Name = node.Name;

        // Build children: sub-nodes first, then leaves
        foreach (var child in node.EnumerateNodes())
            Children.Add(new TreeNodeViewModel(child));

        foreach (var leaf in node.EnumerateLeaves())
            Children.Add(new TreeLeafViewModel(leaf));
    }

    public string Name { get; }
    public TreeNode Model => _node;
    public ObservableCollection<object> Children { get; } = [];

    public override string ToString() => Name;
}

/// <summary>
/// View model for displaying a TreeLeaf in the tree.
/// </summary>
public partial class TreeLeafViewModel : ObservableObject
{
    private readonly TreeLeaf _leaf;

    public TreeLeafViewModel(TreeLeaf leaf)
    {
        _leaf = leaf;
    }

    public string Name => _leaf.Name;
    public TreeLeaf Model => _leaf;

    public string DisplayValue => _leaf.PropertyType switch
    {
        PropertyType.Byte => _leaf.ByteValue.ToString(CultureInfo.InvariantCulture),
        PropertyType.Int32 => _leaf.Int32Value.ToString(CultureInfo.InvariantCulture),
        PropertyType.Single => _leaf.SingleValue.ToString("F4", CultureInfo.InvariantCulture),
        PropertyType.String => _leaf.StringValue,
        PropertyType.Void => "(void)",
        _ => "?"
    };

    public override string ToString() => $"{Name} = {DisplayValue}";
}

/// <summary>
/// Main window view model managing world document, tree view, and editing state.
/// </summary>
public partial class MainViewModel : ObservableObject
{
    private readonly WorldDocument _doc = new();

    [ObservableProperty]
    private ObservableCollection<TreeNodeViewModel> _treeRoots = [];

    [ObservableProperty]
    private object? _selectedTreeItem;

    [ObservableProperty]
    private string _statusText = "Ready";

    [ObservableProperty]
    private EditMode _currentMode = EditMode.Camera;

    [ObservableProperty]
    private float _brushRadius = 50f;

    [ObservableProperty]
    private float _brushStrength = 0.5f;

    // Selected object properties (for property panel binding)
    [ObservableProperty]
    private float _objX;
    [ObservableProperty]
    private float _objY;
    [ObservableProperty]
    private float _objZ;
    [ObservableProperty]
    private float _objAngle;
    [ObservableProperty]
    private float _objScale = 1f;
    [ObservableProperty]
    private int _objType;
    [ObservableProperty]
    private bool _hasSelectedObject;

    public WorldDocument Document => _doc;

    public MainViewModel()
    {
        _doc.WorldChanged += OnWorldChanged;
        _doc.SelectionChanged += OnSelectionChanged;
        _doc.TerrainChanged += () => StatusText = "Terrain modified";
    }

    private void OnWorldChanged()
    {
        RebuildTree();
        StatusText = _doc.FilePath != null
            ? $"Loaded: {Path.GetFileName(_doc.FilePath)}"
            : "New world";
    }

    private void OnSelectionChanged()
    {
        var obj = _doc.SelectedObject;
        HasSelectedObject = obj != null;

        if (obj != null)
        {
            ObjX = obj.GetSingle("X");
            ObjY = obj.GetSingle("Y");
            ObjZ = obj.GetSingle("Z");
            ObjAngle = obj.GetSingle("DirFacing");
            ObjScale = obj.GetSingle("Scale", 1f);
            ObjType = obj.GetInt32("Type");
        }
    }

    private void RebuildTree()
    {
        TreeRoots.Clear();
        if (_doc.WorldRoot != null)
            TreeRoots.Add(new TreeNodeViewModel(_doc.WorldRoot));
    }

    [RelayCommand]
    private void SetMode(string mode)
    {
        CurrentMode = Enum.Parse<EditMode>(mode);
        _doc.CurrentMode = CurrentMode;
        StatusText = $"Mode: {CurrentMode}";
    }

    [RelayCommand]
    private void NewWorld()
    {
        _doc.NewWorld(256, 256);
    }

    partial void OnBrushRadiusChanged(float value)
    {
        _doc.BrushRadius = value;
    }

    partial void OnBrushStrengthChanged(float value)
    {
        _doc.BrushStrength = value;
    }

    /// <summary>
    /// Updates the selected object's position from the property panel.
    /// </summary>
    public void ApplyObjectPosition()
    {
        var obj = _doc.SelectedObject;
        if (obj == null) return;

        obj.SetOrAddSingle("X", ObjX);
        obj.SetOrAddSingle("Y", ObjY);
        obj.SetOrAddSingle("Z", ObjZ);
        obj.SetOrAddSingle("DirFacing", ObjAngle);
    }
}
