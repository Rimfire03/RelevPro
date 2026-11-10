using System.Collections.ObjectModel;
using System.Data;
using System.Globalization;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using RelevPro.Core.Models;
using RelevPro.Core.Services;
using RelevPro.Services;

namespace RelevPro.ViewModels.Releves;

/// <summary>Ligne affichée dans l'onglet « Saisie » : un compteur à relever ou une tâche à cocher, pour la période de la
/// date de saisie choisie.</summary>
public partial class LineRow : ObservableObject
{
    private readonly ReadingViewModel _owner;
    public ViewLine Line { get; }

    public string Title => Line.Titre;
    public bool IsMeter => Line.Kind == LineKind.Compteur;
    public bool IsTask => !IsMeter;
    public string KindIcon => IsMeter ? "🔢" : "☑";
    public string RecurrenceName => Periods.Name(Line.Recurrence);
    public string Unit => Line.Meter?.Unite ?? string.Empty;

    public string PeriodLabel { get; init; } = string.Empty;
    public string LastText { get; init; } = string.Empty;
    public string StatusText { get; init; } = string.Empty;
    public Brush StatusBrush { get; init; } = Brushes.Gray;

    [ObservableProperty] private string _inputText = string.Empty;
    [ObservableProperty] private bool _isDone;

    public LineRow(ReadingViewModel owner, ViewLine line) { _owner = owner; Line = line; }

    partial void OnIsDoneChanged(bool value) { if (_owner.Loaded && IsTask) _owner.ToggleTask(this, value); }

    [RelayCommand] private void Save() => _owner.SaveMeter(this);
    [RelayCommand] private void Edit() => _owner.EditLine(this);
    [RelayCommand] private void Delete() => _owner.DeleteLine(this);
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
    private static readonly Brush Ok = Freeze("#16A34A"), Late = Freeze("#DC2626"), Pending = Freeze("#D97706");

    private readonly MainViewModel _main;
    private readonly int _viewId;
    internal bool Loaded;

    public string ViewName { get; private set; } = string.Empty;
    public ObservableCollection<LineRow> Lines { get; } = new();
    public ObservableCollection<ReadingRow> Readings { get; } = new();
    public List<int> Years { get; private set; } = new();

    [ObservableProperty] private DateTime _refDate = DateTime.Today;
    [ObservableProperty] private int _selectedYear = DateTime.Today.Year;
    [ObservableProperty] private DataView? _monthlyTable;
    [ObservableProperty] private DataView? _yearlyTable;
    [ObservableProperty] private string _statusMessage = string.Empty;

