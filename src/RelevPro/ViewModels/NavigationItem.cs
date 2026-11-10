using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace RelevPro.ViewModels;

/// <summary>Élément de la barre latérale : soit une feuille (écran classique, sans enfants), soit un menu
/// dépliant (« Relevés ») avec des sous-menus créés dynamiquement (une vue de relevé = un enfant).</summary>
public partial class NavigationItem : ObservableObject
{
    [ObservableProperty] private string _title;
    [ObservableProperty] private string _icon;
    private readonly Func<object>? _factory;
    private object? _viewModel;

    /// <summary>Identifiant de la vue de relevé représentée par ce sous-menu, ou null pour un autre élément.</summary>
    public int? ViewId { get; }

    public ObservableCollection<NavigationItem> Children { get; } = new();

    [ObservableProperty] private bool _isExpanded;
    [ObservableProperty] private bool _isSelected;

    /// <summary>Simple ligne de séparation dans le menu défilant (ex. entre les écrans fixes et les modules
    /// activables "Courroies" / "Roulements"), pas un écran : jamais sélectionnable, voir MainWindow.xaml.</summary>
    public bool IsSeparator { get; }

    /// <summary>Faux si la fonctionnalité du module correspondant n'est pas présente dans la licence active
    /// (voir LicenseManager.HasFeature, réévalué par MainViewModel.RefreshModuleVisibility) : l'entrée reste
    /// visible (l'utilisateur l'a activée dans Paramètres) mais grisée et non cliquable, voir
    /// MainWindow.xaml - jamais juste masquée, pour que l'utilisateur comprenne qu'une licence est requise.</summary>
    [ObservableProperty] private bool _isLicensed = true;

    /// <summary>Info-bulle affichée quand l'entrée est grisée faute de licence (voir MainWindow.xaml) ; un module
    /// peut préciser son propre message.</summary>
    public string NotLicensedMessage { get; init; } = "Nécessite une licence : module non couvert par votre licence actuelle.";

    public NavigationItem(string title, string icon, Func<object>? factory, int? viewId = null, bool isSeparator = false)
    {
        _title = title;
        _icon = icon;
        _factory = factory;
        ViewId = viewId;
        IsSeparator = isSeparator;
    }

    public static NavigationItem Separator() => new(string.Empty, string.Empty, null, isSeparator: true);

    public object? GetOrCreateViewModel() => _viewModel ??= _factory?.Invoke();

    public void Reset() => _viewModel = null;

    public override string ToString() => Title;
}
