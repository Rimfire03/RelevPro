using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RelevPro.Core.Services;
using RelevPro.Services;
using Microsoft.Win32;

namespace RelevPro.ViewModels;

/// <summary>Paramètres : logo de l'entreprise.</summary>
public partial class SettingsViewModel
{
    // ---- Logo de l'entreprise (stocké dans la base, commun à tous les postes) ----
    public System.Windows.Media.ImageSource? LogoImage => App.CompanyLogoImage;
    public bool HasLogo => App.CompanyLogo is not null;
    [ObservableProperty] private string _logoStatusMessage = string.Empty;

    [RelayCommand]
    private void ChooseLogo()
    {
        if (!App.GuardWritable()) return;
        var dialog = new OpenFileDialog
        {
            Title = "Choisir le logo de l'entreprise",
            Filter = "Images (*.png;*.jpg;*.jpeg;*.bmp;*.gif)|*.png;*.jpg;*.jpeg;*.bmp;*.gif"
        };
        if (dialog.ShowDialog() != true) return;

        var info = new System.IO.FileInfo(dialog.FileName);
        if (info.Length > CompanyLogoService.MaxBytes)
        {
            LogoStatusMessage = $"Image trop volumineuse ({info.Length / 1024} Ko) : 2 Mo au maximum.";
            return;
        }

        var data = System.IO.File.ReadAllBytes(dialog.FileName);
        if (ImageLoader.TryCreate(data) is null)
        {
            LogoStatusMessage = "Ce fichier n'est pas une image lisible.";
            return;
        }

        App.SetCompanyLogo(data);
        RefreshLogo();
        LogoStatusMessage = "Logo enregistré dans la base : il est utilisé par tous les postes.";
    }

    [RelayCommand]
    private void RemoveLogo()
    {
        if (!App.GuardWritable() || App.CompanyLogo is null) return;
        if (!App.Dialogs.ShowConfirm("Retirer le logo", "Retirer le logo de l'entreprise pour tous les postes ?")) return;
        App.SetCompanyLogo(null);
        RefreshLogo();
        LogoStatusMessage = "Logo retiré.";
    }

    private void RefreshLogo()
    {
        OnPropertyChanged(nameof(LogoImage));
        OnPropertyChanged(nameof(HasLogo));
    }
}