    public ReadingViewModel(MainViewModel main, int viewId)
    {
        _main = main;
        _viewId = viewId;
        ViewName = App.Db.ReadingViews.AsNoTracking().FirstOrDefault(v => v.Id == viewId)?.Nom ?? "(vue supprimée)";
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

    private void ReloadLines()
    {
        Loaded = false;
        Lines.Clear();
        var today = RefDate.Date;
        var lines = App.Db.ViewLines.AsNoTracking().Include(l => l.Meter)
            .Where(l => l.ReadingViewId == _viewId).OrderBy(l => l.Ordre).ThenBy(l => l.Id).ToList();
        var meterIds = lines.Where(l => l.MeterId != null).Select(l => l.MeterId!.Value).Distinct().ToList();
        var readings = App.Db.MeterReadings.AsNoTracking().Where(r => meterIds.Contains(r.MeterId)).OrderBy(r => r.Date).ToList().ToLookup(r => r.MeterId);
        var lineIds = lines.Select(l => l.Id).ToList();
        var completions = App.Db.TaskCompletions.AsNoTracking().Where(t => lineIds.Contains(t.ViewLineId)).ToList();

        foreach (var l in lines)
        {
            var start = Periods.Start(l.Recurrence, today);
            var end = Periods.Next(l.Recurrence, start);
            var label = Periods.Label(l.Recurrence, start);
            string last = "", status;
            Brush brush;
            var done = false;
            string input = "";
            if (l.Kind == LineKind.Compteur)
            {
                var all = readings[l.MeterId ?? 0].ToList();
                var inPeriod = all.Where(r => r.Date >= start && r.Date < end).ToList();
                var lastReading = all.LastOrDefault(r => r.Date.Date <= today);
                if (lastReading is not null) last = $"{lastReading.Value.ToString("0.####", Fr)} {l.Meter?.Unite} ({lastReading.Date:dd/MM/yyyy}){(lastReading.Forced ? " ⚠" : "")}";
                var sameDay = all.FirstOrDefault(r => r.Date.Date == today);
                if (sameDay is not null) input = sameDay.Value.ToString("0.####", Fr);
                done = inPeriod.Count > 0;
                (status, brush) = StatusFor(done, l, start);
            }
            else
            {
                var c = completions.FirstOrDefault(t => t.ViewLineId == l.Id && t.PeriodStart.Date == start);
                done = c is not null;
                if (c is not null) last = $"Fait le {c.CompletedAt:dd/MM/yyyy} ({c.CompletedBy})";
                (status, brush) = StatusFor(done, l, start);
            }
            Lines.Add(new LineRow(this, l) { PeriodLabel = label, LastText = last, StatusText = status, StatusBrush = brush, InputText = input, IsDone = l.Kind == LineKind.Tache && done });
        }
        Loaded = true;
    }

    private static (string, Brush) StatusFor(bool done, ViewLine l, DateTime currentStart)
    {
        if (done) return ("Saisi", Ok);
        var late = l.CreatedOn.Date < currentStart && Periods.Start(l.Recurrence, DateTime.Today) > currentStart;
        return late ? ("En retard", Late) : ("À saisir", Pending);
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

    internal void SaveMeter(LineRow row)
    {
        if (!App.GuardWritable()) return;
        var text = (row.InputText ?? "").Trim().Replace(" ", "");
        if (!decimal.TryParse(text.Replace(',', '.'), NumberStyles.Number, CultureInfo.InvariantCulture, out var value))
        {
            App.Dialogs.ShowMessage("Valeur invalide", "Saisissez un nombre (ex. 1234,5).");
            return;
        }
        var date = RefDate.Date;
        var meter = App.Db.Meters.First(m => m.Id == row.Line.MeterId);
        var all = App.Db.MeterReadings.Where(r => r.MeterId == meter.Id).ToList();
        var existing = all.FirstOrDefault(r => r.Date.Date == date);
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

    internal void ToggleTask(LineRow row, bool done)
    {
        if (!App.GuardWritable()) { ReloadLines(); return; }
        var start = Periods.Start(row.Line.Recurrence, RefDate);
        var existing = App.Db.TaskCompletions.FirstOrDefault(t => t.ViewLineId == row.Line.Id && t.PeriodStart == start);
        if (done && existing is null)
            App.Db.TaskCompletions.Add(new TaskCompletion { ViewLineId = row.Line.Id, PeriodStart = start, CompletedAt = DateTime.Now, CompletedBy = Environment.UserName });
        else if (!done && existing is not null)
            App.Db.TaskCompletions.Remove(existing);
        App.Db.SaveChanges();
        ReloadLines();
        _main.RefreshNotificationBadge();
    }

    // ---- Lignes de la vue ----

    private static readonly List<string> RecurrenceNames = Enum.GetValues<Recurrence>().Select(Periods.Name).ToList();

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
        var rec = (int)Recurrence.Mensuel;
        var fields = new List<EditField>
        {
            EditField.ComboField("Compteur", meters.Select(m => string.IsNullOrEmpty(m.Unite) ? m.Nom : $"{m.Nom} ({m.Unite})").ToList(), () => index, v => index = v),
            EditField.ComboField("Récurrence de saisie", RecurrenceNames, () => rec, v => rec = v)
        };
        if (!App.Dialogs.EditFields("Ajouter un compteur à la vue", fields)) return;
        var meter = meters[Math.Clamp(index, 0, meters.Count - 1)];
        if (App.Db.ViewLines.Any(l => l.ReadingViewId == _viewId && l.MeterId == meter.Id))
        {
            App.Dialogs.ShowMessage("Déjà présent", $"« {meter.Nom} » figure déjà dans cette vue.");
            return;
        }
        AddLine(new ViewLine { Kind = LineKind.Compteur, MeterId = meter.Id, Recurrence = (Recurrence)rec });
    }

    [RelayCommand]
    private void AddTaskLine()
    {
        if (!App.GuardWritable()) return;
        var label = string.Empty;
        var rec = (int)Recurrence.Mensuel;
        var fields = new List<EditField>
        {
            EditField.Text("Tâche à cocher", () => label, v => label = v, required: true),
            EditField.ComboField("Récurrence de saisie", RecurrenceNames, () => rec, v => rec = v)
        };
        if (!App.Dialogs.EditFields("Ajouter une tâche à la vue", fields)) return;
        AddLine(new ViewLine { Kind = LineKind.Tache, Libelle = label.Trim(), Recurrence = (Recurrence)rec });
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
        var rec = (int)line.Recurrence;
        var fields = new List<EditField>();
        if (line.Kind == LineKind.Tache) fields.Add(EditField.Text("Tâche à cocher", () => label, v => label = v, required: true));
        fields.Add(EditField.ComboField("Récurrence de saisie", RecurrenceNames, () => rec, v => rec = v));
        if (!App.Dialogs.EditFields("Modifier la ligne", fields)) return;
        if (line.Kind == LineKind.Tache) line.Libelle = label.Trim();
        line.Recurrence = (Recurrence)rec;
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

    [RelayCommand] private void Today() => RefDate = DateTime.Today;

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
