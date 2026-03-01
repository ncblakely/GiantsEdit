using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using GiantsEdit.Core.DataModel;

namespace GiantsEdit.App.Dialogs;

public partial class IconPickerDialog : Window
{
    private readonly List<IconItem> _allItems;

    public IconPickerDialog()
    {
        InitializeComponent();
        _allItems = [];
    }

    public IconPickerDialog(string title, IEnumerable<int> selectedIds,
        IReadOnlyList<(int Id, string Name)>? availableIcons = null) : this()
    {
        Title = title;
        var selected = new HashSet<int>(selectedIds);
        var icons = availableIcons ?? IconNames.All;

        _allItems = icons
            .Select(ic => new IconItem(ic.Id, ic.Name, selected.Contains(ic.Id)))
            .ToList();

        RefreshList();

        TxtSearch.TextChanged += (_, _) => RefreshList();

        BtnSelectAll.Click += (_, _) =>
        {
            foreach (var item in _allItems) item.IsSelected = true;
            RefreshList();
        };

        BtnSelectNone.Click += (_, _) =>
        {
            foreach (var item in _allItems) item.IsSelected = false;
            RefreshList();
        };

        BtnOk.Click += (_, _) =>
        {
            // Sync selection state from visible checkboxes
            SyncSelection();
            Close(_allItems.Where(i => i.IsSelected).Select(i => i.Id).ToList());
        };

        BtnCancel.Click += (_, _) => Close(null);
    }

    private void RefreshList()
    {
        SyncSelection();
        var filter = TxtSearch.Text?.Trim() ?? "";
        var visible = string.IsNullOrEmpty(filter)
            ? _allItems
            : _allItems.Where(i =>
                i.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)
                || i.Id.ToString().Contains(filter)).ToList();

        IconList.Items.Clear();
        foreach (var item in visible)
        {
            var cb = new CheckBox
            {
                Content = $"{item.Name} ({item.Id})",
                IsChecked = item.IsSelected,
                Tag = item
            };
            IconList.Items.Add(cb);
        }
    }

    private void SyncSelection()
    {
        foreach (var uiItem in IconList.Items)
        {
            if (uiItem is CheckBox cb && cb.Tag is IconItem item)
                item.IsSelected = cb.IsChecked == true;
        }
    }

    private class IconItem(int id, string name, bool isSelected)
    {
        public int Id { get; } = id;
        public string Name { get; } = name;
        public bool IsSelected { get; set; } = isSelected;
    }
}
