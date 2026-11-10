using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RelevPro.Core.Models;
using RelevPro.Core.Services;
using RelevPro.Core.Services.Licensing;
using RelevPro.ViewModels.Releves;
using Microsoft.EntityFrameworkCore;

namespace RelevPro.ViewModels;

public partial class MainViewModel : ObservableObject, IDisposable
{
    public ObservableCollection<NavigationItem> NavigationItems { get; } = new();

    /// <summary>Menu dépliant « Relevés » : ses enfants sont les vues de relevé. Le menu lui-même ouvre la page de gestion des vues.</summary>
    private readonly NavigationItem _viewsMenu;

    public NavigationItem HelpItem { get; } = new("Aide", "❓", () => new HelpViewModel());
    public NavigationItem SettingsItem { get; }
    public NavigationItem NotificationsItem { get; }

    public IReadOnlyList<Site> Sites => App.Sites;
    public bool ShowSiteSelector => App.MultiSiteAvailable;

    public Site? SelectedSite
    {
        get => App.Sites.FirstOrDefault(s => s.Id == App.CurrentSite.Id);
        set
        {
            if (value is null || value.Id == App.CurrentSite.Id) return;
            // Différé : la fenêtre change de modèle de vue, ce qui ne doit pas se faire pendant la mise à jour de la liaison.
            var id = value.Id;
            System.Windows.Application.Current.Dispatcher.BeginInvoke(() => App.SwitchSite(id));
        }
    }

    [ObservableProperty] private NavigationItem? _selectedItem;
    [ObservableProperty] private object? _currentViewModel;
    [ObservableProperty] private int _notificationBadge;

    public bool HasNotificationBadge => NotificationBadge > 0;
    partial void OnNotificationBadgeChanged(int value) => OnPropertyChanged(nameof(HasNotificationBadge));

    /// <summary>Logo de l'entreprise en haut de la barre latérale (null si aucun).</summary>
    public System.Windows.Media.ImageSource? LogoImage => App.CompanyLogoImage;

    private readonly Action _onLogoChanged, _onSitesChanged, _onModulesChanged, _onLicenseChanged;

