using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Expovice.App.Core.Ui;

/// <summary>
/// One chip/pill. Non-generic base so XAML can use it as x:DataType; <see cref="SelectableItem{T}"/> adds the value.
/// UI-thread-affine (not thread-safe), like all view-model state.
/// </summary>
public partial class SelectableItem : ObservableObject
{
    protected SelectableItem(string label, string? caption, string? icon)
    {
        Label = label;
        Caption = caption;
        Icon = icon;
    }

    public string Label { get; }

    /// <summary>Optional small text above the label (day buttons: "Today" over "Sat 17").</summary>
    public string? Caption { get; }

    /// <summary>Optional Lucide icon key (category chips).</summary>
    public string? Icon { get; }

    [ObservableProperty]
    private bool isSelected;

    public IRelayCommand SelectCommand { get; protected init; } = null!;
}

public sealed class SelectableItem<T> : SelectableItem
{
    public SelectableItem(T value, string label, Action<SelectableItem<T>> onSelect, bool isSelected = false, string? caption = null, string? icon = null)
        : base(label, caption, icon)
    {
        Value = value;
        IsSelected = isSelected;
        SelectCommand = new RelayCommand(() => onSelect(this));
    }

    public T Value { get; }
}

/// <summary>Selection helpers for chip lists (DRY across every screen).</summary>
public static class Chips
{
    /// <summary>Radio behaviour: exactly the tapped item becomes selected.</summary>
    public static void SelectOnly<T>(IEnumerable<SelectableItem<T>> items, SelectableItem<T> picked)
    {
        foreach (var item in items)
        {
            item.IsSelected = ReferenceEquals(item, picked);
        }
    }

    public static void Toggle(SelectableItem item) => item.IsSelected = !item.IsSelected;

    public static List<T> Selected<T>(IEnumerable<SelectableItem<T>> items) =>
        items.Where(i => i.IsSelected).Select(i => i.Value).ToList();

    public static T? SelectedValue<T>(IEnumerable<SelectableItem<T>> items) =>
        items.Where(i => i.IsSelected).Select(i => i.Value).FirstOrDefault();
}
