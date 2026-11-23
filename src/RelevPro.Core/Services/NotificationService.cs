using RelevPro.Core.Data;
using RelevPro.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace RelevPro.Core.Services;

public enum NotificationLevel { Late = 0, Anomaly = 1, Pending = 2 }

public sealed record AppNotification(NotificationLevel Level, string ViewName, string Title, string Detail, int ViewId);

/// <summary>Centre de notifications : saisies en retard (période précédente non faite), saisies à faire sur la période en
/// cours, et relevés forcés malgré une alerte au cours des 30 derniers jours.</summary>
public static class NotificationService
{
    public const int AnomalyDays = 30;

    /// <summary>La ligne a-t-elle une saisie pour la période commençant à <paramref name="start"/> ?</summary>
    public static bool IsDone(ViewLine line, DateTime start, ILookup<int, DateTime> readingDates, ISet<(int, DateTime)> completions)
    {
        if (line.Kind == LineKind.Tache) return completions.Contains((line.Id, start));
        if (line.MeterId is not { } m) return true;
        var end = Periods.Next(line.Recurrence, start);
        return readingDates[m].Any(d => d >= start && d < end);
    }

    public static List<AppNotification> Build(RelevDbContext db, DateTime today)
    {
        today = today.Date;
        var lines = db.ViewLines.AsNoTracking().Include(l => l.Meter).Include(l => l.ReadingView).ToList();
        var readingDates = db.MeterReadings.AsNoTracking().Select(r => new { r.MeterId, r.Date }).ToList().ToLookup(r => r.MeterId, r => r.Date);
        var completions = db.TaskCompletions.AsNoTracking().Select(t => new { t.ViewLineId, t.PeriodStart }).ToList()
            .Select(t => (t.ViewLineId, t.PeriodStart.Date)).ToHashSet();

        // une seule notification par vue, récurrence et période (et non une par compteur manquant)
        var late = new Dictionary<(int View, Recurrence Rec, DateTime Start), List<ViewLine>>();
        var pending = new Dictionary<(int View, Recurrence Rec, DateTime Start), List<ViewLine>>();
        void Group(Dictionary<(int, Recurrence, DateTime), List<ViewLine>> d, ViewLine l, DateTime start)
        {
            var key = (l.ReadingViewId, l.Recurrence, start);
            if (!d.TryGetValue(key, out var g)) d[key] = g = new List<ViewLine>();
            g.Add(l);
        }
        foreach (var line in lines.Where(l => l.ReadingView is not null && !(l.Meter?.Arrete ?? false)).OrderBy(l => l.ReadingView!.Ordre).ThenBy(l => l.Ordre))
        {
            var current = Periods.Start(line.Recurrence, today);
            var previous = Periods.Previous(line.Recurrence, current);
            if (line.CreatedOn.Date < current && !IsDone(line, previous, readingDates, completions)) Group(late, line, previous);
            if (!IsDone(line, current, readingDates, completions)) Group(pending, line, current);
        }

        var list = new List<AppNotification>();
        void Emit(Dictionary<(int View, Recurrence Rec, DateTime Start), List<ViewLine>> d, NotificationLevel level, string text)
        {
            foreach (var ((view, rec, start), g) in d)
            {
                var allTasks = g.All(l => l.Kind == LineKind.Tache);
                var noun = allTasks ? "Tâches" : g.All(l => l.Kind == LineKind.Compteur) ? "Relevé" : "Saisie";
                var names = string.Join(", ", g.Take(4).Select(l => l.Titre)) + (g.Count > 4 ? $" (+{g.Count - 4})" : "");
                list.Add(new AppNotification(level, g[0].ReadingView!.Nom, $"{noun} {text} : {g[0].ReadingView!.Nom} ({g.Count})",
                    $"{Periods.Name(rec)} - {Periods.Label(rec, start)} : {names}", view));
            }
        }
        Emit(late, NotificationLevel.Late, "en retard");
        Emit(pending, NotificationLevel.Pending, "à faire");

        var since = today.AddDays(-AnomalyDays);
        var forced = db.MeterReadings.AsNoTracking().Include(r => r.Meter)
            .Where(r => r.Forced && r.Date >= since).OrderByDescending(r => r.Date).ToList();
        foreach (var r in forced)
            list.Add(new AppNotification(NotificationLevel.Anomaly, "", $"Valeur forcée : {r.Meter?.Nom} = {r.Value:0.##} {r.Meter?.Unite}",
                $"{r.Date:dd/MM/yyyy} - {r.Anomaly}", 0));

        return list.OrderBy(n => n.Level).ToList();
    }

    public static int LateCount(RelevDbContext db, DateTime today) => Build(db, today).Count(n => n.Level == NotificationLevel.Late);
}
