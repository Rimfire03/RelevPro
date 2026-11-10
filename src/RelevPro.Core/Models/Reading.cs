namespace RelevPro.Core.Models;

/// <summary>Récurrence de saisie d'une ligne de vue.</summary>
public enum Recurrence { Hebdo = 0, Mensuel = 1, Trimestriel = 2, Semestriel = 3, Annuel = 4 }

public enum LineKind { Compteur = 0, Tache = 1 }

/// <summary>Type d'entrée d'un catalogue (listes déroulantes).</summary>
public enum CatalogKind { Unite = 0, Famille = 1 }

/// <summary>Entrée d'un catalogue modifiable : unité de mesure ou famille de compteurs (électricité, plomberie...).</summary>
public class CatalogItem : ISiteScoped
{
    public int Id { get; set; }
    public int SiteId { get; set; }
    public CatalogKind Kind { get; set; }
    public string Nom { get; set; } = string.Empty;
    public int Ordre { get; set; }
}

/// <summary>Compteur physique (eau, électricité...). Partagé entre plusieurs vues : ses relevés sont
/// enregistrés une seule fois, par compteur.</summary>
public class Meter : ISiteScoped
{
    public int Id { get; set; }
    public int SiteId { get; set; }
    public string Nom { get; set; } = string.Empty;
    public string Unite { get; set; } = string.Empty;

    /// <summary>Famille du compteur (catalogue « Famille » : électricité, plomberie, chauffage...), vide = aucune.</summary>
    public string Famille { get; set; } = string.Empty;
    public int Ordre { get; set; }

    /// <summary>Écart toléré, en % au-dessus de la consommation journalière moyenne habituelle, avant alerte.</summary>
    public int SeuilEcartPct { get; set; } = 100;

    /// <summary>Seuil d'alarme haut : augmentation maximale entre deux relevés (compteur cumulatif) ou valeur maximale
    /// (mesure). 0 = aucun seuil.</summary>
    public decimal SeuilHaut { get; set; }

    /// <summary>Vrai : index qui ne fait qu'augmenter (consommation calculée). Faux : mesure ou niveau (peut baisser,
    /// pas de consommation).</summary>
    public bool Cumulatif { get; set; } = true;

    /// <summary>Compteur arrêté ou ignoré : plus de saisie ni de notification, l'historique reste consultable
    /// (lignes masquées de la saisie sauf « Afficher les compteurs arrêtés »).</summary>
    public bool Arrete { get; set; }

    /// <summary>Valeur à laquelle le compteur repasse à 0 (ex. 100000 pour 5 chiffres). 0 = automatique : puissance de 10
    /// juste au-dessus du plus grand relevé (voir <see cref="Services.ConsumptionService.Capacity"/>).</summary>
    public decimal Capacite { get; set; }
}

/// <summary>Vue de relevé : un écran de saisie regroupant des lignes (compteurs ou tâches).</summary>
public class ReadingView : ISiteScoped
{
    public int Id { get; set; }
    public int SiteId { get; set; }
    public string Nom { get; set; } = string.Empty;

    /// <summary>Famille de regroupement dans le menu (vide = vue non regroupée).</summary>
    public string Famille { get; set; } = string.Empty;

    /// <summary>Fréquence de la vue : une colonne de saisie par période (semaine, mois...). Les lignes ont la même
    /// récurrence ou une plus lente (voir <see cref="Services.Periods.IsActive"/>).</summary>
    public Recurrence Recurrence { get; set; } = Recurrence.Hebdo;
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

    /// <summary>Intitulé de section dans la vue (ex. « Gaz », « Traitement chlore »), vide = sans section.</summary>
    public string Groupe { get; set; } = string.Empty;
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

    /// <summary>Le compteur a fait un tour complet (retour à 0) depuis le relevé précédent : la consommation est
    /// calculée avec la capacité du compteur, au lieu d'une baisse d'index.</summary>
    public bool Tour { get; set; }

    /// <summary>Le compteur a été remplacé depuis le relevé précédent (valeur proche de 0 sans tour) : la consommation de
    /// l'écart est la nouvelle valeur (le nouveau compteur part de 0), jamais négative.</summary>
    public bool Remplacement { get; set; }
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
