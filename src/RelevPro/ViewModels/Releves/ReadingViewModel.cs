using System.Collections.ObjectModel;
using System.Data;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using RelevPro.Core.Models;
using RelevPro.Core.Services;
using RelevPro.Services;

namespace RelevPro.ViewModels.Releves;

/// <summary>Case de la grille de saisie : une ligne (compteur ou tâche) pour une colonne (semaine, mois...). Seules les
/// cases « actives » et non futures se saisissent ; une ligne plus lente que la vue n'est active que dans la première
/// colonne de sa période.</summary>
public partial class GridCell : ObservableObject
{
    private readonly LineRow _row;
    public Periods.Column Column { get; }

    /// <summary>Début de la période de récurrence de la ligne (clé des tâches cochées).</summary>
    public DateTime PeriodStart { get; }
    public bool Active { get; }
    public bool Editable { get; }
    public int? ReadingId { get; }

    /// <summary>Case qui déclenche une notification (saisie en retard, ou valeur forcée récente) : fond rouge pastel.</summary>
    public bool Alert { get; }
    private static readonly System.Windows.Media.Brush AlertBrush = MakeAlertBrush();
    private static System.Windows.Media.Brush MakeAlertBrush()
    {
        var b = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xFE, 0xCA, 0xCA));
        b.Freeze();
        return b;
    }
    /// <summary>Valeur aberrante : relevé forcé malgré une alerte, pas encore validé (clic droit sur la case). Couleur propre (violet pastel).</summary>
    public bool Aberrant { get; }
    public string? Tip { get; }
    private static readonly System.Windows.Media.Brush AberrantBrush = MakeAberrantBrush();
    private static System.Windows.Media.Brush MakeAberrantBrush()
    {
        var b = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xE9, 0xD5, 0xFF));
        b.Freeze();
        return b;
    }
    public System.Windows.Media.Brush? Background => Aberrant ? AberrantBrush : Alert ? AlertBrush : null;
    public System.Windows.Media.Brush BoxBackground => Aberrant ? AberrantBrush : Alert ? AlertBrush : System.Windows.Media.Brushes.White;

    [ObservableProperty] private string _text = string.Empty;
    [ObservableProperty] private bool _isDone;

    public GridCell(LineRow row, Periods.Column column, DateTime periodStart, bool active, bool editable, int? readingId, bool alert = false, bool aberrant = false, string? tip = null)
    {
        _row = row; Column = column; PeriodStart = periodStart; Active = active; Editable = editable; ReadingId = readingId; Alert = alert; Aberrant = aberrant; Tip = tip;
    }

    public Visibility BoxVisibility => Editable && _row.IsMeter ? Visibility.Visible : Visibility.Collapsed;
    public Visibility CheckVisibility => Editable && _row.IsTask ? Visibility.Visible : Visibility.Collapsed;
    public Visibility BlankVisibility => Editable ? Visibility.Collapsed : Visibility.Visible;
    public string Blank => !Active ? "—" : (!Editable && Text.Length > 0 ? Text : string.Empty); // case en lecture seule : valeur affichée

    partial void OnTextChanged(string value) => Defer(() => _row.Owner.SaveCell(this));
    partial void OnIsDoneChanged(bool value) => Defer(() => _row.Owner.ToggleTask(this, value));

    private void Defer(Action a)
    {
        if (!_row.Owner.Loaded) return;
        Application.Current.Dispatcher.BeginInvoke(a);
    }

    public LineRow Row => _row;
}

/// <summary>Ligne de la grille : un compteur ou une tâche, avec ses cases par colonne.</summary>
public partial class LineRow : ObservableObject
{
    public ReadingViewModel Owner { get; }
    public ViewLine Line { get; }
    public List<GridCell> Cells { get; } = new();

    public LineRow(ReadingViewModel owner, ViewLine line) { Owner = owner; Line = line; }

    public string Groupe => Line.Groupe;
    public string GroupeShown { get; set; } = string.Empty;
    public VerticalAlignment GroupeAlign { get; set; } = VerticalAlignment.Center;
    /// <summary>Dernière ligne de sa section : seule sa cellule « Section » garde un trait du bas (cellules fusionnées).</summary>
    public bool IsGroupEnd { get; set; }
    public string Title => Line.Titre;
    public bool IsMeter => Line.Kind == LineKind.Compteur;
    public bool IsTask => !IsMeter;
    public bool IsStopped => Line.Meter?.Arrete ?? false;
    public string KindIcon => IsMeter ? "🔢" : "☑";
    public string RecurrenceName => Periods.Name(Line.Recurrence);
    public string Unit => Line.Meter?.Unite ?? string.Empty;

