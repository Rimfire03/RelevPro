using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace RelevPro.Services;

/// <summary>
/// Largeur des colonnes d'un tableau vide. Tant qu'aucune ligne n'est affichée, WPF ne calcule plus les largeurs : toutes
/// les colonnes (proportionnelles comme à largeur fixe) tombent à leur largeur minimale et les titres se brisent lettre par
/// lettre. Tant que le tableau est vide, chaque colonne reçoit donc une largeur minimale :
/// <list type="bullet">
/// <item>colonne à largeur fixe : sa largeur déclarée (au moins la largeur de son titre sur une ligne) ;</item>
/// <item>colonne proportionnelle : sa part de la place restante selon son poids (la première colonne, plus lourde,
/// reste la plus large), au moins la largeur de son titre sur une ligne.</item>
/// </list>
/// Dès qu'une ligne apparaît, les largeurs minimales d'origine sont rétablies et WPF reprend le calcul. Appliqué à tous
/// les tableaux de l'application (voir <see cref="Register"/>).
/// </summary>
public static class EmptyDataGridLayout
{
    /// <summary>Marge ajoutée à la largeur du texte d'un titre (remplissage, poignée de redimensionnement, flèche de tri).</summary>
    private const double HeaderPadding = 34;

    private sealed class State
    {
        public readonly Dictionary<DataGridColumn, double> OriginalMinWidths = new();
    }

    private static readonly ConditionalWeakTable<DataGrid, State> States = new();

    /// <summary>À appeler une fois au démarrage : suit chaque DataGrid de l'application. L'événement Loaded n'est pas relayé
    /// aux gestionnaires de classe par WPF : on s'appuie sur SizeChanged, déclenché à la première mise en page.</summary>
    public static void Register() =>
        EventManager.RegisterClassHandler(typeof(DataGrid), FrameworkElement.SizeChangedEvent, new RoutedEventHandler(OnSizeChanged));

    private static void OnSizeChanged(object sender, RoutedEventArgs e)
    {
        var grid = (DataGrid)sender;
        if (!States.TryGetValue(grid, out _))
        {
            States.Add(grid, new State());
            if (grid.Items is INotifyCollectionChanged items)
                items.CollectionChanged += (_, _) => Schedule(grid);
            DependencyPropertyDescriptor.FromProperty(ItemsControl.ItemsSourceProperty, typeof(DataGrid))
                .AddValueChanged(grid, (_, _) => Schedule(grid));
        }
        // Après le chargement complet (les préférences de colonnes sont appliquées au chargement, voir ColumnChooser).
        Schedule(grid);
    }

    private static void Schedule(DataGrid grid) =>
        grid.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () => Update(grid));

    /// <summary>Largeur du titre de la colonne sur une seule ligne (0 si le titre n'est pas un texte).</summary>
    private static double HeaderNeed(DataGrid grid, DataGridColumn column)
    {
        if (column.Header is not string text || text.Length == 0) return 0;
        var typeface = new Typeface(grid.FontFamily, FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
        var formatted = new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, typeface,
            grid.FontSize, Brushes.Black, VisualTreeHelper.GetDpi(grid).PixelsPerDip);
        return Math.Ceiling(formatted.WidthIncludingTrailingWhitespace) + HeaderPadding;
    }

    private static void Update(DataGrid grid)
    {
        if (!States.TryGetValue(grid, out var state)) return;

        if (grid.Items.Count > 0)
        {
            Restore(state);
            return;
        }

        if (!grid.IsLoaded || grid.ActualWidth <= 0) return;

        var visible = grid.Columns.Where(c => c.Visibility == Visibility.Visible).ToList();
        if (visible.Count == 0) return;

        foreach (var column in visible)
            if (!state.OriginalMinWidths.ContainsKey(column)) state.OriginalMinWidths[column] = column.MinWidth;

        double Need(DataGridColumn c) => Math.Max(state.OriginalMinWidths[c], HeaderNeed(grid, c));

        // Largeurs fixes et automatiques d'abord.
        var fixedTotal = 0.0;
        foreach (var column in visible.Where(c => !c.Width.IsStar))
        {
            var target = Math.Max(column.Width.IsAbsolute ? column.Width.Value : 0, Need(column));
            column.MinWidth = target;
            fixedTotal += target;
        }

        // Puis les colonnes proportionnelles se partagent la place restante.
        var stars = visible.Where(c => c.Width.IsStar).ToList();
        if (stars.Count == 0) return;
        var totalWeight = stars.Sum(c => c.Width.Value);
        if (totalWeight <= 0) return;

        var available = grid.ActualWidth - grid.RowHeaderActualWidth - 8 - fixedTotal;
        foreach (var column in stars)
        {
            var share = available > 0 ? Math.Floor(available * column.Width.Value / totalWeight) : 0;
            column.MinWidth = Math.Max(Need(column), share);
        }
    }

    private static void Restore(State state)
    {
        if (state.OriginalMinWidths.Count == 0) return;
        foreach (var (column, minWidth) in state.OriginalMinWidths) column.MinWidth = minWidth;
        state.OriginalMinWidths.Clear();
    }
}