    public MainViewModel()
    {
        _viewsMenu = new NavigationItem("Relevés", "📋", () => new ReadingViewListViewModel(this)) { IsExpanded = true };
        SettingsItem = new NavigationItem("Paramètres", "⚙", () => new SettingsViewModel(this));
        NotificationsItem = new NavigationItem("Notifications", "🔔", () => new NotificationsViewModel(this));

        NavigationItems.Add(_viewsMenu);
        NavigationItems.Add(new NavigationItem("Compteurs", "🔢", () => new MeterListViewModel(this)));
        RebuildViewItems();
        RefreshNotificationBadge();

        _onLogoChanged = () => OnPropertyChanged(nameof(LogoImage));
        _onSitesChanged = () => System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            OnPropertyChanged(nameof(Sites));
            OnPropertyChanged(nameof(SelectedSite));
            OnPropertyChanged(nameof(ShowSiteSelector));
        });
        _onModulesChanged = () => OnPropertyChanged(nameof(ShowSiteSelector));
        _onLicenseChanged = () => System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            OnPropertyChanged(nameof(LicenseFooter));
            OnPropertyChanged(nameof(LicenseFooterBrush));
            OnPropertyChanged(nameof(ShowSiteSelector));
        });
        App.CompanyLogoChanged += _onLogoChanged;
        App.SitesChanged += _onSitesChanged;
        App.ModuleVisibilityChanged += _onModulesChanged;
        LicenseManager.Changed += _onLicenseChanged;
    }

    /// <summary>Se détache des événements globaux : à appeler quand ce modèle de vue est remplacé (changement de site).</summary>
    public void Dispose()
    {
        App.CompanyLogoChanged -= _onLogoChanged;
        App.SitesChanged -= _onSitesChanged;
        App.ModuleVisibilityChanged -= _onModulesChanged;
        LicenseManager.Changed -= _onLicenseChanged;
    }

    public string LicenseFooter => LicenseManager.FooterTextWithPosts;

    public System.Windows.Media.Brush LicenseFooterBrush => LicenseManager.FooterIsAlert
        ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xF8, 0x71, 0x71))
        : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x64, 0x74, 0x8B));

    /// <summary>Recalcule le nombre de saisies en retard affiché à côté de « Notifications ».</summary>
    public void RefreshNotificationBadge()
    {
        try { NotificationBadge = NotificationService.LateCount(App.Db, DateTime.Today); }
        catch { NotificationBadge = 0; }
    }

    /// <summary>Reconstruit les sous-menus du menu « Relevés » depuis la base (création, renommage, suppression d'une vue).</summary>
    public void RebuildViewItems()
    {
        var selectedViewId = SelectedItem?.ViewId;
        _viewsMenu.Children.Clear();
        var views = App.Db.ReadingViews.AsNoTracking().OrderBy(v => v.Ordre).ThenBy(v => v.Nom).ToList();
        NavigationItem Leaf(RelevPro.Core.Models.ReadingView v) { var id = v.Id; return new NavigationItem(v.Nom, "▫", () => new ReadingViewModel(this, id), viewId: id); }
        foreach (var fam in views.Where(v => v.Famille.Length > 0).GroupBy(v => v.Famille, StringComparer.CurrentCultureIgnoreCase).OrderBy(g => g.Key, StringComparer.CurrentCultureIgnoreCase))
        {
            var node = new NavigationItem(fam.Key, "📁", () => new ReadingViewListViewModel(this)) { IsExpanded = true };
            foreach (var v in fam) node.Children.Add(Leaf(v));
            _viewsMenu.Children.Add(node);
        }
        foreach (var v in views.Where(v => v.Famille.Length == 0)) _viewsMenu.Children.Add(Leaf(v));
        if (selectedViewId is { } sel)
            SelectedItem = ViewItems().FirstOrDefault(c => c.ViewId == sel) ?? _viewsMenu;
    }

    /// <summary>Feuilles du menu « Relevés » (vues), regroupées ou non par famille.</summary>
    private IEnumerable<NavigationItem> ViewItems() =>
        _viewsMenu.Children.SelectMany(c => c.ViewId is not null ? new[] { c } : c.Children.ToArray());

    /// <summary>Oublie les écrans déjà construits (sauf l'écran affiché) pour qu'ils relisent la base à la prochaine ouverture.</summary>
    public void ResetOtherScreens()
    {
        foreach (var item in NavigationItems)
        {
            if (item != SelectedItem) item.Reset();
            foreach (var child in item.Children)
            {
                if (child != SelectedItem) child.Reset();
                foreach (var leaf in child.Children) if (leaf != SelectedItem) leaf.Reset();
            }
        }
    }

    public void ShowNotifications() => SelectedItem = NotificationsItem;

    public void OpenView(int viewId)
    {
        var item = ViewItems().FirstOrDefault(c => c.ViewId == viewId);
        if (item is not null) SelectedItem = item;
    }

    partial void OnSelectedItemChanged(NavigationItem? value)
    {
        // Sélectionner le menu « Relevés » le déplie aussi.
        if (value == _viewsMenu) _viewsMenu.IsExpanded = true;

        foreach (var item in NavigationItems) item.IsSelected = item == value;
        foreach (var child in _viewsMenu.Children) { child.IsSelected = child == value; foreach (var leaf in child.Children) leaf.IsSelected = leaf == value; }
        HelpItem.IsSelected = value == HelpItem;
        SettingsItem.IsSelected = value == SettingsItem;
        NotificationsItem.IsSelected = value == NotificationsItem;

        // Serveur de base de données : d'autres postes écrivent en même temps. On oublie les lignes déjà lues
        // (rien n'est en attente, les saisies sont enregistrées tout de suite) pour relire le serveur.
        if (App.DbFactory.IsServer && !App.Db.ChangeTracker.HasChanges()) App.Db.ChangeTracker.Clear();

        value?.Reset(); // écran reconstruit à chaque sélection : données toujours à jour (relevés saisis dans une autre vue, autre poste)
        CurrentViewModel = value?.GetOrCreateViewModel();
        RefreshNotificationBadge();
    }

    [RelayCommand] private void ShowHelp() => SelectedItem = HelpItem;
    [RelayCommand] private void ShowSettings() => SelectedItem = SettingsItem;
    [RelayCommand] private void ShowNotificationsPage() => ShowNotifications();
}
