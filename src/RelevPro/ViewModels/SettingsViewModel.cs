using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RelevPro.Core.Services;
using RelevPro.Core.Services.Licensing;
using RelevPro.Services;
using Microsoft.Win32;

namespace RelevPro.ViewModels;

/// <summary>Écran "Paramètres" : chemins, options, export Excel. Autres sections : SettingsViewModel.Logo.cs,
/// SettingsViewModel.History.cs, SettingsViewModel.Updates.cs.</summary>
public partial class SettingsViewModel : ObservableObject
{
    [ObservableProperty] private string _databasePath;
    [ObservableProperty] private string _exportPath;
    [ObservableProperty] private string _statusMessage = string.Empty;
    [ObservableProperty] private string _exportStatusMessage = string.Empty;

    [ObservableProperty] private string _backupStatusMessage = string.Empty;
    [ObservableProperty] private string _exportFolderStatusMessage = string.Empty;

    [ObservableProperty] private bool _autoUpdateEnabled;
    public string CurrentVersion => App.DisplayVersion;
    public int DatabaseVersion => App.DatabaseVersion;

    /// <summary>Taille du fichier de base de données, affichée en Ko ou Mo. Recalculée après tout ce qui
    /// peut la faire varier sensiblement (rechargement, suppression d'historique).</summary>
    public string DatabaseSizeDisplay => App.DbFactory.IsServer ? "serveur" : FormatFileSize(App.Settings.ResolvedDatabasePath);

    /// <summary>Mode de stockage actuel, avec le serveur le cas échéant (sans mot de passe).</summary>
    public string DatabaseModeDisplay => App.DbFactory.IsServer
        ? "Mode : serveur de base de données - " + App.DbFactory.Target.Describe()
        : "Mode : fichier SQLite";

    /// <summary>Ouvre la page « Gestion des sauvegardes automatiques » (liste, planification, conservation).</summary>
    [RelayCommand]
    private void OpenBackupManager()
    {
        var window = new RelevPro.Views.Dialogs.BackupManagerWindow { Owner = System.Windows.Application.Current.MainWindow };
        window.ShowDialog();
    }

    /// <summary>Ouvre la page « Réglages BDD avancés » (serveur de base de données, migration).</summary>
    [RelayCommand]
    private void OpenDatabaseAdvancedSettings()
    {
        var window = new RelevPro.Views.Dialogs.DatabaseServerWindow { Owner = System.Windows.Application.Current.MainWindow };
        window.ShowDialog();
    }

    private static string FormatFileSize(string path)
    {
        long bytes;
        try { bytes = new System.IO.FileInfo(path).Length; }
        catch { return "inconnue"; }
        return bytes < 1024 * 1024 ? $"{bytes / 1024.0:0.0} Ko" : $"{bytes / 1024.0 / 1024.0:0.0} Mo";
    }

    /// <summary>Années proposées pour l'export Excel : de la plus ancienne saisie à l'année en cours.</summary>
    public List<int> ExportYears { get; }

    private readonly MainViewModel _main;

