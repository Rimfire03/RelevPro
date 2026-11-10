namespace RelevPro.Core.Models;

/// <summary>Ligne propre à un site (module MultiSite) : filtrée automatiquement par le site courant du contexte
/// (voir RelevDbContext.CurrentSiteId) et rattachée à ce site à la création. Les réglages communs (couleurs de
/// lignes, logo, titres des menus) n'ont pas de site.</summary>
public interface ISiteScoped
{
    int SiteId { get; set; }
}

/// <summary>Un site du module MultiSite : ses vues de changement de filtre, filtres sur encrassement, Liste K7,
/// Inventaire, Commande, courroies et roulements sont indépendants de ceux des autres sites. Seuls les Paramètres
/// sont communs. Le site 1 (« Site principal ») reprend les données d'avant l'activation du module.</summary>
public class Site
{
    public int Id { get; set; }
    public string Nom { get; set; } = string.Empty;

    /// <summary>Ordre d'affichage dans les listes de sites.</summary>
    public int Ordre { get; set; }

    public const string DefaultName = "Site principal";
}
