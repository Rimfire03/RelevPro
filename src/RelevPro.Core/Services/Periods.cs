using RelevPro.Core.Models;

namespace RelevPro.Core.Services;

/// <summary>Calcul des périodes de saisie (semaine lundi-dimanche, mois, trimestre, semestre, année).</summary>
public static class Periods
{
    public static DateTime Start(Recurrence r, DateTime d)
    {
        d = d.Date;
        return r switch
        {
            Recurrence.Hebdo => d.AddDays(-(((int)d.DayOfWeek + 6) % 7)),
            Recurrence.Mensuel => new DateTime(d.Year, d.Month, 1),
            Recurrence.Trimestriel => new DateTime(d.Year, (d.Month - 1) / 3 * 3 + 1, 1),
            Recurrence.Semestriel => new DateTime(d.Year, d.Month <= 6 ? 1 : 7, 1),
            _ => new DateTime(d.Year, 1, 1)
        };
    }

    /// <summary>Premier jour de la période suivante (borne exclusive de la période commençant à <paramref name="start"/>).</summary>
    public static DateTime Next(Recurrence r, DateTime start) => r switch
    {
        Recurrence.Hebdo => start.AddDays(7),
        Recurrence.Mensuel => start.AddMonths(1),
        Recurrence.Trimestriel => start.AddMonths(3),
        Recurrence.Semestriel => start.AddMonths(6),
        _ => start.AddYears(1)
    };

    public static DateTime Previous(Recurrence r, DateTime start) => r switch
    {
        Recurrence.Hebdo => start.AddDays(-7),
        Recurrence.Mensuel => start.AddMonths(-1),
        Recurrence.Trimestriel => start.AddMonths(-3),
        Recurrence.Semestriel => start.AddMonths(-6),
        _ => start.AddYears(-1)
    };

    public static string Label(Recurrence r, DateTime start)
    {
        var fr = System.Globalization.CultureInfo.GetCultureInfo("fr-FR");
        return r switch
        {
            Recurrence.Hebdo => $"Semaine du {start:dd/MM/yyyy} au {start.AddDays(6):dd/MM/yyyy}",
            Recurrence.Mensuel => fr.TextInfo.ToTitleCase(start.ToString("MMMM yyyy", fr)),
            Recurrence.Trimestriel => $"T{(start.Month - 1) / 3 + 1} {start.Year}",
            Recurrence.Semestriel => $"S{(start.Month <= 6 ? 1 : 2)} {start.Year}",
            _ => start.Year.ToString()
        };
    }

    public static string Name(Recurrence r) => r switch
    {
        Recurrence.Hebdo => "Hebdomadaire",
        Recurrence.Mensuel => "Mensuelle",
        Recurrence.Trimestriel => "Trimestrielle",
        Recurrence.Semestriel => "Semestrielle",
        _ => "Annuelle"
    };
}
