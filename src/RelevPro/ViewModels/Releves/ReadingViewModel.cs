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

    [ObservableProperty] private string _text = string.Empty;
    [ObservableProperty] private bool _isDone;

    public GridCell(LineRow row, Periods.Column column, DateTime periodStart, bool active, bool editable, int? readingId)
    {
        _row = row; Column = column; PeriodStart = periodStart; Active = active; Editable = editable; ReadingId = readingId;
    }

    public Visibility BoxVisibility => Editable && _row.IsMeter ? Visibility.Visible : Visibility.Collapsed;
    public Visibility CheckVisibility => Editable && _row.IsTask ? Visibility.Visible : Visibility.Collapsed;
    public Visibility BlankVisibility => Editable ? Visibility.Collapsed : Visibility.Visible;
    public string Blank => Active ? string.Empty : "—";

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
    public string Title => Line.Titre;
    public bool IsMeter => Line.Kind == LineKind.Compteur;
    public bool IsTask => !IsMeter;
    public string KindIcon => IsMeter ? "🔢" : "☑";
    public string RecurrenceName => Periods.Name(Line.Recurrence);
    public string Unit => Line.Meter?.Unite ?? string.Empty;

    public string ConsumptionText { get; init; } = string.Empty;
    public string StatusText { get; init; } = string.Empty;
    public Brush StatusBrush { get; init; } = Brushes.Gray;

    [RelayCommand] private void Edit() => Owner.EditLine(this);
    [RelayCommand] private void Delete() => Owner.DeleteLine(this);
}

public sealed record ReadingRow(MeterReading Reading, string Meter, string Unit)
{
    public string DateText => Reading.Date.ToString("dd/MM/yyyy");
    public string ValueText => Reading.Value.ToString("0.####", CultureInfo.GetCultureInfo("fr-FR"));
    public string ForcedText => Reading.Forced ? "Forcé" : "";
    public string Anomaly => Reading.Anomaly;
    public string By => Reading.EnteredBy;
}

public partial class ReadingViewModel : ObservableObject
{
    private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");
    private static readonly Brush Ok = Freeze("#16A34A"), Late = Freeze("#DC2626"), Pending = Freeze("#D97706"), None = Freeze("#94A3B8");

    private readonly MainViewModel _main;
    private readonly int _viewId;
    private Recurrence _rec = Recurrence.Hebdo;
    internal bool Loaded;

    public string ViewName { get; private set; } = string.Empty;
    public string ViewSubtitle { get; private set; } = string.Empty;
    public ObservableCollection<LineRow> Lines { get; } = new();
    public ObservableCollection<ReadingRow> Readings { get; } = new();
    public List<int> Years { get; private set; } = new();

    /// <summary>Colonnes de la grille (une par période de la vue) ; la vue XAML recrée ses colonnes sur <see cref="ColumnsChanged"/>.</summary>
    public List<string> ColumnHeaders { get; private set; } = new();
    public event Action? ColumnsChanged;

