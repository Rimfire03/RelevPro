using System.Diagnostics;
using System.IO;
using System.Windows;
using RelevPro.Core.Services;
using RelevPro.Services;

namespace RelevPro;

/// <summary>Ouverture de la base : choix du fichier, accès en écriture (un seul poste), contrôle de version.</summary>
public partial class App
{
    private static DbWriteLock? _writeLock;
    private static ServerWriteLock? _serverLock;
    private static System.Windows.Threading.DispatcherTimer? _serverLockTimer;
    private static string? _writeLockPath;

    /// <summary>Version du fichier de base ouvert (voir DbContextFactory.LatestVersion).</summary>
    public static int DatabaseVersion { get; private set; }

    /// <summary>État de l'accès à la base (puce du pied de page), contrôlé régulièrement.</summary>
    public static DatabaseHealthMonitor DatabaseHealth { get; } = new();
    public static AutoBackupScheduler AutoBackup { get; } = new();

    /// <summary>Type de base utilisé, pour le pied de page : « Locale (SQLite) » ou « Serveur PostgreSQL ».</summary>
    public static string DatabaseModeLabel => DbFactory is { IsServer: true }
        ? "Serveur " + DatabaseProviderInfo.DisplayName(DbFactory.Target.Provider)
        : "Locale (SQLite)";

    /// <summary>Si aucun fichier de base de données n'existe à l'emplacement configuré (premier lancement),
    /// lance l'assistant de première configuration : nouvelle base (site, première vue) ou base existante (.db).
    /// Retourne false si l'utilisateur quitte, auquel cas l'application doit se fermer.</summary>
    private static bool EnsureDatabaseSelected()
    {
        if (Settings.UseDatabaseServer) return EnsureServerReachable();

        // Premier lancement (aucune base à l'emplacement configuré) : assistant TomLine.UI.Wpf.
        if (!File.Exists(Settings.ResolvedDatabasePath))
        {
            var wizard = new Views.Dialogs.FirstRunWizard(Settings.ResolvedDatabasePath);
            if (wizard.ShowDialog() != true) return false;
            if (wizard.Result.ExistingPath is { } existing) Settings.DatabasePath = existing;
            else { Settings.DatabasePath = wizard.Result.DatabasePath; PendingFirstRun = wizard.Result; }
            Settings.Save();
        }

        return true;
    }

    /// <summary>Mode serveur : vérifie que le serveur répond avant d'ouvrir la base. En cas d'échec, propose de
    /// réessayer, de revenir au fichier SQLite local ou de quitter. Retourne false pour quitter.</summary>
    private static bool EnsureServerReachable()
    {
        while (true)
        {
            string error;
            try
            {
                new DbContextFactory(Settings.ResolvedTarget).TestConnection();
                return true;
            }
            catch (Exception ex)
            {
                while (ex.InnerException is not null) ex = ex.InnerException;
                error = ex.Message;
            }

            var choice = MessageBox.Show(
                $"Impossible de joindre le serveur de base de données :\n{Settings.DatabaseServer.Describe()}\n\n{error}\n\n" +
                "Oui : réessayer.\n" +
                "Non : utiliser de nouveau le fichier SQLite local.\n" +
                "Annuler : fermer l'application.",
                "Serveur de base de données injoignable", MessageBoxButton.YesNoCancel, MessageBoxImage.Warning);

            if (choice == MessageBoxResult.Yes) continue;
            if (choice != MessageBoxResult.No) return false;

            Settings.UseDatabaseServer = false;
            Settings.Save();
            return EnsureDatabaseSelected();
        }
    }

