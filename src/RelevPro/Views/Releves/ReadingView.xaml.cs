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
    private const int FirstDynamicIndex = 3; // après Section, Ligne, Récurrence

    public ReadingView()
    {
        InitializeComponent();
        DataContextChanged += (_, e) =>
        {
            if (e.OldValue is ReadingViewModel o) { o.ColumnsChanged -= RebuildColumns; }
            if (e.NewValue is ReadingViewModel n) { n.ColumnsChanged += RebuildColumns; LoadHidden(n.ViewId); RebuildColumns(); }
        };
        SaisieGrid.PreviewMouseRightButtonUp += OnHeaderRightClick;
    }

    /// <summary>En-têtes (titres) des colonnes masquées par l'utilisateur, conservés quand les colonnes de période sont recréées.</summary>
    private readonly HashSet<string> _hidden = new();

    /// <summary>Colonnes fixes masquées lors d'une session précédente (mémorisées par utilisateur).</summary>
    private void LoadHidden(int viewId)
    {
        _hidden.Clear();
        foreach (var n in ColumnPrefs.Hidden(viewId)) _hidden.Add(n);
        foreach (var c in SaisieGrid.Columns.Where(c => !_dynamic.Contains(c)))
            c.Visibility = _hidden.Contains(c.Header?.ToString() ?? "") ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>Clic droit sur le bandeau des titres : liste cochable des colonnes à afficher ou masquer.</summary>
    private void OnHeaderRightClick(object sender, MouseButtonEventArgs e)
    {
        if ((e.OriginalSource as DependencyObject)?.FindAncestor<System.Windows.Controls.Primitives.DataGridColumnHeader>() is null) return;
        var menu = new ContextMenu();
        foreach (var col in SaisieGrid.Columns)
        {
            var c = col; var name = c.Header?.ToString() ?? "";
            var item = new MenuItem { Header = name, IsCheckable = true, IsChecked = c.Visibility == Visibility.Visible, StaysOpenOnClick = true };
            item.Click += (_, _) =>
            {
                if (!item.IsChecked && SaisieGrid.Columns.Count(x => x.Visibility == Visibility.Visible) <= 1) { item.IsChecked = true; return; } // garde au moins une colonne
                c.Visibility = item.IsChecked ? Visibility.Visible : Visibility.Collapsed;
                if (item.IsChecked) _hidden.Remove(name); else _hidden.Add(name);
                if (DataContext is ReadingViewModel vm && !_dynamic.Contains(c)) // seules les colonnes fixes sont mémorisées (les semaines changent)
                    ColumnPrefs.Save(vm.ViewId, SaisieGrid.Columns.Where(x => !_dynamic.Contains(x) && x.Visibility != Visibility.Visible).Select(x => x.Header?.ToString() ?? ""));
            };
            menu.Items.Add(item);
        }
        menu.PlacementTarget = SaisieGrid; menu.IsOpen = true;
        e.Handled = true;
    }

    private void RebuildColumns()
    {
        if (DataContext is not ReadingViewModel vm) return;
        foreach (var c in _dynamic) SaisieGrid.Columns.Remove(c);
        _dynamic.Clear();
        for (var i = 0; i < vm.ColumnHeaders.Count; i++)
        {
            var col = new DataGridTemplateColumn { Header = vm.ColumnHeaders[i], Width = 100, CellTemplate = CellTemplate(i) };
            if (_hidden.Contains(vm.ColumnHeaders[i])) col.Visibility = Visibility.Collapsed;
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
        box.SetBinding(FrameworkElement.ToolTipProperty, new Binding($"Cells[{i}].Tip"));
        // clic droit : menu « valider » uniquement sur une valeur aberrante (sinon menu standard du champ de saisie)
        box.AddHandler(FrameworkElement.ContextMenuOpeningEvent, new ContextMenuEventHandler((s, e) =>
        {
            if (s is not TextBox tb || tb.DataContext is not LineRow row || i >= row.Cells.Count) return;
            var cell = row.Cells[i];
            if (!cell.Aberrant) { tb.ContextMenu = null; return; }
            var item = new MenuItem { Header = "Valider cette valeur (la marquer normale)" };
            item.Click += (_, _) => row.Owner.ValidateReading(cell);
            tb.ContextMenu = new ContextMenu { Items = { item } };
        }));
        box.AddHandler(UIElement.KeyDownEvent, new KeyEventHandler((s, e) =>
        {
            if (e.Key != Key.Enter || s is not TextBox tb) return;
            tb.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
            FocusNextRow(tb);
            e.Handled = true;
        }));
        grid.AppendChild(box);

        var check = new FrameworkElementFactory(typeof(CheckBox));
        // OneWay + Click : un binding TwoWay dans une ligne de DataGrid est retenu par le BindingGroup de la ligne (jamais écrit tant que la
        // ligne n'est pas validée) : la case semblait cochée mais n'était pas enregistrée, puis se décochait au rechargement.
        check.SetBinding(System.Windows.Controls.Primitives.ToggleButton.IsCheckedProperty, new Binding($"Cells[{i}].IsDone") { Mode = BindingMode.OneWay });
        check.AddHandler(System.Windows.Controls.Primitives.ButtonBase.ClickEvent, new RoutedEventHandler((s, e) =>
        {
            if (s is CheckBox cb && cb.DataContext is LineRow row && i < row.Cells.Count) row.Cells[i].IsDone = cb.IsChecked == true;
        }));
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
