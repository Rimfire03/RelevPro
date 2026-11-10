using System.Windows;
using RelevPro.Core.Models;
using RelevPro.Core.Services;
using RelevPro.Core.Services.Licensing;
using RelevPro.Views.Dialogs;

namespace RelevPro;

/// <summary>Module MultiSite : site courant, choix à l'ouverture, bascule. Les données de chaque site sont
/// séparées dans la base (voir ISiteScoped) ; seuls les Paramètres sont communs.</summary>
public partial class App
{
    /// <summary>Site dont les données sont affichées (le filtre de site du contexte <see cref="Db"/> pointe dessus).</summary>
    public static Site CurrentSite { get; private set; } = new() { Id = 1, Nom = Site.DefaultName };

    public static IReadOnlyList<Site> Sites { get; private set; } = Array.Empty<Site>();

    /// <summary>Déclenché quand la liste des sites change (création, renommage, suppression).</summary>
    public static event Action? SitesChanged;

    /// <summary>La licence couvre la fonctionnalité « multisite » (licence gratuite / bypass : oui, comme pour
    /// tous les modules, voir LicenseManager.HasFeature).</summary>
    public static bool MultiSiteLicensed => LicenseManager.HasFeature("multisite");

    /// <summary>Module activé dans Paramètres ET couvert par la licence. Sinon l'application fonctionne en
    /// mono-site : le site actif est conservé, les autres sites restent intacts mais inaccessibles.</summary>
    public static bool MultiSiteUsable => Settings.MultiSiteEnabled && MultiSiteLicensed;

    /// <summary>Le sélecteur de site (barre latérale) n'existe que si le module est utilisable et que plusieurs
    /// sites existent.</summary>
    public static bool MultiSiteAvailable => MultiSiteUsable && Sites.Count > 1;

    public const string FeatureMultiSite = "multisite";
    public const string MultiSiteNotLicensedMessage = "Fonctionnalité Multisite : non incluse dans votre licence";

    public static void RefreshSites()
    {
        Sites = SiteService.GetSites(Db);
        CurrentSite = Sites.FirstOrDefault(s => s.Id == CurrentSite.Id) ?? CurrentSite;
        SitesChanged?.Invoke();
    }

    /// <summary>Détermine le site à ouvrir au démarrage et règle le contexte de base dessus. Retourne false si
    /// l'utilisateur annule le choix (l'application se ferme).
    /// Ordre : site demandé par une bascule (<c>--site=ID</c>), site par défaut des Paramètres, sinon choix
    /// à l'écran. Sans module utilisable (désactivé ou hors licence) : dernier site utilisé, sinon site par
    /// défaut, sinon le premier, sans rien demander.</summary>
    private static bool ChooseStartupSite(string[] args)
    {
        Sites = SiteService.GetSites(Db);
        if (Sites.Count == 0) Sites = new[] { new Site { Id = 1, Nom = Site.DefaultName } };

        int? requested = args.Select(a => a.StartsWith("--site=") && int.TryParse(a[7..], out var v) ? (int?)v : null)
            .FirstOrDefault(v => v is not null);

        Site? pick;
        if (MultiSiteUsable && Sites.Count > 1)
        {
            pick = Sites.FirstOrDefault(s => s.Id == requested) ?? Sites.FirstOrDefault(s => s.Id == Settings.DefaultSiteId);
            if (pick is null)
            {
                var chooser = new SiteChooserWindow(Sites);
                if (chooser.ShowDialog() != true || chooser.Chosen is null) return false;
                pick = chooser.Chosen;
                if (chooser.MakeDefault)
                {
                    Settings.DefaultSiteId = pick.Id;
                    Settings.Save();
                }
            }
        }
        else
        {
            pick = Sites.FirstOrDefault(s => s.Id == Settings.LastSiteId)
                   ?? Sites.FirstOrDefault(s => s.Id == Settings.DefaultSiteId)
                   ?? Sites[0];
        }

        UseSite(pick);
        return true;
    }

    private static void UseSite(Site site)
    {
        CurrentSite = site;
        Db.CurrentSiteId = site.Id;
        DbFactory.SiteId = site.Id;
        Db.ChangeTracker.Clear();
        if (Settings.LastSiteId != site.Id)
        {
            Settings.LastSiteId = site.Id;
            Settings.Save();
        }
    }

    /// <summary>Bascule vers un autre site sans redémarrer : le filtre de site du contexte change, puis la fenêtre principale reçoit un nouveau modèle de vue (menus, vues, années du nouveau site) ; l'ancien est détaché des événements globaux.</summary>
    public static void SwitchSite(int siteId)
    {
        var site = Sites.FirstOrDefault(s => s.Id == siteId);
        if (site is null || site.Id == CurrentSite.Id) return;

        UseSite(site);
        SitesChanged?.Invoke();

        if (Current.MainWindow is { } window)
        {
            var old = window.DataContext as RelevPro.ViewModels.MainViewModel;
            window.DataContext = new RelevPro.ViewModels.MainViewModel();
            old?.Dispose();
        }
    }
}
