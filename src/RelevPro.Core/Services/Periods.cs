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

    /// <summary>Colonne de saisie d'une vue : une période de la fréquence de la vue.</summary>
    public sealed record Column(DateTime Start, DateTime End);

    /// <summary>Premier lundi à partir de <paramref name="d"/> (d inclus).</summary>
    public static DateTime FirstMonday(DateTime d) => d.Date.AddDays((8 - (int)d.DayOfWeek) % 7);

    /// <summary>Colonnes affichées pour la date de référence. Vue hebdomadaire : toutes les semaines (lundis) du mois.
    /// Autre fréquence : la seule période en cours.</summary>
    public static List<Column> Columns(Recurrence view, DateTime refDate)
    {
        var window = view == Recurrence.Hebdo ? Recurrence.Mensuel : view;
        var ws = Start(window, refDate);
        var we = Next(window, ws);
        if (view != Recurrence.Hebdo) return new() { new Column(ws, we) };
        var list = new List<Column>();
        for (var d = FirstMonday(ws); d < we; d = d.AddDays(7)) list.Add(new Column(d, d.AddDays(7)));
        return list;
    }

    /// <summary>Fenêtre affichée (mois pour une vue hebdomadaire, sinon la période de la vue).</summary>
    public static Recurrence Window(Recurrence view) => view == Recurrence.Hebdo ? Recurrence.Mensuel : view;

    /// <summary>La ligne se saisit-elle dans cette colonne ? Une ligne plus lente que la vue (mensuelle dans une vue
    /// hebdomadaire...) se saisit dans la première colonne de sa période.</summary>
    public static bool IsActive(Recurrence view, Recurrence line, DateTime columnStart)
    {
        if (line <= view) return true;
        var lineStart = Start(line, columnStart);
        return columnStart == (view == Recurrence.Hebdo ? FirstMonday(lineStart) : lineStart);
    }

    public static string ColumnLabel(Recurrence view, DateTime start) => view == Recurrence.Hebdo
        ? $"S{System.Globalization.ISOWeek.GetWeekOfYear(start)} · {start:dd/MM}"
        : Label(view, start);

    public static string Name(Recurrence r) => r switch
    {
        Recurrence.Hebdo => "Hebdomadaire",
        Recurrence.Mensuel => "Mensuelle",
        Recurrence.Trimestriel => "Trimestrielle",
        Recurrence.Semestriel => "Semestrielle",
        _ => "Annuelle"
    };
}
