namespace RelevPro.Core.Models;

/// <summary>Récurrence de saisie d'une ligne de vue.</summary>
public enum Recurrence { Hebdo = 0, Mensuel = 1, Trimestriel = 2, Semestriel = 3, Annuel = 4 }

public enum LineKind { Compteur = 0, Tache = 1 }

/// <summary>Compteur physique (eau, électricité...). Partagé entre plusieurs vues : ses relevés sont
/// enregistrés une seule fois, par compteur.</summary>
public class Meter : ISiteScoped
{
    public int Id { get; set; }
    public int SiteId { get; set; }
    public string Nom { get; set; } = string.Empty;
    public string Unite { get; set; } = string.Empty;
    public int Ordre { get; set; }

    /// <summary>Écart toléré, en % au-dessus de la consommation journalière moyenne habituelle, avant alerte.</summary>
    public int SeuilEcartPct { get; set; } = 100;
}

/// <summary>Vue de relevé : un écran de saisie regroupant des lignes (compteurs ou tâches).</summary>
public class ReadingView : ISiteScoped
{
    public int Id { get; set; }
    public int SiteId { get; set; }
    public string Nom { get; set; } = string.Empty;
    public int Ordre { get; set; }
    public List<ViewLine> Lines { get; set; } = new();
}

/// <summary>Ligne à saisir dans une vue : un compteur à relever ou une tâche à cocher, avec sa récurrence.</summary>
public class ViewLine : ISiteScoped
{
    public int Id { get; set; }
    public int SiteId { get; set; }
    public int ReadingViewId { get; set; }
    public ReadingView? ReadingView { get; set; }
    public LineKind Kind { get; set; }
    public int? MeterId { get; set; }
    public Meter? Meter { get; set; }

    /// <summary>Libellé de la tâche (vide pour un compteur : le nom du compteur est utilisé).</summary>
    public string Libelle { get; set; } = string.Empty;
    public Recurrence Recurrence { get; set; } = Recurrence.Mensuel;
    public int Ordre { get; set; }
    public DateTime CreatedOn { get; set; } = DateTime.Today;

    public string Titre => Kind == LineKind.Compteur ? Meter?.Nom ?? "(compteur supprimé)" : Libelle;
}

/// <summary>Valeur relevée d'un compteur à une date. <see cref="Forced"/> : saisie malgré une alerte.</summary>
public class MeterReading : ISiteScoped
{
    public int Id { get; set; }
    public int SiteId { get; set; }
    public int MeterId { get; set; }
    public Meter? Meter { get; set; }
    public DateTime Date { get; set; }
    public decimal Value { get; set; }
    public bool Forced { get; set; }
    public string Anomaly { get; set; } = string.Empty;
    public string EnteredBy { get; set; } = string.Empty;
    public DateTime EnteredAt { get; set; } = DateTime.Now;
}

/// <summary>Tâche cochée pour la période commençant à <see cref="PeriodStart"/>.</summary>
public class TaskCompletion : ISiteScoped
{
    public int Id { get; set; }
    public int SiteId { get; set; }
    public int ViewLineId { get; set; }
    public ViewLine? ViewLine { get; set; }
    public DateTime PeriodStart { get; set; }
    public DateTime CompletedAt { get; set; } = DateTime.Now;
    public string CompletedBy { get; set; } = string.Empty;
}
