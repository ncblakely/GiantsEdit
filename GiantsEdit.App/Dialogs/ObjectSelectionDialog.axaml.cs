using Avalonia.Controls;
using GiantsEdit.Core.DataModel;

namespace GiantsEdit.App.Dialogs;

public partial class ObjectSelectionDialog : Window
{
    private readonly ObjectCatalog? _catalog;
    private List<ObjectCatalogEntry> _allEntries = [];

    public int? SelectedTypeId { get; private set; }
    public bool Confirmed { get; private set; }

    public ObjectSelectionDialog() => InitializeComponent();

    public ObjectSelectionDialog(ObjectCatalog catalog)
    {
        _catalog = catalog;
        InitializeComponent();

        _allEntries = catalog.Entries.ToList();
        ObjectList.ItemsSource = _allEntries.Select(e => $"{e.Id}: {e.Name} ({e.ModelPath})").ToList();

        TxtFilter.TextChanged += (_, _) => ApplyFilter();

        BtnOk.Click += (_, _) =>
        {
            if (ObjectList.SelectedIndex >= 0)
            {
                var displayed = GetFilteredEntries();
                if (ObjectList.SelectedIndex < displayed.Count)
                {
                    SelectedTypeId = displayed[ObjectList.SelectedIndex].Id;
                    Confirmed = true;
                    Close();
                }
            }
        };

        BtnCancel.Click += (_, _) => Close();

        ObjectList.DoubleTapped += (_, _) =>
        {
            BtnOk.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        };
    }

    private void ApplyFilter()
    {
        var filtered = GetFilteredEntries();
        ObjectList.ItemsSource = filtered.Select(e => $"{e.Id}: {e.Name} ({e.ModelPath})").ToList();
    }

    private List<ObjectCatalogEntry> GetFilteredEntries()
    {
        string filter = TxtFilter.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(filter))
            return _allEntries;

        return _allEntries.Where(e =>
            e.Name.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
            e.ModelPath.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
            e.Id.ToString().Contains(filter)).ToList();
    }
}
