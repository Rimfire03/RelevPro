using RelevPro.Core.Services.Licensing;
using RelevPro.Views.Dialogs;

namespace RelevPro;

public partial class App
{
    /// <summary>Ordre serveur "remove_bypass" exécuté (licence.ini supprimé) : le flux normal de licence reprend
    /// sans fermer l'application. Licence stockée valide = on continue ; sinon message, puis fenêtre de saisie
    /// (qui, annulée, referme l'application comme au démarrage).</summary>
    private static async void OnBypassRemoved()
    {
        try
        {
            var o = await LicenseManager.CheckAtStartupAsync();
            if (o.Mode is not (LicenseMode.Blocked or LicenseMode.Expired)) return;

            System.Windows.MessageBox.Show("Le mode licence gratuite a été désactivé par l'administrateur.",
                "Licence", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
            var w = new LicenseActivationWindow(o.BlockedMessage, o.Mode);
            if (w.ShowDialog() != true || !w.ActivationSucceeded) Current.Shutdown();
        }
        catch { }
    }

    private static bool Qw9()
    {
        LicenseManager.BypassRemoved += () => Current?.Dispatcher.BeginInvoke(OnBypassRemoved);
        // Task.Run (pas un .GetAwaiter().GetResult() direct) : CheckAtStartupAsync peut faire un vrai
        // appel réseau (voir LicenseManager.Vs) dont la suite voudrait reprendre sur le thread
        // d'interface via son SynchronizationContext - ici bloqué en attente synchrone, avant même que
        // la boucle de messages ne tourne (OnStartup). Sans ce Task.Run, l'application se fige
        // définitivement dès qu'une licence est déjà stockée (donc dès le second lancement).
        var o = System.Threading.Tasks.Task.Run(() => LicenseManager.CheckAtStartupAsync()).GetAwaiter().GetResult();
        if (o.Mode is not (LicenseMode.Blocked or LicenseMode.Expired)) { ShowProfileIfRequired(); return true; }

        var w = new LicenseActivationWindow(o.BlockedMessage, o.Mode);
        var ok = w.ShowDialog() == true && w.ActivationSucceeded;
        if (ok) ShowProfileIfRequired();
        return ok;
    }

    /// <summary>Informations client demandées par le serveur : jamais bloquant, une seule fois par lancement, jamais en
    /// licence.ini (aucune demande dans ce mode).</summary>
    private static void ShowProfileIfRequired()
    {
        try { TomLine.UI.Wpf.Controls.LicenseProfilePrompt.ShowIfRequired(LicenseManager.Service, Current?.MainWindow, "RelevPro"); }
        catch { }
    }

    /// <summary>"Changer de licence" depuis Paramètres : la nouvelle clé est activée avant que l'ancienne soit
    /// désactivée ; annulation ou échec = l'ancienne licence reste en place.</summary>
    public static bool ChangeLicense()
    {
        var w = new LicenseActivationWindow(null, LicenseManager.Mode, changeMode: true) { Owner = Current.MainWindow };
        return w.ShowDialog() == true && w.ActivationSucceeded;
    }

    private static System.Windows.Threading.DispatcherTimer? _licenseTimer;

    /// <summary>Revalidation périodique : prolongation ou changement de modules côté serveur pris en compte
    /// sans relancer l'application (les abonnés de LicenseManager.Changed recalculent l'état des modules).</summary>
    public static void StartLicenseRevalidation()
    {
        // Le minuteur tourne aussi en mode bypass (licence.ini) : il y sert au ping toutes les 24 h.
        if (_licenseTimer is not null || LicenseManager.IsGlobalFree) return;
        _licenseTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMinutes(15) };
        _licenseTimer.Tick += async (_, _) =>
        {
            try { await LicenseManager.RevalidateAsync(); ShowProfileIfRequired(); } catch { }
        };
        _licenseTimer.Start();
    }

    public static string LicenseFooterText => LicenseManager.FooterTextWithPosts;
    public static bool IsLicenseOfflineGrace => LicenseManager.IsOfflineGrace;
}
