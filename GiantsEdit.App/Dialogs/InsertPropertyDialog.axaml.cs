using Avalonia.Controls;
using GiantsEdit.Core.Formats;

namespace GiantsEdit.App.Dialogs;

public partial class InsertPropertyDialog : Window
{
    private readonly List<InsertableProperty> _allProperties;
    private readonly List<string> _allDisplayNames;

    public InsertableProperty? SelectedProperty { get; private set; }
    public bool Confirmed { get; private set; }

    public InsertPropertyDialog()
    {
        InitializeComponent();

        _allProperties = WorldPropertyCatalog.Properties
            .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        _allDisplayNames = _allProperties.Select(p => p.Name).ToList();

        PropertyList.ItemsSource = _allDisplayNames;

        TxtFilter.TextChanged += (_, _) => ApplyFilter();

        PropertyList.DoubleTapped += (_, _) => Confirm();
        BtnOk.Click += (_, _) => Confirm();
        BtnCancel.Click += (_, _) => Close();
    }

    /// <summary>
    /// Filters out properties that already exist as singletons in the tree.
    /// </summary>
    public void SetExistingProperties(HashSet<string> existingNames)
    {
        var filtered = new List<string>();
        for (int i = 0; i < _allProperties.Count; i++)
        {
            var p = _allProperties[i];
            if (!p.IsRepeatable && existingNames.Contains(p.Name))
                continue;
            filtered.Add(_allDisplayNames[i]);
        }
        PropertyList.ItemsSource = filtered;
    }

    private void ApplyFilter()
    {
        var filter = TxtFilter.Text;
        if (string.IsNullOrWhiteSpace(filter))
        {
            PropertyList.ItemsSource = _allDisplayNames;
            return;
        }

        PropertyList.ItemsSource = _allDisplayNames
            .Where(n => n.Contains(filter, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    private void Confirm()
    {
        if (PropertyList.SelectedItem is not string selected) return;

        int idx = _allDisplayNames.IndexOf(selected);
        if (idx < 0) return;

        SelectedProperty = _allProperties[idx];
        Confirmed = true;
        Close();
    }
}