    [ObservableProperty] private DateTime _refDate = DateTime.Today;
    [ObservableProperty] private int _selectedYear = DateTime.Today.Year;
    [ObservableProperty] private DataView? _monthlyTable;
    [ObservableProperty] private DataView? _yearlyTable;
    [ObservableProperty] private string _statusMessage = string.Empty;
    [ObservableProperty] private string _periodTitle = string.Empty;
    [ObservableProperty] private string _consumptionHeader = "Consommation";

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
    partial void OnSelectedYearChanged(int value) => ReloadMonthly();

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
        ReloadReadings();
    }

    private Recurrence LineRec(ViewLine l) => l.Recurrence < _rec ? _rec : l.Recurrence;

    private void ReloadLines()
    {
        Loaded = false;
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
        var lineIds = lines.Select(l => l.Id).ToList();
        var completions = App.Db.TaskCompletions.AsNoTracking().Where(t => lineIds.Contains(t.ViewLineId)).ToList();

        foreach (var l in lines)
        {
            var r = LineRec(l);
            var all = l.Kind == LineKind.Compteur ? readings[l.MeterId ?? 0].ToList() : new List<MeterReading>();
            var cells = new List<(Periods.Column Col, DateTime Lp, DateTime LpEnd, bool Active, bool Editable, MeterReading? Existing, bool Done)>();
            foreach (var c in cols)
            {
                var active = Periods.IsActive(_rec, r, c.Start);
                var lp = Periods.Start(r, c.Start);
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
                cells.Add((c, lp, lpEnd, active, active && c.Start <= today, existing, done));
            }

            // statut de la ligne : en retard si une case échue (période de la ligne terminée) est vide
            var due = cells.Where(x => x.Active && x.Col.Start <= today).ToList();
            string status; Brush brush;
            if (due.Count == 0) (status, brush) = ("—", None);
            else
            {
                var missing = due.Where(x => l.Kind == LineKind.Compteur ? x.Existing is null : !x.Done).ToList();
                if (missing.Count == 0) (status, brush) = ("Saisi", Ok);
                else if (missing.Any(x => (r == _rec ? x.Col.End : x.LpEnd) <= today && l.CreatedOn.Date < x.Col.End)) (status, brush) = ("En retard", Late);
                else (status, brush) = ("À saisir", Pending);
            }

            var cons = string.Empty;
            if (l.Kind == LineKind.Compteur && l.Meter is { Cumulatif: true } m && ConsumptionService.Consumption(all, wStart, wEnd) is { } v)
                cons = $"{v.ToString("#,0.##", Fr)} {m.Unite}".Trim();

            var row = new LineRow(this, l) { ConsumptionText = cons, StatusText = status, StatusBrush = brush };
            foreach (var x in cells)
            {
                var cell = new GridCell(row, x.Col, x.Lp, x.Active, x.Editable, x.Existing?.Id)
                {
                    Text = x.Existing is null ? string.Empty : x.Existing.Value.ToString("0.####", Fr),
                    IsDone = x.Done
                };
                row.Cells.Add(cell);
            }
            Lines.Add(row);
        }
        Loaded = true;
    }

    private void ReloadMonthly()
    {
        var rows = ConsumptionService.Monthly(App.Db, _viewId, SelectedYear);
        var cols = new List<string> { "Compteur", "Unité" };
        cols.AddRange(Enumerable.Range(1, 12).Select(m => Fr.DateTimeFormat.GetAbbreviatedMonthName(m)));
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

    private static DataView Table(List<string> cols, IEnumerable<object?[]> rows)
    {
        var t = new DataTable();
        foreach (var c in cols) t.Columns.Add(c, typeof(string));
        foreach (var r in rows)
            t.Rows.Add(r.Select(v => v switch { null => "", decimal d => d.ToString("#,0.##", Fr), _ => v.ToString() }).Cast<object>().ToArray());
        return t.DefaultView;
    }

    private void ReloadReadings()
    {
        Readings.Clear();
        var ids = App.Db.ViewLines.AsNoTracking().Where(l => l.ReadingViewId == _viewId && l.MeterId != null).Select(l => l.MeterId!.Value).Distinct().ToList();
        foreach (var r in App.Db.MeterReadings.AsNoTracking().Include(r => r.Meter).Where(r => ids.Contains(r.MeterId))
                     .OrderByDescending(r => r.Date).ThenByDescending(r => r.Id).Take(300).ToList())
            Readings.Add(new ReadingRow(r, r.Meter?.Nom ?? "", r.Meter?.Unite ?? ""));
    }

    // ---- Saisie ----

    internal void SaveCell(GridCell cell)
    {
        if (!App.GuardWritable()) { ReloadLines(); return; }
        var text = (cell.Text ?? "").Trim().Replace(" ", "");
        if (text.Length == 0) { ReloadLines(); return; } // effacer une case ne supprime pas le relevé (voir l'onglet Relevés)
        if (!decimal.TryParse(text.Replace(',', '.'), NumberStyles.Number, CultureInfo.InvariantCulture, out var value))
        {
            App.Dialogs.ShowMessage("Valeur invalide", "Saisissez un nombre (ex. 1234,5).");
            ReloadLines();
            return;
        }
        var meter = App.Db.Meters.First(m => m.Id == cell.Row.Line.MeterId);
        var all = App.Db.MeterReadings.Where(r => r.MeterId == meter.Id).ToList();
        var existing = cell.ReadingId is { } id ? all.FirstOrDefault(r => r.Id == id) : null;
        if (existing is not null && existing.Value == value) { ReloadLines(); return; }
        // un relevé en retard est daté de la période de la case (jamais dans le futur)
        var date = existing?.Date.Date ?? Max(cell.Column.Start, Min(DateTime.Today, cell.Column.End.AddDays(-1)));
        var anomalies = ReadingValidator.Check(meter, date, value, all.Where(r => r != existing));

        var forced = false;
        if (anomalies.Count > 0)
        {
            if (!App.Dialogs.ShowConfirm($"Anomalie de saisie - {meter.Nom}",
                    string.Join("\n", anomalies) + "\n\nForcer cette valeur ? (Non = corriger la saisie)"))
            {
                ReloadLines();
                return;
            }
            forced = true;
        }

        if (existing is null)
        {
            existing = new MeterReading { MeterId = meter.Id, Date = date };
            App.Db.MeterReadings.Add(existing);
        }
        existing.Value = value;
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

    [RelayCommand] private void Today() => RefDate = DateTime.Today;
    [RelayCommand] private void PreviousPeriod() { var w = Periods.Window(_rec); RefDate = Periods.Previous(w, Periods.Start(w, RefDate)); }
    [RelayCommand] private void NextPeriod() { var w = Periods.Window(_rec); RefDate = Periods.Next(w, Periods.Start(w, RefDate)); }

    // ---- Lignes de la vue ----

    /// <summary>Récurrences permises : celle de la vue ou plus lentes (une ligne n'est jamais plus rapide que sa vue).</summary>
    private List<Recurrence> Allowed => Enum.GetValues<Recurrence>().Where(r => r >= _rec).ToList();
    private List<string> AllowedNames => Allowed.Select(Periods.Name).ToList();

    private string LastGroup() => App.Db.ViewLines.Where(l => l.ReadingViewId == _viewId).OrderByDescending(l => l.Ordre).Select(l => l.Groupe).FirstOrDefault() ?? string.Empty;

    [RelayCommand]
    private void AddMeterLine()
    {
        if (!App.GuardWritable()) return;
        var meters = App.Db.Meters.AsNoTracking().OrderBy(m => m.Ordre).ThenBy(m => m.Nom).ToList();
        if (meters.Count == 0)
        {
            App.Dialogs.ShowMessage("Aucun compteur", "Créez d'abord un compteur dans le menu « Compteurs ».");
            return;
        }
        var index = 0;
        var rec = 0;
        var groupe = LastGroup();
        var fields = new List<EditField>
        {
            EditField.ComboField("Compteur", meters.Select(m => string.IsNullOrEmpty(m.Unite) ? m.Nom : $"{m.Nom} ({m.Unite})").ToList(), () => index, v => index = v),
            EditField.ComboField("Récurrence de saisie", AllowedNames, () => rec, v => rec = v),
            EditField.Text("Section (regroupe les lignes, facultatif)", () => groupe, v => groupe = v)
        };
        if (!App.Dialogs.EditFields("Ajouter un compteur à la vue", fields)) return;
        var meter = meters[Math.Clamp(index, 0, meters.Count - 1)];
        if (App.Db.ViewLines.Any(l => l.ReadingViewId == _viewId && l.MeterId == meter.Id))
        {
            App.Dialogs.ShowMessage("Déjà présent", $"« {meter.Nom} » figure déjà dans cette vue.");
            return;
        }
        AddLine(new ViewLine { Kind = LineKind.Compteur, MeterId = meter.Id, Recurrence = Allowed[Math.Clamp(rec, 0, Allowed.Count - 1)], Groupe = groupe.Trim() });
    }

    [RelayCommand]
    private void AddTaskLine()
    {
        if (!App.GuardWritable()) return;
        var label = string.Empty;
        var rec = 0;
        var groupe = LastGroup();
        var fields = new List<EditField>
        {
            EditField.Text("Tâche à cocher", () => label, v => label = v, required: true),
            EditField.ComboField("Récurrence de saisie", AllowedNames, () => rec, v => rec = v),
            EditField.Text("Section (regroupe les lignes, facultatif)", () => groupe, v => groupe = v)
        };
        if (!App.Dialogs.EditFields("Ajouter une tâche à la vue", fields)) return;
        AddLine(new ViewLine { Kind = LineKind.Tache, Libelle = label.Trim(), Recurrence = Allowed[Math.Clamp(rec, 0, Allowed.Count - 1)], Groupe = groupe.Trim() });
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
        if (!App.GuardWritable()) return;
        var line = App.Db.ViewLines.First(l => l.Id == row.Line.Id);
        var label = line.Libelle;
        var groupe = line.Groupe;
        var rec = Math.Max(0, Allowed.IndexOf(LineRec(line)));
        var fields = new List<EditField>();
        if (line.Kind == LineKind.Tache) fields.Add(EditField.Text("Tâche à cocher", () => label, v => label = v, required: true));
        fields.Add(EditField.ComboField("Récurrence de saisie", AllowedNames, () => rec, v => rec = v));
        fields.Add(EditField.Text("Section (regroupe les lignes, facultatif)", () => groupe, v => groupe = v));
        if (!App.Dialogs.EditFields("Modifier la ligne", fields)) return;
        if (line.Kind == LineKind.Tache) line.Libelle = label.Trim();
        line.Recurrence = Allowed[Math.Clamp(rec, 0, Allowed.Count - 1)];
        line.Groupe = groupe.Trim();
        App.Db.SaveChanges();
        Reload();
        _main.RefreshNotificationBadge();
    }

    internal void DeleteLine(LineRow row)
    {
        if (!App.GuardWritable()) return;
        if (!App.Dialogs.ShowConfirm("Retirer la ligne",
                $"Retirer « {row.Title} » de cette vue ? Les relevés du compteur sont conservés (et restent visibles dans les autres vues)."))
            return;
        var line = App.Db.ViewLines.First(l => l.Id == row.Line.Id);
        App.Db.ViewLines.Remove(line);
        App.Db.SaveChanges();
        Reload();
        _main.RefreshNotificationBadge();
    }

    [RelayCommand]
    private void DeleteReading(ReadingRow row)
    {
        if (!App.GuardWritable()) return;
        if (!App.Dialogs.ShowConfirm("Supprimer le relevé",
                $"Supprimer le relevé de « {row.Meter} » du {row.DateText} ({row.ValueText} {row.Unit}) ? Il disparaît aussi des autres vues."))
            return;
        var r = App.Db.MeterReadings.First(x => x.Id == row.Reading.Id);
        App.Db.MeterReadings.Remove(r);
        App.Db.SaveChanges();
        Reload();
        _main.RefreshNotificationBadge();
    }
}
