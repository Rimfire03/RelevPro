namespace RelevPro;

/// <summary>Modules activables depuis Paramètres (voir AppSettings.ShowBeltsModule / ShowBearingsModule) :
/// MainViewModel écoute cet événement pour ajouter/retirer l'entrée de menu correspondante sans redémarrer
/// l'application.</summary>
public partial class App
{
    public static event Action? ModuleVisibilityChanged;

    public static void RaiseModuleVisibilityChanged() => ModuleVisibilityChanged?.Invoke();
}
