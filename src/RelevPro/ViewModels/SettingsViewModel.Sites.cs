using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RelevPro.Core.Models;
using RelevPro.Core.Services;
using RelevPro.Services;
using Microsoft.EntityFrameworkCore;

namespace RelevPro.ViewModels;

/// <summary>Une ligne de la liste des sites (Paramètres du module MultiSite).</summary>
public class SiteRow(Site site, bool isCurrent, bool isDefault)
{
    public int Id { get; } = site.Id;
    public string Nom { get; } = site.Nom;
    public bool IsCurrent { get; } = isCurrent;
    public bool IsDefault { get; } = isDefault;

    /// <summary>Nom suivi des repères « ouvert » et « par défaut ».</summary>
    public string Display => Nom + (IsCurrent ? "   (site ouvert)" : "") + (IsDefault ? "   (par défaut)" : "");
}

/// <summary>Module MultiSite : activation (licence « multisite ») et gestion des sites.</summary>
public partial class SettingsViewModel
{
    [ObservableProperty] private bool _showMultiSiteModule;
    [ObservableProperty] private string _siteStatusMessage = string.Empty;

    public ObservableCollection<SiteRow> SiteRows { get; } = new();

    /// <summary>La case « Activer le module MultiSite » et la gestion des sites exigent la fonctionnalité de licence « multisite ».</summary>
    public bool CanToggleMultiSiteModule => App.MultiSiteLicensed;

    public bool ShowMultiSiteNotLicensed => !App.MultiSiteLicensed;

    public string MultiSiteToolTip => App.MultiSiteLicensed
        ? "Plusieurs sites aux données séparées ; seuls les Paramètres sont communs"
        : App.MultiSiteNotLicensedMessage;

    public string DefaultSiteDisplay => App.Settings.DefaultSiteId is int id && App.Sites.FirstOrDefault(s => s.Id == id) is { } s
        ? $"Site ouvert automatiquement au démarrage : {s.Nom}"
        : "Aucun site par défaut : le site est demandé à chaque ouverture.";

    partial void OnShowMultiSiteModuleChanged(bool value)
    {
        App.Settings.MultiSiteEnabled = value;
        App.Settings.Save();
        App.RaiseModuleVisibilityChanged();
    }

    private void LoadSites()
    {
        SiteRows.Clear();
        foreach (var s in App.Sites)
            SiteRows.Add(new SiteRow(s, s.Id == App.CurrentSite.Id, s.Id == App.Settings.DefaultSiteId));
        OnPropertyChanged(nameof(DefaultSiteDisplay));
    }

    private bool CanManageSites()
    {
        if (!App.MultiSiteLicensed)
        {
            App.Dialogs.ShowMessage("MultiSite", App.MultiSiteNotLicensedMessage + ".");
            return false;
        }
        return App.GuardWritable();
    }

    /// <summary>Nom saisi pour un site (nouveau ou renommé), ou null si annulé / invalide.</summary>
    private string? AskSiteName(string title, string initial, int? excludedId)
    {
        var name = initial;
        var fields = new List<EditField> { EditField.Text("Nom du site", () => name, v => name = v, required: true) };
        if (!App.Dialogs.EditFields(title, fields)) return null;
        name = name.Trim();
        if (name.Length == 0) return null;
        if (SiteService.NameExists(App.Db, name, excludedId))
        {
            App.Dialogs.ShowMessage("MultiSite", $"Un site nommé « {name} » existe déjà.");
            return null;
        }
        return name;
    }

    [RelayCommand]
    private void AddSite()
    {
        if (!CanManageSites()) return;
        var name = AskSiteName("Nouveau site", string.Empty, null);
        if (name is null) return;

        SiteService.Create(App.Db, name);
        App.RefreshSites();
        LoadSites();
        SiteStatusMessage = $"Site « {name} » créé (vide).";
    }

    [RelayCommand]
    private void RenameSite(SiteRow? row)
    {
        if (row is null || !CanManageSites()) return;
        var name = AskSiteName("Renommer le site", row.Nom, row.Id);
        if (name is null) return;

        SiteService.Rename(App.Db, row.Id, name);
        App.RefreshSites();
        LoadSites();
        SiteStatusMessage = $"Site renommé en « {name} ».";
    }

    [RelayCommand]
    private void SetDefaultSite(SiteRow? row)
    {
        if (row is null) return;
        App.Settings.DefaultSiteId = row.IsDefault ? null : row.Id; // un second clic retire le site par défaut
        App.Settings.Save();
        LoadSites();
        SiteStatusMessage = App.Settings.DefaultSiteId is null ? "Plus de site par défaut." : $"« {row.Nom} » s'ouvrira automatiquement.";
    }

    /// <summary>Suppression d'un site : confirmation, copie de sécurité de la base, puis nettoyage complet de ses
    /// données. Refus à la confirmation = le site n'est pas supprimé.</summary>
    [RelayCommand]
    private void DeleteSite(SiteRow? row)
    {
        if (row is null || !CanManageSites()) return;
        if (App.Sites.Count <= 1)
        {
            App.Dialogs.ShowMessage("MultiSite", "Le dernier site ne peut pas être supprimé.");
            return;
        }

        if (!App.Dialogs.ShowConfirm("Supprimer le site",
                $"Supprimer le site « {row.Nom} » ?\n\nTOUTES ses données seront définitivement effacées de la base : filtres et leur historique, " +
                "filtres sur encrassement, Liste K7, Inventaire, Commande, courroies et roulements. Les autres sites et les Paramètres ne sont pas touchés.\n\n" +
                "Une copie de sécurité de la base est faite juste avant. Cette action est irréversible."))
        {
            return; // refus : rien n'est supprimé
        }

        try
        {
            var backup = App.DbFactory.CreateBackup($"avant-suppression-site-{row.Id}");
            var removed = SiteService.Delete(App.Db, row.Id);

            if (App.Settings.DefaultSiteId == row.Id) App.Settings.DefaultSiteId = null;
            if (App.Settings.LastSiteId == row.Id) App.Settings.LastSiteId = null;
            App.Settings.Save();

            if (row.Id == App.CurrentSite.Id)
            {
                // Le site ouvert est supprimé : on bascule sur le site par défaut ou le premier restant.
                App.RefreshSites();
                var next = App.Sites.FirstOrDefault(s => s.Id == App.Settings.DefaultSiteId) ?? App.Sites[0];
                App.SwitchSite(next.Id);
                return;
            }

            App.RefreshSites();
            LoadSites();
            SiteStatusMessage = $"Site « {row.Nom} » supprimé ({removed} lignes effacées). Copie de sécurité : {backup}";
        }
        catch (Exception ex)
        {
            var inner = ex;
            while (inner.InnerException is not null) inner = inner.InnerException;
            SiteStatusMessage = $"Échec de la suppression (rien n'a été supprimé) : {inner.Message}";
        }
    }
}