    public string ConsumptionText { get; init; } = string.Empty;
    public string StatusText { get; init; } = string.Empty;
    public Brush StatusBrush { get; init; } = Brushes.Gray;

    [RelayCommand] private void Edit() => Owner.EditLine(this);
    [RelayCommand] private void Delete() => Owner.DeleteLine(this);
}

/// <summary>Carte de l'onglet Statistiques : un graphique par ligne (barres mensuelles, courbe ou taux de réalisation).</summary>
public sealed class ChartCardVm
{
    private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");
    public ConsumptionService.ChartCard Card { get; }
    public int Year { get; }
    public ChartCardVm(ConsumptionService.ChartCard card, int year) { Card = card; Year = year; }

    public string Title => Card.Title;
    public bool IsCounter => Card.Kind == "Compteur";
    public bool IsMeasure => Card.Kind == "Mesure";
    public bool IsTask => Card.Kind == "Tache";
    public decimal?[] Months => Card.Months;
    public decimal?[] PrevMonths => Card.PrevMonths;
    public List<(DateTime Date, decimal Value)> Points => Card.Points;
    public double Ratio => Card.Expected == 0 ? 0 : (double)Card.Done / Card.Expected;
    public int Done => Card.Done;
    public int Expected => Card.Expected;
    public string Unit => Card.Unit;
    public int PrevYear => Year - 1;
    public bool IsEmptyTask => IsTask && Card.Expected == 0;

