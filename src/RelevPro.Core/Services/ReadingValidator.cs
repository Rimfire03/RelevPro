using Microsoft.EntityFrameworkCore;
using RelevPro.Core.Data;
using RelevPro.Core.Models;

namespace RelevPro.Core.Services;

/// <summary>Contrôle d'une valeur saisie : un compteur ne peut qu'augmenter, et la consommation journalière ne doit
/// pas dépasser de trop la moyenne habituelle (seuil du compteur).</summary>
public static class ReadingValidator
{
    /// <summary>Nombre d'intervalles précédents utilisés pour la moyenne.</summary>
    public const int HistorySize = 12;

    /// <summary>Recontrôle les relevés forcés (valeurs aberrantes) : ceux qui ne présentent plus d'anomalie (valeur voisine corrigée,
    /// seuil modifié...) redeviennent normaux ; les autres voient leur message mis à jour. Retourne le nombre de relevés modifiés.</summary>
    public static int RecheckForced(RelevDbContext db, IReadOnlyCollection<int>? meterIds = null)
    {
        var forced = db.MeterReadings.Where(r => r.Forced && (meterIds == null || meterIds.Contains(r.MeterId))).ToList();
        if (forced.Count == 0) return 0;
        var ids = forced.Select(r => r.MeterId).Distinct().ToList();
        var meters = db.Meters.Where(m => ids.Contains(m.Id)).ToDictionary(m => m.Id);
        var all = db.MeterReadings.AsNoTracking().Where(r => ids.Contains(r.MeterId)).ToList().ToLookup(r => r.MeterId);
        var changed = 0;
        foreach (var r in forced)
        {
            if (!meters.TryGetValue(r.MeterId, out var m)) continue;
            var others = all[r.MeterId].Where(x => x.Id != r.Id).ToList();
            var cap = r.Tour ? ConsumptionService.Capacity(m, all[r.MeterId].ToList()) : 0;
            var anomalies = Check(m, r.Date, r.Value, others, cap, r.Remplacement);
            if (anomalies.Count == 0) { r.Forced = false; r.Anomaly = string.Empty; changed++; }
            else { var text = string.Join(" ", anomalies); if (r.Anomaly != text) { r.Anomaly = text; changed++; } }
        }
        if (changed > 0) db.SaveChanges();
        return changed;
    }

    /// <summary>Retourne les anomalies (vide = valeur acceptée). <paramref name="others"/> : relevés existants du
    /// compteur, hors celui qui est modifié.</summary>
    public static List<string> Check(Meter meter, DateTime date, decimal value, IEnumerable<MeterReading> others, decimal rolloverCapacity = 0, bool replaced = false)
    {
        // relevé avec tour confirmé : comparé au relevé précédent comme si le compteur avait continué (valeur + capacité)
        var v = rolloverCapacity > 0 ? value + rolloverCapacity : value;
        var list = others.OrderBy(r => r.Date).ToList();
        var anomalies = new List<string>();
        var prev = replaced ? null : list.LastOrDefault(r => r.Date < date.Date); // compteur changé : plus de comparaison avec l'ancien
        var next = list.FirstOrDefault(r => r.Date > date.Date);

        if (!meter.Cumulatif)
        {
            if (meter.SeuilHaut > 0 && value > meter.SeuilHaut)
                anomalies.Add($"Valeur au-dessus du seuil d'alarme : {Fmt(value)} > {Fmt(meter.SeuilHaut)} {meter.Unite}.");
            return anomalies;
        }

        // rythme habituel : plus haut de la moyenne et du rythme maximal déjà observé, avec la marge du compteur
        decimal? limit = null;
        if (prev is not null && v >= prev.Value)
        {
            var history = list.Where(r => r.Date <= prev.Date).ToList();
            if (AverageDailyRate(history) is > 0 and var a) limit = Math.Max(a, MaxDailyRate(history)) * (1 + meter.SeuilEcartPct / 100m);
        }
        var daysSincePrev = prev is null ? 1 : Math.Max(1, (date.Date - prev.Date).Days);
        // seuil d'alarme fixe : ne sonne que si l'augmentation dépasse aussi le rythme habituel (quand l'historique permet de le calculer)
        if (prev is not null && meter.SeuilHaut > 0 && v - prev.Value > meter.SeuilHaut && (limit is null || (v - prev.Value) / daysSincePrev > limit))
            anomalies.Add($"Augmentation au-dessus du seuil d'alarme : {Fmt(v - prev.Value)} > {Fmt(meter.SeuilHaut)} {meter.Unite} depuis le {prev.Date:dd/MM/yyyy}.");
        if (prev is not null && v < prev.Value)
            anomalies.Add($"Le compteur diminue : {Fmt(value)} < {Fmt(prev.Value)} (relevé du {prev.Date:dd/MM/yyyy}).");
        if (next is not null && value > next.Value)
            anomalies.Add($"Valeur supérieure au relevé suivant : {Fmt(value)} > {Fmt(next.Value)} (relevé du {next.Date:dd/MM/yyyy}).");

        if (prev is not null && limit is { } lim && (v - prev.Value) / daysSincePrev > lim)
        {
            var avg = AverageDailyRate(list.Where(r => r.Date <= prev.Date).ToList())!.Value;
            anomalies.Add($"Écart trop grand : {Fmt(v - prev.Value)} {meter.Unite} en {daysSincePrev} j " +
                          $"(habituel : {Fmt(avg * daysSincePrev)} {meter.Unite}, seuil +{meter.SeuilEcartPct} %).");
        }
        return anomalies;
    }

    private static decimal MaxDailyRate(IReadOnlyList<MeterReading> ordered)
    {
        decimal max = 0; var n = 0;
        for (var i = ordered.Count - 1; i > 0 && n < HistorySize; i--, n++)
            max = Math.Max(max, (ordered[i].Value - ordered[i - 1].Value) / Math.Max(1, (ordered[i].Date - ordered[i - 1].Date).Days));
        return max;
    }

    /// <summary>Consommation journalière moyenne sur les derniers intervalles croissants (les baisses forcées sont ignorées).</summary>
    public static decimal? AverageDailyRate(IReadOnlyList<MeterReading> ordered)
    {
        var rates = new List<decimal>();
        for (var i = ordered.Count - 1; i > 0 && rates.Count < HistorySize; i--)
        {
            var delta = ordered[i].Value - ordered[i - 1].Value;
            var days = Math.Max(1, (ordered[i].Date - ordered[i - 1].Date).Days);
            if (delta >= 0) rates.Add(delta / days);
        }
        return rates.Count == 0 ? null : rates.Average();
    }

    private static string Fmt(decimal v) => v.ToString("0.##", System.Globalization.CultureInfo.GetCultureInfo("fr-FR"));
}
