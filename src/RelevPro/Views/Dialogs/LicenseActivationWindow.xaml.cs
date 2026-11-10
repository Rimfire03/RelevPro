using System.Windows;
using System.Windows.Input;
using RelevPro.Core.Services.Licensing;
using TomLine.Licensing;
using TomLine.UI.Wpf.Controls;

namespace RelevPro.Views.Dialogs;

public partial class LicenseActivationWindow : Window
{
    public bool ActivationSucceeded { get; private set; }

    /// <param name="blockedMessage">Message d'accueil (ex. licence révoquée).</param>
    /// <param name="mode">État de la licence à l'ouverture : <see cref="LicenseMode.Expired"/> affiche
    /// "Réessayer" (relance la validation de la licence conservée).</param>
    /// <param name="changeMode">Changement de licence alors qu'une licence valide est en place : ni démo
    /// ni "Réessayer" ; "Annuler" garde la licence actuelle.</param>
    public LicenseActivationWindow(string? blockedMessage, LicenseMode mode = LicenseMode.Blocked, bool changeMode = false)
    {
        InitializeComponent();
        var expired = mode == LicenseMode.Expired && !changeMode;
        if (changeMode)
        {
            Title = "Changer de licence";
            Qh.Text = "Changer de licence";
            Qa.Text = "Saisissez la nouvelle clé de licence. La licence actuelle n'est remplacée que si la nouvelle est acceptée.";
            Ql.Content = "Annuler";
        }
        else if (expired)
        {
            Qh.Text = "Licence expirée";
            Qa.Text = string.IsNullOrWhiteSpace(blockedMessage) || blockedMessage == "Licence expirée."
                ? "Votre licence a expiré. Cliquez sur « Réessayer » si elle a été prolongée, ou saisissez une autre licence."
                : blockedMessage;
        }
        else
        {
            Qa.Text = string.IsNullOrWhiteSpace(blockedMessage) ? "Saisissez votre clé de licence pour continuer." : blockedMessage;
        }

        Qk.Visibility = expired ? Visibility.Visible : Visibility.Collapsed;
        // Pas de démo en licence gratuite (jamais de fenêtre dans ce cas), ni quand une licence valide est en place.
        Qi.Visibility = changeMode || LicenseManager.IsFreeLicense ? Visibility.Collapsed : Visibility.Visible;
        Loaded += (_, _) => Qb.Focus();
    }

    private void Qd(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) Qc(sender, e);
    }

    private void Busy(string text)
    {
        Qe.IsEnabled = false;
        Qi.IsEnabled = false;
        Qk.IsEnabled = false;
        Qf.Foreground = (System.Windows.Media.Brush)FindResource("BrushTextSecondary");
        Qf.Text = text;
    }

    private void Idle(string? error)
    {
        Qe.IsEnabled = true;
        Qi.IsEnabled = true;
        Qk.IsEnabled = true;
        Qf.Foreground = (System.Windows.Media.Brush)FindResource("BrushDanger");
        Qf.Text = error ?? "";
    }

    private void Done()
    {
        ActivationSucceeded = true;
        DialogResult = true;
        Close();
    }

    private async void Qc(object sender, RoutedEventArgs e)
    {
        var key = Qb.Text.Trim();
        if (key.Length == 0)
        {
            Qf.Text = "Merci de saisir une clé de licence.";
            return;
        }

        Busy("Activation en cours...");

        var outcome = await LicenseManager.ActivateAsync(key);
        if (outcome.Mode == LicenseMode.Active)
        {
            if (!string.IsNullOrEmpty(outcome.InfoMessage))
                MessageBox.Show(this, outcome.InfoMessage, "Licence activÃ©e", MessageBoxButton.OK, MessageBoxImage.Information);
            Done();
            return;
        }

        Idle(outcome.BlockedMessage ?? "Activation refusée.");
    }

    // Démo automatique : le serveur crée et active la démo pour ce poste, la clé n'est jamais montrée.
    // Appel asynchrone (timeout 5 s dans le client) : l'interface reste réactive, bouton désactivé.
    private DemoRequestProfile? _demoProfile;

    private async void Qj(object sender, RoutedEventArgs e)
    {
        // Formulaire du demandeur (civilité, nom, e-mail…) : envoyé au serveur et enregistré avec la démo.
        var form = new DemoRequestWindow("RelevPro", _demoProfile) { Owner = this };
        if (form.ShowDialog() != true || form.Profile is not { } profile) return;
        _demoProfile = profile;

        Busy("Demande de démo en cours...");

        var outcome = await LicenseManager.RequestDemoAsync(profile);
        if (outcome.Mode == LicenseMode.Active)
        {
            MessageBox.Show(this, outcome.InfoMessage ?? "Démo activée.", "Démo activée", MessageBoxButton.OK, MessageBoxImage.Information);
            Done();
            return;
        }

        Idle(outcome.BlockedMessage ?? "La démo n'a pas pu être activée.");
    }

    // Licence expirée : relance la validation (puis l'activation avec la clé conservée si besoin, voir
    // LicenseManager) - une licence prolongée côté serveur redevient valide.
    private async void Qm(object sender, RoutedEventArgs e)
    {
        Busy("Vérification de la licence...");

        var outcome = await LicenseManager.RevalidateAsync();
        if (outcome.Mode is LicenseMode.Active or LicenseMode.Grace)
        {
            Done();
            return;
        }

        Idle(outcome.BlockedMessage is null or "Licence expirée." ? "La licence est toujours expirée." : outcome.BlockedMessage);
    }

    private void Qg(object sender, RoutedEventArgs e)
    {
        ActivationSucceeded = false;
        DialogResult = false;
        Close();
    }
}
