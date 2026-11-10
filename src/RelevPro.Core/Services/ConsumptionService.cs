using RelevPro.Core.Data;
using RelevPro.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace RelevPro.Core.Services;

/// <summary>Consommations par mois et par an des compteurs cumulatifs d'une vue (voir <see cref="Consumption"/>).</summary>
public static class ConsumptionService
{
    /// <summary>Capacité du compteur (valeur avant retour à 0) : celle du compteur si elle est renseignée, sinon la puissance
    /// de 10 juste au-dessus du plus grand relevé (99 994 donne 100 000).</summary>
    public static decimal Capacity(Meter? meter, IReadOnlyList<MeterReading> readings)
    {
        if (meter is { Capacite: > 0 }) return meter.Capacite;
        if (readings.Count == 0) return 0;
        var max = readings.Max(r => r.Value);
        return max <= 0 ? 0 : (decimal)Math.Pow(10, Math.Floor(Math.Log10((double)max)) + 1);
    }

    /// <summary>Consommation d'une période : du premier relevé de la période jusqu'au premier relevé de la période suivante
    /// (règle des relevés papier : on relève en début de période ; période en cours : jusqu'au dernier relevé). Un relevé « avec tour » compte (capacité - ancienne valeur + nouvelle valeur),
    /// un relevé « compteur changé » compte sa valeur (le nouveau compteur part de 0) ; toute autre baisse d'index compte 0, jamais une consommation négative.</summary>
    public static decimal? Consumption(IReadOnlyList<MeterReading> ordered, DateTime start, DateTime endExclusive, decimal capacity = 0)
    {
        var i0 = -1;
        for (var i = 0; i < ordered.Count; i++) if (ordered[i].Date >= start && ordered[i].Date < endExclusive) { i0 = i; break; }
        if (i0 < 0) return null;
        var iEnd = i0;
        for (var i = i0 + 1; i < ordered.Count; i++)
        {
            iEnd = i;
            if (ordered[i].Date >= endExclusive) break;
        }
        if (iEnd == i0) return null;
        // Entre deux changements (tour, compteur changé), consommation = dernier - premier de chaque tronçon : une valeur
        // isolée aberrante au milieu n'a aucun effet, et un tronçon ne peut pas être négatif.
        decimal total = 0, segStart = ordered[i0].Value;
        for (var i = i0 + 1; i <= iEnd; i++)
        {
            var r = ordered[i];
            if (r.Tour && capacity > 0)
            {
                total += Math.Max(0, ordered[i - 1].Value - segStart) + (capacity - ordered[i - 1].Value + r.Value);
                segStart = r.Value;
            }
            else if (r.Remplacement)
            {
                total += Math.Max(0, ordered[i - 1].Value - segStart) + r.Value; // le nouveau compteur part de 0
                segStart = r.Value;
            }
        }
        total += Math.Max(0, ordered[iEnd].Value - segStart);
        return total;
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
        var meters = db.Meters.AsNoTracking().Where(m => ids.Contains(m.Id) && m.Cumulatif).OrderBy(m => m.Ordre).ThenBy(m => m.Nom).ToList();
        var readings = db.MeterReadings.AsNoTracking().Where(r => ids.Contains(r.MeterId)).OrderBy(r => r.Date).ToList().ToLookup(r => r.MeterId);
        return meters.Select(m =>
        {
            var ordered = readings[m.Id].ToList();
            var cap = Capacity(m, ordered);
            var values = periods.Select(p => Consumption(ordered, p.Start, p.End, cap)).ToArray();
            decimal? sum = total && values.Any(v => v is not null) ? values.Sum(v => v ?? 0) : null;
            return new Row(m.Nom, m.Unite, values, sum);
        }).ToList();
    }
}