    /// <summary>Prend l'accès en écriture si aucun autre poste ne l'a déjà, sinon ouvre la base en
    /// lecture seule. Sur un serveur de base de données, le verrou est une ligne de la table DbInfo
    /// (voir <see cref="ServerWriteLock"/>) : même règle du premier connecté.</summary>
    private static void OpenDatabase(DbTarget target)
    {
        // Liste des utilisateurs autorisés (Paramètres > Utilisateurs) : une session non autorisée reste en lecture
        // seule, sans même chercher à prendre le verrou d'écriture.
        var access = AccessControl.Evaluate(target);
        AccessAllowed = access.Allowed;
        AccessEnabled = access.Enabled;

        if (!AccessAllowed)
        {
            ReleaseServerLock();
            _writeLock?.Dispose();
            _writeLock = null;
            _writeLockPath = null;
            IsReadOnly = true;
            WriteLockOwner = null;
        }
        else if (target.IsServer)
        {
            _writeLock?.Dispose();
            _writeLock = null;
            _writeLockPath = null;
            // Comme avec un fichier : seul le premier poste connecté peut écrire, les suivants sont en lecture seule.
            if (_serverLock is null)
            {
                var result = ServerWriteLock.TryAcquire(target);
                _serverLock = result.Lock;
                IsReadOnly = result.Lock is null && result.OwnerUser is not null;
                WriteLockOwner = IsReadOnly ? result.OwnerUser : null;
                if (_serverLock is not null) StartServerLockHeartbeat();
            }
        }
        else
        {
            var path = target.FilePath!;
            // Rechargement du même fichier par le rédacteur : on garde le verrou pour ne pas le céder.
            if (_writeLock is null || !string.Equals(_writeLockPath, path, StringComparison.OrdinalIgnoreCase))
            {
                _writeLock?.Dispose();
                _writeLock = DbWriteLock.TryAcquire(path);
                _writeLockPath = path;
            }
            IsReadOnly = _writeLock is null;
            WriteLockOwner = IsReadOnly ? DbWriteLock.ReadOwner(path) : null;
        }

        DbFactory = new DbContextFactory(target, IsReadOnly);
        CheckDatabaseVersion();
        BackupOnVersionChange();
        DbFactory.EnsureDatabaseUpToDate(CurrentVersion);
        if (target.IsServer && !IsReadOnly && _serverLock is null)
        {
            // Base serveur qui vient d'être créée : le verrou n'existait pas encore, on le prend maintenant.
            _serverLock = ServerWriteLock.TryAcquire(target).Lock;
            if (_serverLock is not null) StartServerLockHeartbeat();
        }
        DatabaseVersion = DbFactory.GetDatabaseVersion();
        Db = DbFactory.Create();
        LoadCompanyLogo();
    }

