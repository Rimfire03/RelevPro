using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using RelevPro.ViewModels.Releves;

namespace RelevPro.Views.Releves;

public partial class ReadingView : UserControl
{
    /// <summary>Colonnes de saisie ajoutées dynamiquement (une par semaine / période), à retirer au prochain changement.</summary>
    private readonly List<DataGridColumn> _dynamic = new();
    private const int FirstDynamicIndex = 3; // après Section, Ligne, Récurrence

    public ReadingView()
    {
        InitializeComponent();
        DataContextChanged += (_, e) =>
        {
            if (e.OldValue is ReadingViewModel o) o.ColumnsChanged -= RebuildColumns;
            if (e.NewValue is ReadingViewModel n) { n.ColumnsChanged += RebuildColumns; RebuildColumns(); }
        };
    }

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
            if (e.Key == Key.Enter && s is TextBox tb) tb.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
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
