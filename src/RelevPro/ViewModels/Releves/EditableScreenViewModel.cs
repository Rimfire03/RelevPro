using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace RelevPro.ViewModels.Releves;

/// <summary>Base des écrans qui ont un bouton « Mode édition » : la structure ne se modifie qu'une fois le mode activé,
/// la saisie des relevés reste toujours accessible.</summary>
public abstract partial class EditableScreenViewModel : ObservableObject
{
    public bool IsEditMode => App.IsEditMode;
    public string EditModeButtonLabel => IsEditMode ? "Quitter mode édition" : "Mode édition";

    [RelayCommand]
    private void ToggleEditMode()
    {
        App.IsEditMode = !App.IsEditMode;
        OnPropertyChanged(nameof(IsEditMode));
        OnPropertyChanged(nameof(EditModeButtonLabel));
    }

    protected static bool GuardEdit()
    {
        if (App.IsEditMode) return true;
        App.Dialogs.ShowMessage("Mode édition", "Cliquez sur « Mode édition » pour modifier la structure (vues, lignes, compteurs). La saisie des relevés reste toujours possible.");
        return false;
    }
}
