using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using RelevPro.Services;
using RelevPro.ViewModels.Releves;

namespace RelevPro.Views.Releves;

public partial class ReadingView : UserControl
{
    /// <summary>Colonnes de saisie ajoutées dynamiquement (une par semaine / période), à retirer au prochain changement.</summary>
    private readonly List<DataGridColumn> _dynamic = new();
    private const int FirstDynamicIndex = 4; // après Actions, Section, Ligne, Récurrence

    public ReadingView()
    {
        InitializeComponent();
        DataContextChanged += (_, e) =>
        {
            if (e.OldValue is ReadingViewModel o) { o.ColumnsChanged -= RebuildColumns; o.PropertyChanged -= OnVmChanged; }
            if (e.NewValue is ReadingViewModel n) { n.ColumnsChanged += RebuildColumns; n.PropertyChanged += OnVmChanged; RebuildColumns(); ShowActions(); }
        };
    }

    private void OnVmChanged(object? s, System.ComponentModel.PropertyChangedEventArgs e) { if (e.PropertyName == nameof(ReadingViewModel.IsEditMode)) ShowActions(); }

    /// <summary>La colonne Actions (Modifier / Supprimer) n'apparaît qu'en mode édition.</summary>
    private void ShowActions() => ActionsColumn.Visibility = (DataContext as ReadingViewModel)?.IsEditMode == true ? Visibility.Visible : Visibility.Collapsed;

    private void RebuildColumns()
    {
        if (DataContext is not ReadingViewModel vm) return;
        foreach (var c in _dynamic) SaisieGrid.Columns.Remove(c);
        _dynamic.Clear();
        for (var i = 0; i < vm.ColumnHeaders.Count; i++)
        {
            var col = new DataGridTemplateColumn { Header = vm.ColumnHeaders[i], Width = 100, CellTemplate = CellTemplate(i) };
            SaisieGrid.Columns.Insert(FirstDynamicIndex + i, col);
            _dynamic.Add(col);
        }
    }

    /// <summary>Entrée : la case de la même colonne, ligne suivante (saisissable), prend le focus. La sauvegarde est différée
    /// puis recharge les lignes (nouveaux contrôles) : on attend donc l'inactivité avant de chercher la case suivante.</summary>
    private static void FocusNextRow(TextBox tb)
    {
        var grid = tb.FindAncestor<DataGrid>();
        var row = tb.FindAncestor<DataGridRow>();
        var column = tb.FindAncestor<DataGridCell>()?.Column;
        if (grid is null || row is null || column is null) return;
        var start = row.GetIndex() + 1;
        grid.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ApplicationIdle, new Action(() =>
        {
            for (var n = start; n < grid.Items.Count; n++)
            {
                grid.ScrollIntoView(grid.Items[n]);
                grid.UpdateLayout();
                if (grid.ItemContainerGenerator.ContainerFromIndex(n) is not DataGridRow r || column.GetCellContent(r) is not { } content) continue;
                if (FindBox(content) is { IsVisible: true, IsEnabled: true } box) { box.Focus(); box.SelectAll(); return; }
            }
        }));
    }

    private static TextBox? FindBox(DependencyObject d)
    {
        if (d is TextBox t) return t;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(d); i++)
            if (FindBox(VisualTreeHelper.GetChild(d, i)) is { } f) return f;
        return null;
    }

    private static DataTemplate CellTemplate(int i)
    {
        var grid = new FrameworkElementFactory(typeof(Grid));
        grid.SetBinding(Panel.BackgroundProperty, new Binding($"Cells[{i}].Background"));

        var box = new FrameworkElementFactory(typeof(TextBox));
        box.SetBinding(TextBox.TextProperty, new Binding($"Cells[{i}].Text") { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.LostFocus });
        box.SetBinding(UIElement.VisibilityProperty, new Binding($"Cells[{i}].BoxVisibility"));
        box.SetBinding(Control.BackgroundProperty, new Binding($"Cells[{i}].BoxBackground"));
        box.SetValue(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Right);
        box.SetValue(Control.VerticalContentAlignmentProperty, VerticalAlignment.Center);
        box.SetValue(FrameworkElement.MarginProperty, new Thickness(3, 2, 3, 2));
        box.SetValue(Control.PaddingProperty, new Thickness(6, 0, 6, 0)); // le style du thème (padding 8,7) rogne le texte dans une ligne de 34 px
        box.SetValue(FrameworkElement.MinHeightProperty, 0.0);
        box.AddHandler(UIElement.KeyDownEvent, new KeyEventHandler((s, e) =>
        {
            if (e.Key != Key.Enter || s is not TextBox tb) return;
            tb.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
            FocusNextRow(tb);
            e.Handled = true;
        }));
        grid.AppendChild(box);

        var check = new FrameworkElementFactory(typeof(CheckBox));
        check.SetBinding(System.Windows.Controls.Primitives.ToggleButton.IsCheckedProperty, new Binding($"Cells[{i}].IsDone") { Mode = BindingMode.TwoWay });
        check.SetBinding(UIElement.VisibilityProperty, new Binding($"Cells[{i}].CheckVisibility"));
        check.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        check.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        grid.AppendChild(check);

        var blank = new FrameworkElementFactory(typeof(TextBlock));
        blank.SetBinding(TextBlock.TextProperty, new Binding($"Cells[{i}].Blank"));
        blank.SetBinding(UIElement.VisibilityProperty, new Binding($"Cells[{i}].BlankVisibility"));
        blank.SetValue(TextBlock.ForegroundProperty, Brushes.Gray);
        blank.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        blank.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        grid.AppendChild(blank);

        return new DataTemplate { VisualTree = grid };
    }
}