    public string Headline => IsCounter ? (Card.Total is { } t ? $"{t.ToString("#,0.##", Fr)} {Card.Unit}".Trim() : "—")
        : IsMeasure ? (Points.Count == 0 ? "—" : $"{Points[^1].Value.ToString("#,0.##", Fr)} {Card.Unit}".Trim())
        : Expected == 0 ? "—" : $"{Done}/{Expected}";

    public string SubLine
    {
        get
        {
            if (IsCounter)
            {
                if (Card.Total is { } t && Card.PrevTotal is > 0 and var p)
                {
                    var d = (t - p) / p * 100m;
                    return $"{(d > 0 ? "▲" : d < 0 ? "▼" : "=")} {Math.Abs(d):0} % vs {PrevYear} (même période)";
                }
                return $"Consommation {Year}";
            }
            if (IsMeasure) return Points.Count == 0 ? "Aucun relevé" : $"dernier relevé · min {Points.Min(x => x.Value):#,0.##} · max {Points.Max(x => x.Value):#,0.##}";
            return Card.Expected == 0 ? "Aucune période échue" : $"périodes réalisées en {Year}";
        }
    }

    /// <summary>Sens de l'évolution (compteur) : sert à colorer la ligne de synthèse.</summary>
    public string Trend => IsCounter && Card.Total is { } t && Card.PrevTotal is > 0 and var p ? (t > p ? "up" : t < p ? "down" : "flat") : "flat";
}

public partial class ReadingViewModel : EditableScreenViewModel
{
    private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");
    private static readonly Brush Ok = Freeze("#16A34A"), Late = Freeze("#DC2626"), Pending = Freeze("#D97706"), None = Freeze("#94A3B8");

    private readonly MainViewModel _main;
    private readonly int _viewId;
    public int ViewId => _viewId;
    private Recurrence _rec = Recurrence.Hebdo;
    internal bool Loaded;

    public string ViewName { get; private set; } = string.Empty;
    public string ViewSubtitle { get; private set; } = string.Empty;
    public ObservableCollection<LineRow> Lines { get; } = new();
    public List<int> Years { get; private set; } = new();

    /// <summary>Colonnes de la grille (une par période de la vue) ; la vue XAML recrée ses colonnes sur <see cref="ColumnsChanged"/>.</summary>
    public List<string> ColumnHeaders { get; private set; } = new();
    public event Action? ColumnsChanged;

    [ObservableProperty] private DateTime _refDate = DateTime.Today;
    [ObservableProperty] private int _selectedYear = DateTime.Today.Year;
    [ObservableProperty] private DataView? _monthlyTable;
    [ObservableProperty] private DataView? _yearlyTable;
    public ObservableCollection<ChartCardVm> ChartCards { get; } = new();
    [ObservableProperty] private string _statusMessage = string.Empty;
    [ObservableProperty] private string _periodTitle = string.Empty;
    [ObservableProperty] private string _consumptionHeader = "Consommation";
    [ObservableProperty] private bool _showStopped;
    public bool HasStopped { get; private set; }

    partial void OnShowStoppedChanged(bool value) => ReloadLines();

    public ReadingViewModel(MainViewModel main, int viewId)
    {
        _main = main;
        _viewId = viewId;
        var v = App.Db.ReadingViews.AsNoTracking().FirstOrDefault(x => x.Id == viewId);
        ViewName = v?.Nom ?? "(vue supprimée)";
        _rec = v?.Recurrence ?? Recurrence.Hebdo;
        ViewSubtitle = (v is { Famille.Length: > 0 } ? v.Famille + " · " : "") + "Vue " + Periods.Name(_rec).ToLowerInvariant();
        Reload();
    }

    partial void OnRefDateChanged(DateTime value) => ReloadLines();
    partial void OnSelectedYearChanged(int value) { ReloadMonthly(); ReloadStats(); }

    private static Brush Freeze(string hex)
    {
        var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        b.Freeze();
        return b;
    }

    public void Reload()
    {
        Years = ConsumptionService.YearsWithData(App.Db, _viewId);
        if (!Years.Contains(SelectedYear)) _selectedYear = Years[^1];
        OnPropertyChanged(nameof(Years));
        OnPropertyChanged(nameof(SelectedYear));
        ReloadLines();
        ReloadMonthly();
        ReloadYearly();
        ReloadStats();
    }

    private Recurrence LineRec(ViewLine l) => l.Recurrence < _rec ? _rec : l.Recurrence;

    private void ReloadLines()
    {
        Loaded = false;
        // valeurs aberrantes devenues normales (valeur voisine corrigée...) : plus colorées ni notifiées
        var viewMeters = App.Db.ViewLines.AsNoTracking().Where(l => l.ReadingViewId == _viewId && l.MeterId != null).Select(l => l.MeterId!.Value).Distinct().ToList();
        if (ReadingValidator.RecheckForced(App.Db, viewMeters) > 0) _main?.RefreshNotificationBadge();
        Lines.Clear();
        var today = DateTime.Today;
        var cols = Periods.Columns(_rec, RefDate);
        var window = Periods.Window(_rec);
        var wStart = Periods.Start(window, RefDate);
        var wEnd = Periods.Next(window, wStart);
        PeriodTitle = Periods.Label(window, wStart);
        ConsumptionHeader = "Consommation " + PeriodTitle;
        var headers = cols.Select(c => Periods.ColumnLabel(_rec, c.Start)).ToList();
        if (!headers.SequenceEqual(ColumnHeaders)) { ColumnHeaders = headers; ColumnsChanged?.Invoke(); }

        var lines = App.Db.ViewLines.AsNoTracking().Include(l => l.Meter)
            .Where(l => l.ReadingViewId == _viewId).OrderBy(l => l.Ordre).ThenBy(l => l.Id).ToList();
        var meterIds = lines.Where(l => l.MeterId != null).Select(l => l.MeterId!.Value).Distinct().ToList();
        var readings = App.Db.MeterReadings.AsNoTracking().Where(r => meterIds.Contains(r.MeterId)).OrderBy(r => r.Date).ThenBy(r => r.Id).ToList().ToLookup(r => r.MeterId);
        HasStopped = lines.Any(l => l.Meter?.Arrete ?? false);
        OnPropertyChanged(nameof(HasStopped));
        if (!ShowStopped) lines = lines.Where(l => !(l.Meter?.Arrete ?? false)).ToList();
        var lineIds = lines.Select(l => l.Id).ToList();
        var completions = App.Db.TaskCompletions.AsNoTracking().Where(t => lineIds.Contains(t.ViewLineId)).ToList();

        var built = new List<LineRow>();
        foreach (var l in lines)
        {
            var r = LineRec(l);
            var stopped = l.Meter?.Arrete ?? false;
            var all = l.Kind == LineKind.Compteur ? readings[l.MeterId ?? 0].ToList() : new List<MeterReading>();
            var cells = new List<(Periods.Column Col, DateTime Lp, DateTime LpEnd, bool Active, bool Editable, MeterReading? Existing, bool Done)>();
            var mp = l.MoisPeriode(_rec);
            var currentStart = Periods.Start(r, today, mp);
            var previousStart = Periods.Previous(r, currentStart);
            foreach (var c in cols)
            {
                var active = Periods.IsActive(_rec, r, c.Start, mp);
                var lp = Periods.Start(r, c.Start, mp);
                var lpEnd = Periods.Next(r, lp);
                MeterReading? existing = null;
                var done = false;
                if (active)
                {
                    if (l.Kind == LineKind.Compteur)
                    {
                        var (from, to) = r == _rec ? (c.Start, c.End) : (lp, lpEnd);
                        existing = all.LastOrDefault(x => x.Date >= from && x.Date < to);
                    }
                    else done = completions.Any(t => t.ViewLineId == l.Id && t.PeriodStart.Date == lp);
                }
                cells.Add((c, lp, lpEnd, active, active && c.Start <= today && !stopped, existing, done));
            }

            // statut de la ligne : en retard si une case échue (période de la ligne terminée) est vide
            var due = cells.Where(x => x.Active && x.Col.Start <= today).ToList();
            string status; Brush brush;
            if (stopped) (status, brush) = ("Arrêté", None);
            else if (due.Count == 0) (status, brush) = ("—", None);
            else
            {
                var missing = due.Where(x => l.Kind == LineKind.Compteur ? x.Existing is null : !x.Done).ToList();
                if (missing.Count == 0) (status, brush) = ("Saisi", Ok);
                else if (missing.Any(x => (r == _rec ? x.Col.End : x.LpEnd) <= today && l.CreatedOn.Date < x.Col.End)) (status, brush) = ("En retard", Late);
                else (status, brush) = ("À saisir", Pending);
            }

            var cons = string.Empty;
            if (l.Kind == LineKind.Compteur && l.Meter is { Cumulatif: true } m && ConsumptionService.Consumption(all, wStart, wEnd, ConsumptionService.Capacity(m, all)) is { } v)
                cons = $"{v.ToString("#,0.##", Fr)} {m.Unite}".Trim();

            var row = new LineRow(this, l) { ConsumptionText = cons, StatusText = status, StatusBrush = brush };
            foreach (var x in cells)
            {
                // même règle que le centre de notifications : période précédente non saisie (en retard) ou valeur forcée récente
                var lateNow = r == Recurrence.Hebdo && x.Lp == currentStart && today > currentStart && l.CreatedOn.Date < today; // hebdo : retard dès date + 1 jour
                var missing = x.Active && !stopped && ((x.Lp == previousStart && l.CreatedOn.Date < currentStart) || lateNow)
                              && (l.Kind == LineKind.Compteur ? x.Existing is null : !x.Done);
                var aberrant = x.Existing is { Forced: true } && !stopped;
                var cell = new GridCell(row, x.Col, x.Lp, x.Active, x.Editable, x.Existing?.Id, missing, aberrant,
                    aberrant ? "Valeur aberrante : " + x.Existing!.Anomaly + "\nClic droit pour la valider." : null)
                {
                    Text = x.Existing is null ? string.Empty : x.Existing.Value.ToString("0.####", Fr),
                    IsDone = x.Done
                };
                row.Cells.Add(cell);
            }
            built.Add(row);
        }
        // section affichée au milieu de son bloc de lignes (cellules fusionnées) ; bloc pair : ligne du haut du milieu, calée en bas
        for (var i = 0; i < built.Count;)
        {
            var j = i;
            while (j + 1 < built.Count && built[j + 1].Groupe == built[i].Groupe) j++;
            var n = j - i + 1;
            var mid = built[i + (n - 1) / 2];
            if (built[i].Groupe.Length > 0) { mid.GroupeShown = built[i].Groupe; mid.GroupeAlign = n % 2 == 0 ? VerticalAlignment.Bottom : VerticalAlignment.Center; }
            built[j].IsGroupEnd = true;
            i = j + 1;
        }
        foreach (var r in built) Lines.Add(r);
        Loaded = true;
    }

    private void ReloadMonthly()
    {
        var rows = ConsumptionService.Monthly(App.Db, _viewId, SelectedYear);
        var cols = new List<string> { "Compteur", "Unité" };
        cols.AddRange(Enumerable.Range(1, 12).Select(m => Fr.DateTimeFormat.GetAbbreviatedMonthName(m).TrimEnd('.'))); // un point dans le nom de colonne casse la liaison de la grille (cellules vides)
        cols.Add("Total");
        MonthlyTable = Table(cols, rows.Select(r => new object?[] { r.Compteur, r.Unite }.Concat(r.Values.Cast<object?>()).Append(r.Total).ToArray()));
    }

    private void ReloadYearly()
    {
        var years = Years;
        var rows = ConsumptionService.Yearly(App.Db, _viewId, years);
        var cols = new List<string> { "Compteur", "Unité" };
        cols.AddRange(years.Select(y => y.ToString()));
        YearlyTable = Table(cols, rows.Select(r => new object?[] { r.Compteur, r.Unite }.Concat(r.Values.Cast<object?>()).ToArray()));
    }

    private void ReloadStats()
    {
        ChartCards.Clear();
        foreach (var c in ConsumptionService.Charts(App.Db, _viewId, SelectedYear, DateTime.Today)) ChartCards.Add(new ChartCardVm(c, SelectedYear));
    }

    private static DataView Table(List<string> cols, IEnumerable<object?[]> rows)
    {
        var t = new DataTable();
        foreach (var c in cols) t.Columns.Add(c, typeof(string));
        foreach (var r in rows)
            t.Rows.Add(r.Select(v => v switch { null => "", decimal d => d.ToString("#,0.##", Fr), _ => v.ToString() }).Cast<object>().ToArray());
        return t.DefaultView;
    }

    // ---- Saisie ----

    /// <summary>Clic droit « valider » : la valeur aberrante est reconnue normale (plus colorée, plus de notification).</summary>
    internal void ValidateReading(GridCell cell)
    {
        if (!App.GuardWritable()) return;
        if (cell.ReadingId is not { } id || App.Db.MeterReadings.FirstOrDefault(r => r.Id == id) is not { } r) return;
        r.Forced = false;
        r.Anomaly = string.Empty;
        App.Db.SaveChanges();
        StatusMessage = "Valeur validée comme normale.";
        Reload();
        _main.RefreshNotificationBadge();
    }

    internal void SaveCell(GridCell cell)
    {
        if (!App.GuardWritable()) { ReloadLines(); return; }
        var text = (cell.Text ?? "").Trim().Replace(" ", "");
        if (text.Length == 0)
        {
            // case effacée : le relevé est supprimé, la case repasse à « non saisie »
            if (cell.ReadingId is { } rid && App.Db.MeterReadings.FirstOrDefault(r => r.Id == rid) is { } old)
            {
                App.Db.MeterReadings.Remove(old);
                App.Db.SaveChanges();
                Reload();
                _main.RefreshNotificationBadge();
            }
            else ReloadLines();
            return;
        }
        if (!decimal.TryParse(text.Replace(',', '.'), NumberStyles.Number, CultureInfo.InvariantCulture, out var value))
        {
            App.Dialogs.ShowMessage("Valeur invalide", "Saisissez un nombre (ex. 1234,5).");
            ReloadLines();
            return;
        }
        var meter = App.Db.Meters.First(m => m.Id == cell.Row.Line.MeterId);
        if (meter.Tronquer) value = Math.Truncate(value);
        var all = App.Db.MeterReadings.Where(r => r.MeterId == meter.Id).ToList();
        var existing = cell.ReadingId is { } id ? all.FirstOrDefault(r => r.Id == id) : null;
        if (existing is not null && existing.Value == value) { ReloadLines(); return; }
        // un relevé en retard est daté de la période de la case (jamais dans le futur)
        var date = existing?.Date.Date ?? Max(cell.Column.Start, Min(DateTime.Today, cell.Column.End.AddDays(-1)));
        // retour à une valeur proche de 0 : tour complet ou compteur changé ? La consommation en dépend, donc on demande toujours.
        var tour = false; var replaced = false;
        var others = all.Where(r => r != existing).ToList();
        var prevR = others.Where(r => r.Date < date).OrderBy(r => r.Date).LastOrDefault();
        var capacity = ConsumptionService.Capacity(meter, all);
        if (meter.Cumulatif && prevR is not null && value < prevR.Value && value <= prevR.Value * 0.1m)
        {
            var likelyTour = capacity > 0 && prevR.Value >= capacity * 0.8m;
            var head = $"{prevR.Value.ToString("0.####", Fr)} le {prevR.Date:dd/MM/yyyy}, puis {value.ToString("0.####", Fr)} : retour à une valeur proche de 0.\n" +
                       (likelyTour ? "Le relevé précédent était proche de la capacité : probablement un TOUR complet.\n\n" : "Le relevé précédent était loin de la capacité : probablement un COMPTEUR CHANGÉ.\n\n");
            tour = App.Dialogs.ShowConfirm($"Le compteur a-t-il fait un tour ? - {meter.Nom}",
                head + $"Oui : tour complet (repassé à 0 après {capacity.ToString("0.####", Fr)}) ; consommation de l'écart : {(capacity - prevR.Value + value).ToString("0.####", Fr)} {meter.Unite}.\n" +
                "Non : autre cause (compteur changé, erreur de saisie).\n\nLa capacité se règle dans la fiche du compteur (mode édition, par défaut : puissance de 10 au-dessus du plus grand relevé).");
            if (!tour)
            {
                replaced = App.Dialogs.ShowConfirm($"Compteur changé ? - {meter.Nom}",
                    head + $"Oui : le compteur a été remplacé ; consommation de l'écart : {value.ToString("0.####", Fr)} {meter.Unite} (le nouveau compteur part de 0).\n" +
                    "Non : erreur de saisie, la valeur est signalée comme anomalie (vous pourrez la corriger ou la forcer).");
            }
        }
        var anomalies = ReadingValidator.Check(meter, date, value, others, tour ? capacity : 0, replaced);

        var forced = false;
        if (anomalies.Count > 0)
        {
            var choice = App.Dialogs.ShowChoice($"Anomalie de saisie - {meter.Nom}", string.Join("\n", anomalies),
                "Valider la valeur (forcer, la marquer normale)", "Forcer la valeur (à contrôler plus tard)", "Corriger la saisie");
            if (choice is not (0 or 1))
            {
                ReloadLines();
                return;
            }
            forced = choice == 1; // 0 : acceptée comme normale, ni colorée ni notifiée
        }

        if (existing is null)
        {
            existing = new MeterReading { MeterId = meter.Id, Date = date };
            App.Db.MeterReadings.Add(existing);
        }
        existing.Value = value;
        existing.Tour = tour;
        existing.Remplacement = replaced;
        existing.Forced = forced;
        existing.Anomaly = forced ? string.Join(" ", anomalies) : string.Empty;
        existing.EnteredBy = Environment.UserName;
        existing.EnteredAt = DateTime.Now;
        App.Db.SaveChanges();
        StatusMessage = $"{meter.Nom} : {value.ToString("0.####", Fr)} {meter.Unite} enregistré{(forced ? " (valeur forcée)" : "")}.";
        Reload();
        _main.RefreshNotificationBadge();
    }

    private static DateTime Min(DateTime a, DateTime b) => a < b ? a : b;
    private static DateTime Max(DateTime a, DateTime b) => a > b ? a : b;

    internal void ToggleTask(GridCell cell, bool done)
    {
        if (!App.GuardWritable()) { ReloadLines(); return; }
        var lineId = cell.Row.Line.Id;
        var existing = App.Db.TaskCompletions.FirstOrDefault(t => t.ViewLineId == lineId && t.PeriodStart == cell.PeriodStart);
        if (done && existing is null)
            App.Db.TaskCompletions.Add(new TaskCompletion { ViewLineId = lineId, PeriodStart = cell.PeriodStart, CompletedAt = DateTime.Now, CompletedBy = Environment.UserName });
        else if (!done && existing is not null)
            App.Db.TaskCompletions.Remove(existing);
        App.Db.SaveChanges();
        ReloadLines();
        _main.RefreshNotificationBadge();
    }

    // ---- Navigation ----

    /// <summary>Imprime la période affichée avec des cases vides à remplir à la main (paysage pour une vue hebdomadaire, sinon portrait).</summary>
    [RelayCommand] private void PrintSheet() => Services.SheetPrinter.Print(ViewName, PeriodTitle, ColumnHeaders, Lines.ToList(), _rec == Recurrence.Hebdo);
    [RelayCommand] private void Today() => RefDate = DateTime.Today;
    [RelayCommand] private void PreviousPeriod() { var w = Periods.Window(_rec); RefDate = Periods.Previous(w, Periods.Start(w, RefDate)); }
    [RelayCommand] private void NextPeriod() { var w = Periods.Window(_rec); RefDate = Periods.Next(w, Periods.Start(w, RefDate)); }

    // ---- Lignes de la vue ----

    /// <summary>Récurrences permises : celle de la vue ou plus lentes (une ligne n'est jamais plus rapide que sa vue).</summary>
    private List<Recurrence> Allowed => Enum.GetValues<Recurrence>().Where(r => r >= _rec).ToList();
    private static readonly List<string> MonthNames = Enumerable.Range(1, 12).Select(i => System.Globalization.CultureInfo.GetCultureInfo("fr-FR").TextInfo.ToTitleCase(System.Globalization.CultureInfo.GetCultureInfo("fr-FR").DateTimeFormat.GetMonthName(i))).ToList();
    private const string StartMonthLabel = "Mois de début de la période de calcul (lignes trimestrielles, semestrielles ou annuelles, plus lentes que la vue)";
    private List<string> AllowedNames => Allowed.Select(Periods.Name).ToList();

    private string LastGroup() => App.Db.ViewLines.Where(l => l.ReadingViewId == _viewId).OrderByDescending(l => l.Ordre).Select(l => l.Groupe).FirstOrDefault() ?? string.Empty;

    [RelayCommand]
    private void AddMeterLine()
    {
        if (!App.GuardWritable() || !GuardEdit()) return;
        var rec = 0; var mois = 0;
        var groupe = LastGroup();
        var meter = new Meter { Ordre = (App.Db.Meters.Max(x => (int?)x.Ordre) ?? -1) + 1 };
        var extra = new[]
        {
            EditField.ComboField("Récurrence de saisie", AllowedNames, () => rec, v => rec = v),
            EditField.ComboField(StartMonthLabel, MonthNames, () => mois, v => mois = v),
            EditField.Text("Section (regroupe les lignes, facultatif)", () => groupe, v => groupe = v)
        };
        if (!MeterEditor.Edit("Nouveau compteur", meter, extra)) return;
        // le compteur appartient à cette vue seule : ses relevés ne sont pas partagés
        AddLine(new ViewLine { Kind = LineKind.Compteur, Meter = meter, Recurrence = Allowed[Math.Clamp(rec, 0, Allowed.Count - 1)], MoisDebut = mois + 1, Groupe = groupe.Trim() });
    }

    /// <summary>Ligne sélectionnée dans la grille de saisie : cible des boutons « Modifier » / « Supprimer » du bandeau.</summary>
    [ObservableProperty] private LineRow? _selectedLine;

    [RelayCommand] private void EditSelected() { if (GuardEdit() && SelectedLine is { } l) EditLine(l); else if (SelectedLine is null && App.IsEditMode) App.Dialogs.ShowMessage("Modifier", "Sélectionnez d'abord une ligne dans le tableau."); }
    [RelayCommand] private void DeleteSelected() { if (GuardEdit() && SelectedLine is { } l) DeleteLine(l); else if (SelectedLine is null && App.IsEditMode) App.Dialogs.ShowMessage("Supprimer", "Sélectionnez d'abord une ligne dans le tableau."); }

    [RelayCommand] private void ManageUnits() { if (!GuardEdit()) return; new Views.Dialogs.CatalogWindow(CatalogKind.Unite, "Catalogue des unités") { Owner = Application.Current.MainWindow }.ShowDialog(); }
    [RelayCommand] private void ManageFamilies() { if (!GuardEdit()) return; new Views.Dialogs.CatalogWindow(CatalogKind.Famille, "Familles de compteurs") { Owner = Application.Current.MainWindow }.ShowDialog(); }

    [RelayCommand]
    private void AddTaskLine()
    {
        if (!App.GuardWritable() || !GuardEdit()) return;
        var label = string.Empty;
        var rec = 0; var mois = 0;
        var groupe = LastGroup();
        var fields = new List<EditField>
        {
            EditField.Text("Tâche à cocher", () => label, v => label = v, required: true),
            EditField.ComboField("Récurrence de saisie", AllowedNames, () => rec, v => rec = v),
            EditField.ComboField(StartMonthLabel, MonthNames, () => mois, v => mois = v),
            EditField.Text("Section (regroupe les lignes, facultatif)", () => groupe, v => groupe = v)
        };
        if (!App.Dialogs.EditFields("Ajouter une tâche à la vue", fields)) return;
        AddLine(new ViewLine { Kind = LineKind.Tache, Libelle = label.Trim(), Recurrence = Allowed[Math.Clamp(rec, 0, Allowed.Count - 1)], MoisDebut = mois + 1, Groupe = groupe.Trim() });
    }

    private void AddLine(ViewLine line)
    {
        line.ReadingViewId = _viewId;
        line.CreatedOn = DateTime.Today;
        line.Ordre = (App.Db.ViewLines.Where(l => l.ReadingViewId == _viewId).Max(l => (int?)l.Ordre) ?? -1) + 1;
        App.Db.ViewLines.Add(line);
        App.Db.SaveChanges();
        Reload();
        _main.RefreshNotificationBadge();
    }

    internal void EditLine(LineRow row)
    {
        if (!App.GuardWritable() || !GuardEdit()) return;
        var line = App.Db.ViewLines.First(l => l.Id == row.Line.Id);
        var label = line.Libelle;
        var groupe = line.Groupe;
        var rec = Math.Max(0, Allowed.IndexOf(LineRec(line)));
        var mois = Math.Clamp(line.MoisDebut, 1, 12) - 1;
        var exclure = line.ExclureConso;
        var fields = new List<EditField>();
        if (line.Kind == LineKind.Tache) fields.Add(EditField.Text("Tâche à cocher", () => label, v => label = v, required: true));
        fields.Add(EditField.ComboField("Récurrence de saisie", AllowedNames, () => rec, v => rec = v));
        fields.Add(EditField.ComboField(StartMonthLabel, MonthNames, () => mois, v => mois = v));
        fields.Add(EditField.Text("Section (regroupe les lignes, facultatif)", () => groupe, v => groupe = v));
        if (line.Kind == LineKind.Compteur) fields.Add(EditField.BoolField("Exclure cette ligne de la consommation (mensuelle, annuelle) et des statistiques", () => exclure, v => exclure = v));
        if (line.Kind == LineKind.Compteur)
        {
            var meter = App.Db.Meters.First(m => m.Id == line.MeterId);
            if (!MeterEditor.Edit("Modifier le compteur", meter, fields)) { App.Db.Entry(meter).Reload(); return; }
        }
        else if (!App.Dialogs.EditFields("Modifier la ligne", fields)) return;
        if (line.Kind == LineKind.Tache) line.Libelle = label.Trim();
        line.Recurrence = Allowed[Math.Clamp(rec, 0, Allowed.Count - 1)];
        line.MoisDebut = mois + 1;
        if (line.Kind == LineKind.Compteur) line.ExclureConso = exclure;
        line.Groupe = groupe.Trim();
        App.Db.SaveChanges();
        Reload();
        _main.RefreshNotificationBadge();
    }

    internal void DeleteLine(LineRow row)
    {
        if (!App.GuardWritable() || !GuardEdit()) return;
        var isMeter = row.Line.Kind == LineKind.Compteur;
        if (!App.Dialogs.ShowConfirm("Supprimer la ligne",
                isMeter ? $"Supprimer le compteur « {row.Title} » ? Tous ses relevés seront supprimés définitivement (astuce : « Arrêté » dans sa fiche le garde en historique)."
                        : $"Supprimer la tâche « {row.Title} » ?"))
            return;
        var line = App.Db.ViewLines.First(l => l.Id == row.Line.Id);
        // shortcut: un compteur encore partagé par une ancienne vue n'est pas supprimé, seule la ligne l'est
        if (isMeter && line.MeterId is { } mid && !App.Db.ViewLines.Any(l => l.MeterId == mid && l.Id != line.Id)) App.Db.Meters.Remove(App.Db.Meters.First(m => m.Id == mid)); // cascade : ligne et relevés
        else App.Db.ViewLines.Remove(line);
        App.Db.SaveChanges();
        Reload();
        _main.RefreshNotificationBadge();
    }
}
