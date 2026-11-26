using System.Printing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using RelevPro.ViewModels.Releves;

namespace RelevPro.Services;

/// <summary>Impression de la feuille de saisie en cours : cases vides à remplir à la main, tableau étiré sur toute la page.
/// shortcut: une seule page ; au-delà d'environ 40 lignes la hauteur des cases devient petite, à paginer si besoin.</summary>
public static class SheetPrinter
{
    public static void Print(string title, string period, IReadOnlyList<string> headers, IReadOnlyList<LineRow> rows, bool landscape)
    {
        var pd = new PrintDialog();
        pd.PrintTicket.PageOrientation = landscape ? PageOrientation.Landscape : PageOrientation.Portrait;
        if (pd.ShowDialog() != true) return;

        const double margin = 28;
        var pw = pd.PrintableAreaWidth > 100 ? pd.PrintableAreaWidth : (landscape ? 1122 : 793);
        var ph = pd.PrintableAreaHeight > 100 ? pd.PrintableAreaHeight : (landscape ? 793 : 1122);
        var w = pw - 2 * margin; var h = ph - 2 * margin;

        var font = Math.Clamp(h / (rows.Count + 3) * 0.3, 8, 13);
        var root = new Grid { Width = w, Height = h, Background = Brushes.White };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.Children.Add(new TextBlock
        {
            Text = $"{title} - {period}", FontSize = font + 5, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 8)
        });

        var withSection = rows.Any(r => r.Groupe.Length > 0);
        var offset = withSection ? 2 : 1; // colonnes avant les périodes
        var grid = new Grid();
        Grid.SetRow(grid, 1);
        if (withSection) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.2, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2.6, GridUnitType.Star) });
        foreach (var _ in headers) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        foreach (var _ in rows) grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star), MinHeight = font * 2.2 });

        void Put(int r, int c, string text, bool header = false, bool gray = false)
        {
            var b = new Border
            {
                BorderBrush = Brushes.Black, BorderThickness = new Thickness(0.7), Padding = new Thickness(3, 2, 3, 2),
                Background = header ? new SolidColorBrush(Color.FromRgb(0xE5, 0xE7, 0xEB)) : gray ? new SolidColorBrush(Color.FromRgb(0xF3, 0xF4, 0xF6)) : Brushes.White,
                Child = new TextBlock
                {
                    Text = text, FontSize = font, FontWeight = header ? FontWeights.SemiBold : FontWeights.Normal, TextWrapping = TextWrapping.Wrap,
                    TextAlignment = c >= offset ? TextAlignment.Center : TextAlignment.Left, VerticalAlignment = VerticalAlignment.Center,
                    Foreground = gray ? Brushes.Gray : Brushes.Black
                }
            };
            Grid.SetRow(b, r); Grid.SetColumn(b, c);
            grid.Children.Add(b);
        }

        if (withSection) Put(0, 0, "Section", true);
        Put(0, offset - 1, "Ligne", true);
        for (var i = 0; i < headers.Count; i++) Put(0, offset + i, headers[i], true);
        for (var r = 0; r < rows.Count; r++)
        {
            var row = rows[r];
            if (withSection) Put(r + 1, 0, row.GroupeShown);
            Put(r + 1, offset - 1, row.Unit.Length > 0 ? $"{row.Title} ({row.Unit})" : row.Title);
            for (var i = 0; i < headers.Count; i++)
                Put(r + 1, offset + i, i < row.Cells.Count && !row.Cells[i].Active ? "—" : "", gray: i < row.Cells.Count && !row.Cells[i].Active);
        }
        root.Children.Add(grid);

        root.Measure(new Size(w, h));
        root.Arrange(new Rect(margin, margin, w, h));
        root.UpdateLayout();
        // le visuel est dessiné dans la zone imprimable : décalage de la marge via un conteneur
        var page = new Grid { Width = pw, Height = ph };
        page.Children.Add(root); root.Margin = new Thickness(margin); root.HorizontalAlignment = HorizontalAlignment.Left; root.VerticalAlignment = VerticalAlignment.Top;
        page.Measure(new Size(pw, ph)); page.Arrange(new Rect(0, 0, pw, ph)); page.UpdateLayout();
        pd.PrintVisual(page, $"Relevé {title} {period}");
    }
}
