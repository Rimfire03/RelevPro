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

        var list = new List<AppNotification>();
        foreach (var line in lines.Where(l => l.ReadingView is not null).OrderBy(l => l.ReadingView!.Ordre).ThenBy(l => l.Ordre))
        {
            var current = Periods.Start(line.Recurrence, today);
            var previous = Periods.Previous(line.Recurrence, current);
            var viewName = line.ReadingView!.Nom;
            var kind = line.Kind == LineKind.Compteur ? "Relevé" : "Tâche";

            if (line.CreatedOn.Date < current && !IsDone(line, previous, readingDates, completions))
                list.Add(new AppNotification(NotificationLevel.Late, viewName, $"{kind} en retard : {line.Titre}",
                    $"{Periods.Name(line.Recurrence)} - {Periods.Label(line.Recurrence, previous)}", line.ReadingViewId));
            if (!IsDone(line, current, readingDates, completions))
                list.Add(new AppNotification(NotificationLevel.Pending, viewName, $"{kind} à faire : {line.Titre}",
                    $"{Periods.Name(line.Recurrence)} - {Periods.Label(line.Recurrence, current)}", line.ReadingViewId));
        }

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
