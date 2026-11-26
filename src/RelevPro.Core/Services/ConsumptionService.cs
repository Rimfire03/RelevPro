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
        db.ViewLines.AsNoTracking().Where(l => l.ReadingViewId == viewId && l.MeterId != null && !l.ExclureConso).Select(l => l.MeterId!.Value).Distinct().ToList();

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

    /// <summary>Données d'un graphique : une carte par ligne de la vue. <see cref="Months"/> / <see cref="PrevMonths"/> : consommation
    /// de chaque mois (compteur cumulatif, année choisie et année précédente) ; <see cref="Points"/> : relevés de l'année (mesure) ;
    /// <see cref="Done"/> / <see cref="Expected"/> : périodes réalisées / échues (tâche).</summary>
    public sealed record ChartCard(string Title, string Kind, string Unit, decimal?[] Months, decimal?[] PrevMonths, decimal? Total, decimal? PrevTotal,
        List<(DateTime Date, decimal Value)> Points, int Done, int Expected);

    public static List<ChartCard> Charts(RelevDbContext db, int viewId, int year, DateTime today)
    {
        var view = db.ReadingViews.AsNoTracking().FirstOrDefault(v => v.Id == viewId);
        var viewRec = view?.Recurrence ?? Recurrence.Hebdo;
        var lines = db.ViewLines.AsNoTracking().Include(l => l.Meter).Where(l => l.ReadingViewId == viewId).OrderBy(l => l.Ordre).ThenBy(l => l.Id).ToList();
        var ids = lines.Where(l => l.MeterId != null).Select(l => l.MeterId!.Value).Distinct().ToList();
        var readings = db.MeterReadings.AsNoTracking().Where(r => ids.Contains(r.MeterId)).OrderBy(r => r.Date).ThenBy(r => r.Id).ToList().ToLookup(r => r.MeterId);
        var lineIds = lines.Select(l => l.Id).ToList();
        var done = db.TaskCompletions.AsNoTracking().Where(t => lineIds.Contains(t.ViewLineId)).Select(t => new { t.ViewLineId, t.PeriodStart }).ToList()
            .Select(t => (t.ViewLineId, t.PeriodStart.Date)).ToHashSet();

        var yStart = new DateTime(year, 1, 1);
        var yEnd = yStart.AddYears(1);
        var curEnd = year == today.Year ? today.Date.AddDays(1) : yEnd; // année en cours : jusqu'à aujourd'hui
        var cards = new List<ChartCard>();
        foreach (var l in lines)
        {
            if (l.Meter?.Arrete == true || (l.ExclureConso && l.Kind != LineKind.Tache)) continue;
            if (l.Kind == LineKind.Tache)
            {
                var r = l.Recurrence < viewRec ? viewRec : l.Recurrence;
                var mp = l.MoisPeriode(viewRec);
                int expected = 0, ok = 0;
                for (var p0 = Periods.Start(r, new[] { l.CreatedOn.Date, yStart }.Max(), mp); p0 < yEnd; p0 = Periods.Next(r, p0))
                {
                    if (p0 < yStart) continue;
                    if (Periods.Next(r, p0) > today.Date) break; // période pas encore échue
                    expected++;
                    if (done.Contains((l.Id, p0))) ok++;
                }
                cards.Add(new ChartCard(l.Titre, "Tache", "", new decimal?[12], new decimal?[12], null, null, new(), ok, expected));
                continue;
            }
            var m = l.Meter!;
            var all = readings[m.Id].ToList();
            if (!m.Cumulatif)
            {
                var pts = all.Where(x => x.Date >= yStart && x.Date < yEnd).Select(x => (x.Date, x.Value)).ToList();
                cards.Add(new ChartCard(l.Titre, "Mesure", m.Unite, new decimal?[12], new decimal?[12], null, null, pts, 0, 0));
                continue;
            }
            var cap = Capacity(m, all);
            decimal?[] Months(int y) => Enumerable.Range(1, 12).Select(i => Consumption(all, new DateTime(y, i, 1), new DateTime(y, i, 1).AddMonths(1), cap)).ToArray();
            var cur = Months(year); var prev = Months(year - 1);
            var total = Consumption(all, yStart, curEnd, cap);
            var prevTotal = Consumption(all, yStart.AddYears(-1), curEnd.AddYears(-1), cap); // même période l'an passé
            cards.Add(new ChartCard(l.Titre, "Compteur", m.Unite, cur, prev, total, prevTotal, new(), 0, 0));
        }
        return cards;
    }
}
