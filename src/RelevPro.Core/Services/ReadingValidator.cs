using RelevPro.Core.Models;

namespace RelevPro.Core.Services;

/// <summary>Contrôle d'une valeur saisie : un compteur ne peut qu'augmenter, et la consommation journalière ne doit
/// pas dépasser de trop la moyenne habituelle (seuil du compteur).</summary>
public static class ReadingValidator
{
    /// <summary>Nombre d'intervalles précédents utilisés pour la moyenne.</summary>
    public const int HistorySize = 6;

    /// <summary>Retourne les anomalies (vide = valeur acceptée). <paramref name="others"/> : relevés existants du
    /// compteur, hors celui qui est modifié.</summary>
    public static List<string> Check(Meter meter, DateTime date, decimal value, IEnumerable<MeterReading> others)
    {
        var list = others.OrderBy(r => r.Date).ToList();
        var anomalies = new List<string>();
        var prev = list.LastOrDefault(r => r.Date < date.Date);
        var next = list.FirstOrDefault(r => r.Date > date.Date);

        if (prev is not null && value < prev.Value)
            anomalies.Add($"Le compteur diminue : {Fmt(value)} < {Fmt(prev.Value)} (relevé du {prev.Date:dd/MM/yyyy}).");
        if (next is not null && value > next.Value)
            anomalies.Add($"Valeur supérieure au relevé suivant : {Fmt(value)} > {Fmt(next.Value)} (relevé du {next.Date:dd/MM/yyyy}).");

        if (prev is not null && value >= prev.Value)
        {
            var avg = AverageDailyRate(list.Where(r => r.Date <= prev.Date).ToList());
            if (avg is > 0)
            {
                var days = Math.Max(1, (date.Date - prev.Date).Days);
                var rate = (value - prev.Value) / days;
                var limit = avg.Value * (1 + meter.SeuilEcartPct / 100m);
                if (rate > limit)
                    anomalies.Add($"Écart trop grand : {Fmt(value - prev.Value)} {meter.Unite} en {days} j " +
                                  $"(habituel : {Fmt(avg.Value * days)} {meter.Unite}, seuil +{meter.SeuilEcartPct} %).");
            }
        }
        return anomalies;
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
