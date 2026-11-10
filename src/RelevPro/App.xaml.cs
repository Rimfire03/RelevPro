using System.IO;
using System.Windows;
using RelevPro.Core.Data;
using RelevPro.Core.Services;
using RelevPro.Core.Services.Licensing;
using RelevPro.Services;
using RelevPro.Views.Dialogs;

namespace RelevPro;

public partial class App : Application
{
    public static AppSettings Settings { get; private set; } = null!;
    public static DbContextFactory DbFactory { get; private set; } = null!;
    public static RelevDbContext Db { get; private set; } = null!;
    public static IDialogService Dialogs { get; private set; } = null!;
    public static ExcelExportService ExcelExport { get; private set; } = null!;

    /// <summary>Vrai si un autre poste détenait déjà l'accès en écriture au démarrage (voir
    /// <see cref="DbWriteLock"/>) : ce poste peut consulter les données mais pas les modifier.</summary>
    public static bool IsReadOnly { get; private set; }
    public static bool IsWritable => !IsReadOnly;

    /// <summary>Poste qui détient l'accès en écriture, affiché dans le bandeau "lecture seule".</summary>
    public static string? WriteLockOwner { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += (_, ex) =>
        {
            MessageBox.Show(ex.Exception.ToString(), "Erreur non gérée", MessageBoxButton.OK, MessageBoxImage.Error);
            ex.Handled = true;
        };

        // Mode assistant de mise à jour portable : la nouvelle version, lancée depuis le dossier temporaire, remplace les
        // fichiers de l'installation puis relance l'application (aucune fenêtre, ni CLUF ni licence).
        if (e.Args.Length > 0 && e.Args[0] == Core.Services.UpdateService.ApplyUpdateArg)
        {
            Core.Services.UpdateService.RunApplyHelper(e.Args);
            Shutdown(0);
            return;
        }
        Core.Services.UpdateService.CleanOldUpdateFolders();

        // Par défaut (OnLastWindowClose), fermer la fenêtre de saisie de la licence - avant que
        // MainWindow existe - déclenche la fermeture de toute l'application, puisque c'est alors
        // la seule fenêtre ouverte. Repassé à OnLastWindowClose juste après l'ouverture de
        // MainWindow, pour que fermer celle-ci quitte bien l'application normalement.
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        if (!EnsureEulaAccepted())
        {
            Shutdown(-1);
            return;
        }

        if (!Qw9())
        {
            Shutdown(-1);
            return;
        }

        try
        {
            Settings = AppSettings.Load();
            if (!EnsureDatabaseSelected())
            {
                Shutdown(-1);
                return;
            }

            OpenDatabase(Settings.ResolvedTarget);
            ApplyFirstRunSite();
        }
        catch (DatabaseVersionException ex)
        {
            // Base plus récente que le logiciel : proposer la mise à jour du logiciel plutôt qu'une erreur.
            if (!(ex.AppTooOld && OfferUpdateForNewerDatabase()))
                MessageBox.Show(ex.Message, "Démarrage impossible : version de la base de données", MessageBoxButton.OK, MessageBoxImage.Stop);
            Shutdown(-1);
            return;
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Impossible d'ouvrir la base de données :\n{Settings?.ResolvedTarget.Describe()}\n\n{ex.Message}",
                "Erreur au démarrage", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(-1);
            return;
        }

        if (!ChooseStartupSite(e.Args))
        {
            Shutdown(-1);
            return;
        }
        ApplyFirstRunView();

        EmptyDataGridLayout.Register();
        Dialogs = new DialogService();
        ExcelExport = new ExcelExportService();

        var mainWindow = new MainWindow();
        MainWindow = mainWindow;
        mainWindow.Show();
        ShutdownMode = ShutdownMode.OnLastWindowClose;
        StartLicenseRevalidation();
        DatabaseHealth.Start();
        AutoBackup.Start();

        if (IsReadOnly)
            MessageBox.Show(mainWindow, ReadOnlyMessage, "Données en lecture seule", MessageBoxButton.OK, MessageBoxImage.Information);

        NotificationsAtStartup();

        if (Settings.AutoUpdateEnabled) _ = CheckForUpdateOnStartupAsync();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Db?.Dispose();
        _writeLock?.Dispose();
        ReleaseServerLock();
        base.OnExit(e);
    }

    /// <summary>À appeler avant toute modification de données : sur un poste en lecture seule, prévient
    /// l'utilisateur et retourne false.</summary>
    public static bool GuardWritable()
    {
        if (IsWritable) return true;
        Dialogs.ShowMessage("Lecture seule", ReadOnlyMessage + "\n\nAucune modification n'a été enregistrée.");
        return false;
    }

    /// <summary>Faux si la session Windows n'est pas dans la liste des utilisateurs autorisés en lecture/écriture
    /// (Paramètres > Utilisateurs) : le poste est alors en lecture seule, quel que soit le verrou d'écriture.</summary>
    public static bool AccessAllowed { get; private set; } = true;

    /// <summary>La gestion des utilisateurs est-elle activée (Paramètres > Utilisateurs) ? Désactivée par défaut :
    /// aucun contrôle, et le cadenas du pied de page est masqué.</summary>
    public static bool AccessEnabled { get; private set; }

    public static string ReadOnlyTitle => !AccessAllowed
        ? $"Données en lecture seule : la session « {Core.Services.AccessControl.CurrentIdentity} » n'est pas autorisée à modifier les données"
        : "Données en lecture seule : " + (DbFactory is { IsServer: true } ? "base actuellement utilisée par " : "fichier actuellement utilisé par ") +
          (WriteLockOwner is null ? "un autre utilisateur" : "@" + WriteLockOwner);

    public static string ReadOnlyMessage => !AccessAllowed
        ? ReadOnlyTitle + ". Demandez à un utilisateur autorisé de vous ajouter dans Paramètres > Utilisateurs."
        : ReadOnlyTitle + ". Fermez puis relancez l'application une fois qu'il l'a quittée pour pouvoir modifier les données.";

    /// <summary>Cadenas du pied de page : vert si la session est autorisée en lecture/écriture, rouge sinon.</summary>
    public static System.Windows.Media.Brush AccessBrush => Frozen(AccessAllowed ? "#16A34A" : "#DC2626");

    private static System.Windows.Media.Brush Frozen(string hex)
    {
        var brush = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        return brush;
    }

    public static string AccessTooltip => AccessAllowed
        ? $"Accès approuvé : la session « {Core.Services.AccessControl.CurrentIdentity} » est autorisée en lecture/écriture."
        : $"Accès non approuvé : la session « {Core.Services.AccessControl.CurrentIdentity} » est en lecture seule (voir Paramètres > Utilisateurs).";
}