    /// <summary>Renouvelle le verrou d'écriture du serveur toutes les 15 s. Si un autre poste l'a repris (ce poste est
    /// resté injoignable plus d'une minute), l'application redémarre en lecture seule.</summary>
    private static void StartServerLockHeartbeat()
    {
        _serverLockTimer?.Stop();
        _serverLockTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(15) };
        _serverLockTimer.Tick += async (_, _) =>
        {
            var held = _serverLock;
            if (held is null) return;
            bool still;
            try
            {
                still = await Task.Run(held.Refresh);
            }
            catch
            {
                return; // serveur injoignable : on réessaie au prochain tour (la puce du pied de page signale la perte)
            }
            if (still) return;

            _serverLockTimer?.Stop();
            _serverLock = null;
            MessageBox.Show(
                "Un autre poste a repris l'accès en écriture à la base (ce poste est resté injoignable trop longtemps).\n\n" +
                "L'application va redémarrer en lecture seule.",
                "Accès en écriture perdu", MessageBoxButton.OK, MessageBoxImage.Warning);
            Restart();
        };
        _serverLockTimer.Start();
    }

    /// <summary>Libère le verrou d'écriture du serveur (fermeture de l'application).</summary>
    private static void ReleaseServerLock()
    {
        _serverLockTimer?.Stop();
        _serverLock?.Dispose();
        _serverLock = null;
    }
    /// <summary>Quand le logiciel a changé de version depuis le dernier démarrage (mise à jour par l'application, MSI ou
    /// copie manuelle), sauvegarde la base dans RelevData\Save DB avant qu'elle ne soit ouverte et migrée. Un échec de
    /// sauvegarde ne bloque pas le démarrage.</summary>
    private static void BackupOnVersionChange()
    {
        if (IsReadOnly || Settings.LastRunVersion == CurrentVersion) return;
        try
        {
            if (!DbFactory.IsNewDatabase())
                DbFactory.CreateBackup($"avant-version-{CurrentVersion}");
            Settings.LastRunVersion = CurrentVersion;
            Settings.Save();
        }
        catch
        {
            // Réessayé au prochain démarrage.
        }
    }

    /// <summary>Bloque l'ouverture si ce logiciel et la base ne sont pas à la même version : logiciel trop
    /// ancien pour une base déjà mise à jour, ou poste en lecture seule qui ne peut pas mettre la base à
    /// jour lui-même.</summary>
    private static void CheckDatabaseVersion()
    {
        if (DbFactory.IsNewDatabase()) return; // base neuve, créée directement à la dernière version

        var dbVersion = DbFactory.GetDatabaseVersion();
        var expected = DbContextFactory.LatestVersion;

        if (dbVersion > expected)
        {
            var by = DbFactory.GetLastMigratedByAppVersion();
            throw new DatabaseVersionException(
                $"Cette version du logiciel ({CurrentVersion}) est trop ancienne pour ouvrir la base de données.\n\n" +
                $"Version de la base : {dbVersion}" + (by is null ? "" : $" (mise à jour par le logiciel version {by})") + "\n" +
                $"Version de base gérée par ce logiciel : {expected}\n\n" +
                "Installez la dernière version du logiciel sur ce poste" + (by is null ? "" : $" ({by} ou plus récente)") +
                ", puis relancez-le. Rien n'a été modifié dans la base.", appTooOld: true);
        }

        if (dbVersion < expected && !IsReadOnly)
        {
            var pending = DbFactory.IsServer ? DbContextFactory.PendingServerMigrations(dbVersion) : DbContextFactory.PendingMigrations(dbVersion);
            var changes = string.Join("\n", pending.Select(c => "  • " + c));
            var accepted = MessageBox.Show(
                $"La base de données est en version {dbVersion} ; cette version du logiciel ({CurrentVersion}) a besoin de la version {expected}.\n\n" +
                $"Modifications à appliquer :\n{changes}\n\n" +
                (DbFactory.IsServer
                    ? "Faites une sauvegarde du serveur avant de continuer si ce n'est pas déjà fait. "
                    : "Une copie de sauvegarde complète de la base sera faite à côté du fichier avant la mise à jour. ") +
                "Après la mise à jour, les postes équipés d'une version plus ancienne du logiciel ne pourront plus l'ouvrir.\n\n" +
                "Mettre à jour la base maintenant ?",
                "Mise à jour de la base de données", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);
            if (accepted != MessageBoxResult.Yes)
                throw new DatabaseVersionException(
                    $"La base de données n'a pas été mise à jour : elle reste en version {dbVersion}, et cette version du logiciel ({CurrentVersion}) " +
                    $"ne peut pas l'ouvrir sans la mettre en version {expected}.\n\n" +
                    "Relancez le logiciel et acceptez la mise à jour, ou utilisez sur ce poste la version du logiciel qui correspond à la base.");
        }

        if (dbVersion < expected && IsReadOnly)
        {
            var owner = WriteLockOwner is null ? "un autre utilisateur" : "@" + WriteLockOwner;
            throw new DatabaseVersionException(
                $"La base de données doit être mise à jour (version {dbVersion} → {expected}) pour cette version du logiciel ({CurrentVersion}), " +
                $"mais elle est actuellement ouverte en écriture par {owner}, qui utilise une version plus ancienne du logiciel.\n\n" +
                "La mise à jour de la base se fera automatiquement au prochain lancement du logiciel à jour sur un poste " +
                $"ayant l'accès en écriture. Demandez à {owner} de fermer le logiciel (et de le mettre à jour), puis relancez-le ici.");
        }
    }

    /// <summary>Remplace le fichier de base courant par une sauvegarde choisie (bouton "Importer une
    /// sauvegarde", écran Paramètres), puis relance l'application : c'est ce redémarrage qui déclenche,
    /// via <see cref="OpenDatabase"/> → <see cref="CheckDatabaseVersion"/> → <see cref="DbContextFactory.EnsureDatabaseUpToDate"/>,
    /// la vérification de la version de la sauvegarde importée et sa mise à jour automatique si elle est
    /// antérieure (avec la même confirmation et la même sauvegarde préalable que pour toute autre mise à
    /// jour de base). Réservé au poste rédacteur (voir <see cref="GuardWritable"/>, appelé par l'appelant).</summary>
    public static void ImportDatabaseBackup(string backupPath)
    {
        if (DbFactory.IsServer)
        {
            // Serveur : la sauvegarde (fichier SQLite) est d'abord mise à jour sur une copie temporaire, puis
            // son contenu remplace celui du serveur.
            var temp = Path.Combine(Path.GetTempPath(), $"filtres-import-{Guid.NewGuid():N}.db");
            try
            {
                File.Copy(backupPath, temp, overwrite: true);
                new DbContextFactory(temp).EnsureDatabaseUpToDate(CurrentVersion);
                DatabaseCopier.Copy(DbTarget.ForFile(temp), DbFactory.Target, CurrentVersion, replaceDestination: true);
            }
            finally
            {
                Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
                foreach (var f in Directory.GetFiles(Path.GetTempPath(), Path.GetFileName(temp) + "*"))
                    try { File.Delete(f); } catch { /* temporaire */ }
            }
            Db.Dispose();
            Restart();
            return;
        }

        Db.Dispose();
        // Les connexions Sqlite peuvent rester mises en pool après Dispose() et garder le fichier
        // verrouillé : on les libère explicitement avant d'écraser le fichier.
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        File.Copy(backupPath, Settings.ResolvedDatabasePath, overwrite: true);
        Restart();
    }

    /// <summary>Relance l'application dans un nouveau processus puis ferme celui-ci. Utilisé après un
    /// changement de chemin de base de données (Paramètres) : un simple rechargement du contexte ne
    /// suffirait pas, tous les écrans déjà ouverts (barre latérale, année consultée...) gardant sinon des
    /// données de l'ancienne base en mémoire.</summary>
    public static void Restart(string? arguments = null)
    {
        var exePath = Environment.ProcessPath;
        if (exePath is not null)
            Process.Start(new ProcessStartInfo { FileName = exePath, Arguments = arguments ?? string.Empty, UseShellExecute = true });
        Current.Dispatcher.Invoke(() => Current.Shutdown()); // peut être appelé depuis un thread d'arrière-plan
    }
}
