using RelevPro.Core.Data;
using RelevPro.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace RelevPro.Core.Services;

/// <summary>Consommations par mois et par an des compteurs d'une vue : dernier index de la période moins le dernier index
/// précédant la période (à défaut, premier relevé de la période s'il y en a au moins deux).</summary>
public static class ConsumptionService
{
    public static decimal? Consumption(IReadOnlyList<MeterReading> ordered, DateTime start, DateTime endExclusive)
    {
        var inside = ordered.Where(r => r.Date >= start && r.Date < endExclusive).ToList();
        if (inside.Count == 0) return null;
        var last = inside[^1];
        var before = ordered.LastOrDefault(r => r.Date < start);
        var baseValue = before?.Value ?? (inside.Count > 1 ? inside[0].Value : (decimal?)null);
        return baseValue is null ? null : last.Value - baseValue.Value;
    }

    public sealed record Row(string Compteur, string Unite, decimal?[] Values, decimal? Total);

    /// <summary>Une ligne par compteur de la vue : les 12 mois de <paramref name="year"/>.</summary>
    public static List<Row> Monthly(RelevDbContext db, int viewId, int year) =>
        Build(db, viewId, Enumerable.Range(1, 12).Select(m => (new DateTime(year, m, 1), new DateTime(year, m, 1).AddMonths(1))).ToList(), total: true);

    public static List<Row> Yearly(RelevDbContext db, int viewId, IReadOnlyList<int> years) =>
        Build(db, viewId, years.Select(y => (new DateTime(y, 1, 1), new DateTime(y + 1, 1, 1))).ToList(), total: false);

    public static List<int> YearsWithData(RelevDbContext db, int viewId)
    {
        var ids = MeterIds(db, viewId);
        var years = db.MeterReadings.AsNoTracking().Where(r => ids.Contains(r.MeterId)).Select(r => r.Date.Year).Distinct().OrderBy(y => y).ToList();
        if (!years.Contains(DateTime.Today.Year)) years.Add(DateTime.Today.Year);
        return years;
    }

    private static List<int> MeterIds(RelevDbContext db, int viewId) =>
        db.ViewLines.AsNoTracking().Where(l => l.ReadingViewId == viewId && l.MeterId != null).Select(l => l.MeterId!.Value).Distinct().ToList();

    private static List<Row> Build(RelevDbContext db, int viewId, List<(DateTime Start, DateTime End)> periods, bool total)
    {
        var ids = MeterIds(db, viewId);
        var meters = db.Meters.AsNoTracking().Where(m => ids.Contains(m.Id)).OrderBy(m => m.Ordre).ThenBy(m => m.Nom).ToList();
        var readings = db.MeterReadings.AsNoTracking().Where(r => ids.Contains(r.MeterId)).OrderBy(r => r.Date).ToList().ToLookup(r => r.MeterId);
        return meters.Select(m =>
        {
            var ordered = readings[m.Id].ToList();
            var values = periods.Select(p => Consumption(ordered, p.Start, p.End)).ToArray();
            decimal? sum = total && values.Any(v => v is not null) ? values.Sum(v => v ?? 0) : null;
            return new Row(m.Nom, m.Unite, values, sum);
        }).ToList();
    }
}
