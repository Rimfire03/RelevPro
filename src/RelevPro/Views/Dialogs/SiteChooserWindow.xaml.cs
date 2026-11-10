using System.Windows;
using System.Windows.Input;
using RelevPro.Core.Models;

namespace RelevPro.Views.Dialogs;

/// <summary>Choix du site à l'ouverture de l'application (module MultiSite, aucun site par défaut défini).</summary>
public partial class SiteChooserWindow : Window
{
    public Site? Chosen { get; private set; }
    public bool MakeDefault => DefaultCheck.IsChecked == true;

    public SiteChooserWindow(IReadOnlyList<Site> sites)
    {
        InitializeComponent();
        SiteList.ItemsSource = sites;
        SiteList.SelectedIndex = Math.Max(0, sites.ToList().FindIndex(s => s.Id == App.Settings.LastSiteId));
    }

    private void OnOpen(object sender, RoutedEventArgs e)
    {
        if (SiteList.SelectedItem is not Site site) return;
        Chosen = site;
        DialogResult = true;
    }

    private void OnDoubleClick(object sender, MouseButtonEventArgs e) => OnOpen(sender, e);

    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;
}
