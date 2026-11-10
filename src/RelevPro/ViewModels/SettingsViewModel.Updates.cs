using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RelevPro.Core.Services;

namespace RelevPro.ViewModels;

/// <summary>Paramètres : vérification et installation des mises à jour.</summary>
public partial class SettingsViewModel
{
    [ObservableProperty] private string _updateStatusMessage = string.Empty;
    [ObservableProperty] private bool _isCheckingForUpdate;
    [ObservableProperty] private bool _isInstallingUpdate;
    [ObservableProperty] private bool _updateAvailable;

    private UpdateInfo? _pendingUpdate;

    public string[] UpdateChannelChoices => UpdateChannels.All;

    [ObservableProperty] private string _updateChannel = App.Settings.UpdateChannel;

    /// <summary>Sauvegardé immédiatement ; l'ancien résultat de recherche ne vaut plus pour le nouveau canal.</summary>
    partial void OnUpdateChannelChanged(string value)
    {
        App.Settings.UpdateChannel = value;
        App.Settings.Save();
        UpdateAvailable = false;
        _pendingUpdate = null;
        UpdateStatusMessage = string.Empty;
        _ = CheckForUpdatesCommand.ExecuteAsync(null);   // vérification immédiate sur le nouveau canal
    }

    [RelayCommand]
    private async Task CheckForUpdates()
    {
        IsCheckingForUpdate = true;
        UpdateAvailable = false;
        _pendingUpdate = null;
        UpdateStatusMessage = "Recherche d'une mise à jour...";
        try
        {
            var info = await App.CheckForUpdateAsync();
            if (info is null)
            {
                UpdateStatusMessage = App.Updater.LastCheckNote ?? $"Vous utilisez la dernière version ({App.DisplayVersion}).";
            }
            else
            {
                _pendingUpdate = info;
                UpdateAvailable = true;
                UpdateStatusMessage = info.IsChannelSwitch
                    ? $"Retour sur le canal Main possible : version {info.Version} (actuelle : {App.DisplayVersion}), même base de données."
                    : $"Nouvelle version disponible : {info.Version} (actuelle : {App.DisplayVersion}).";
            }
        }
        catch (Exception ex)
        {
            UpdateStatusMessage = $"Erreur pendant la vérification : {ex.Message}";
        }
        finally
        {
            IsCheckingForUpdate = false;
        }
    }

    /// <summary>Sauvegarde la base de données, télécharge et installe la mise à jour (remplace le contenu
    /// du dossier d'installation), puis relance l'application - voir UpdateService.DownloadAndApplyAsync.</summary>
    [RelayCommand]
    private async Task InstallUpdate()
    {
        if (_pendingUpdate is null) return;

        if (!App.Dialogs.ShowConfirm("Installer la mise à jour",
                $"Télécharger et installer la version {_pendingUpdate.Version} ? Une copie de sauvegarde de la base de données sera faite avant toute chose. L'application va se fermer puis redémarrer automatiquement."))
        {
            return;
        }

        IsInstallingUpdate = true;
        UpdateStatusMessage = "Téléchargement de la mise à jour...";
        try
        {
            App.DbFactory.CreateBackup("avant-mise-a-jour");
            await App.Updater.DownloadAndApplyAsync(_pendingUpdate);
            System.Windows.Application.Current.Shutdown();
        }
        catch (Exception ex)
        {
            UpdateStatusMessage = $"Erreur pendant l'installation : {ex.Message}";
            IsInstallingUpdate = false;
        }
    }
}