    public SettingsViewModel(MainViewModel main)
    {
        _main = main;
        _databasePath = App.Settings.DatabasePath;
        _exportPath = App.Settings.ExportPath;
        _autoUpdateEnabled = App.Settings.AutoUpdateEnabled;
        _showMultiSiteModule = App.Settings.MultiSiteEnabled;
        LoadSites();
        var first = App.Db.MeterReadings.Min(r => (DateTime?)r.Date)?.Year ?? DateTime.Today.Year;
        ExportYears = Enumerable.Range(first, DateTime.Today.Year - first + 1).Reverse().ToList();
        _selectedExportYear = DateTime.Today.Year;
        InitializeLastWrite();
        ReloadAccess();
        LicenseManager.Changed += () => System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            OnPropertyChanged(nameof(LicenseStatusText));
            OnPropertyChanged(nameof(ShowLicenseButtons));
            OnPropertyChanged(nameof(CanToggleMultiSiteModule));
            OnPropertyChanged(nameof(MultiSiteToolTip));
            OnPropertyChanged(nameof(ShowMultiSiteNotLicensed));
        });
    }

    /// <summary>La case à cocher se sauvegarde immédiatement : contrairement aux autres champs, il n'y
    /// a pas de raison de faire attendre l'utilisateur jusqu'au bouton "Enregistrer les paramètres".</summary>
    partial void OnAutoUpdateEnabledChanged(bool value)
    {
        App.Settings.AutoUpdateEnabled = value;
        App.Settings.Save();
    }

    /// <summary>"Lire le contrat de licence (CLUF)" (dernière carte de l'écran) : relecture seule.</summary>
    [RelayCommand]
    private void ShowEula() => App.ShowEula();

    /// <summary>Carte "Licence" masquée en licence gratuite (flag de build ou licence.ini) : rien à
    /// afficher ni à déconnecter dans ce cas.</summary>
    public bool ShowLicenseCard => true;
    /// <summary>Boutons "Changer de licence" / "Se déconnecter" : jamais en licence gratuite (bypass).</summary>
    public bool ShowLicenseButtons => !LicenseManager.IsFreeLicense;
    public string LicenseStatusText => LicenseManager.FooterTextWithPosts;

    /// <summary>"Changer de licence" (par exemple passer d'une démo à une vraie licence) : la nouvelle clé est
    /// activée d'abord, l'ancienne désactivée ensuite ; en cas d'échec ou d'annulation, l'ancienne reste en
    /// place. Les modules sont recalculés tout de suite (voir LicenseManager.Changed).</summary>
    [RelayCommand]
    private void ChangeLicense() => App.ChangeLicense();

    /// <summary>"Se déconnecter / changer de licence" : libère l'activation côté serveur puis efface la
    /// licence stockée localement (jamais licence.ini) et referme l'application - l'écran de saisie de
    /// clé s'affichera au prochain lancement.</summary>
    [RelayCommand]
    private async Task DisconnectLicense()
    {
        if (!App.Dialogs.ShowConfirm("Changer de licence",
                "Déconnecter cette licence ? L'application va se fermer ; relancez-la pour saisir une nouvelle clé."))
            return;

        await LicenseManager.DeactivateAsync();
        Application.Current.Shutdown();
    }

    /// <summary>Année à exporter en Excel : sélecteur propre à cette carte (indépendant de l'année consultée
    /// dans la barre latérale), placé par défaut sur l'année en cours.</summary>
    [ObservableProperty] private int _selectedExportYear = DateTime.Today.Year;

    [RelayCommand]
    private void ExportExcelYear()
    {
        try
        {
            var year = SelectedExportYear;
            var path = App.ExcelExport.ExportYear(App.Db, App.Settings.ResolvedExportPath, year, App.Sites.Count > 1 ? App.CurrentSite.Nom : null);
            ExportStatusMessage = $"Export Excel de l'année {year} généré : {path}";
        }
        catch (Exception ex)
        {
            ExportStatusMessage = $"Erreur pendant l'export Excel : {ex.Message}";
        }
    }

    [RelayCommand]
    private void BrowseDatabasePath()
    {
        var dialog = new SaveFileDialog
        {
            Filter = "Base de données SQLite (*.db)|*.db",
            FileName = "relevpro.db",
            InitialDirectory = Path.Combine(AppContext.BaseDirectory, "RelevData")
        };
        if (dialog.ShowDialog() == true) DatabasePath = dialog.FileName;
    }

    [RelayCommand]
    private void BrowseExportPath()
    {
        var dialog = new OpenFolderDialog
        {
            InitialDirectory = Path.Combine(AppContext.BaseDirectory, "RelevData")
        };
        if (dialog.ShowDialog() == true) ExportPath = dialog.FolderName;
    }

    /// <summary>Un changement de chemin de base de données exige un redémarrage complet (voir
    /// <see cref="App.Restart"/>) : un simple rechargement laisserait les écrans déjà ouverts (barre
    /// latérale, année consultée...) avec des données de l'ancienne base en mémoire.</summary>
    [RelayCommand]
    private void SaveSettings()
    {
        var databasePathChanged = !string.Equals(DatabasePath, App.Settings.DatabasePath, StringComparison.Ordinal);

        if (databasePathChanged && !App.Dialogs.ShowConfirm("Changement de base de données",
                "Le chemin de la base de données a changé. L'application va se fermer puis redémarrer automatiquement pour l'ouvrir.\n\n" +
                "Continuer ?"))
        {
            DatabasePath = App.Settings.DatabasePath; // annulé : revient au chemin actuellement ouvert
            return;
        }

        App.Settings.DatabasePath = DatabasePath;
        App.Settings.Save();

        if (databasePathChanged)
        {
            App.Restart();
            return;
        }

        StatusMessage = "Paramètres enregistrés.";
    }

    /// <summary>Enregistre le dossier d'export PDF / Excel (carte « Export PDF » des Paramètres).</summary>
    [RelayCommand]
    private void SaveExportPath()
    {
        App.Settings.ExportPath = ExportPath;
        App.Settings.Save();
        ExportFolderStatusMessage = "Dossier d'export enregistré.";
    }
    /// <summary>Dossier RelevData\Save DB (créé au besoin) : emplacement proposé pour les dumps et sauvegardes.</summary>
    private static string SaveDbDirectory()
    {
        Directory.CreateDirectory(DbContextFactory.BackupDirectory);
        return DbContextFactory.BackupDirectory;
    }

    [RelayCommand]
    private void ExportDatabase()
    {
        var dialog = new SaveFileDialog
        {
            Filter = "Base de données SQLite (*.db)|*.db",
            FileName = $"save_db_relevpro_{DateTime.Now:yyyyMMdd_HHmmss}_{App.CurrentVersion}.db",
            InitialDirectory = SaveDbDirectory()
        };
        if (dialog.ShowDialog() != true) return;

        try
        {
            App.DbFactory.ExportTo(dialog.FileName);
            BackupStatusMessage = $"Sauvegarde exportée : {dialog.FileName}";
        }
        catch (Exception ex)
        {
            BackupStatusMessage = $"Erreur pendant l'export de la base : {ex.Message}";
        }
    }

    /// <summary>Ouvre directement le sélecteur de fichier, puis remplace le fichier de base courant par la
    /// sauvegarde choisie et redémarre : voir <see cref="App.ImportDatabaseBackup"/>, qui déclenche au
    /// redémarrage la vérification/mise à jour automatique de la version de schéma de la sauvegarde
    /// importée.</summary>
    [RelayCommand]
    private void ImportDatabase()
    {
        if (!App.GuardWritable()) return;

        var dialog = new OpenFileDialog
        {
            Title = "Importer une sauvegarde ou un dump",
            Filter = "Sauvegarde ou dump (*.db;*.bak)|*.db;*.bak|Tous les fichiers (*.*)|*.*",
            InitialDirectory = SaveDbDirectory()
        };
        if (dialog.ShowDialog() != true) return;
        var path = dialog.FileName;

        // Le fichier doit être une base de ce logiciel, pas plus récente que lui.
        int version;
        try
        {
            version = new DbContextFactory(DbTarget.ForFile(path), readOnly: true).GetDatabaseVersion();
        }
        catch (Exception ex)
        {
            App.Dialogs.ShowMessage("Fichier illisible", "Ce fichier n'est pas une sauvegarde valide : " + ex.Message);
            return;
        }
        if (version == 0)
        {
            App.Dialogs.ShowMessage("Fichier illisible", "Ce fichier ne contient pas de base de données de ce logiciel.");
            return;
        }
        if (version > DbContextFactory.LatestVersion)
        {
            App.Dialogs.ShowMessage("Sauvegarde trop récente",
                $"Cette sauvegarde a été créée par une version plus récente du logiciel (base v{version}, ce logiciel gère v{DbContextFactory.LatestVersion}). Mettez d'abord le logiciel à jour.");
            return;
        }

        if (!App.Dialogs.ShowConfirm("Importer une sauvegarde",
                $"TOUTES les données actuelles ({App.DbFactory.Target.Describe()}) vont être remplacées par celles du fichier :\n{path}\n\n" +
                "Une copie de sécurité de la base actuelle est faite juste avant. Le logiciel redémarrera ensuite (la version de la sauvegarde sera mise à jour si besoin). Continuer ?"))
        {
            return;
        }

        try
        {
            // La purge est suspendue : elle ne doit pas supprimer la sauvegarde choisie avant son import.
            BackupService.PruneSuspended = true;
            var safety = App.DbFactory.CreateBackup("avant-import");
            BackupStatusMessage = $"Copie de sécurité faite : {safety}";
            App.ImportDatabaseBackup(path);
        }
        catch (Exception ex)
        {
            BackupService.PruneSuspended = false;
            BackupStatusMessage = $"Erreur pendant l'import de la sauvegarde : {ex.Message}\nLa base actuelle n'a pas été modifiée.";
        }
    }
}