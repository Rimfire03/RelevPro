using RelevPro.Core.Data;
using RelevPro.Core.Data.Migrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;

namespace RelevPro.Core.Services;

public class DbContextFactory
{
    private readonly DbTarget _target;
    private readonly bool _readOnly;

    public DbContextFactory(string dbPath, bool readOnly = false) : this(DbTarget.ForFile(dbPath), readOnly) { }

    public DbContextFactory(DbTarget target, bool readOnly = false)
    {
        _target = target;
        // Lecture seule : fichier SQLite ouvert en lecture seule, ou serveur dont les écritures sont refusées (ReadOnlyGuard).
        _readOnly = readOnly;
        if (!target.IsServer)
        {
            var dir = Path.GetDirectoryName(target.FilePath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        }
    }

    public DbTarget Target => _target;
    public bool IsServer => _target.IsServer;

    /// <summary>Site affiché par les contextes créés (module MultiSite).</summary>
    public int SiteId { get; set; } = 1;

    public RelevDbContext Create() => new(_target, _readOnly) { CurrentSiteId = SiteId };

    /// <summary>Crée le site principal si la table des sites est vide (base neuve).</summary>
    private static void EnsureDefaultSite(RelevDbContext ctx)
    {
        if (ctx.Sites.Any()) return;
        ctx.Sites.Add(new Models.Site { Nom = Models.Site.DefaultName });
        ctx.SaveChanges();
        ctx.ChangeTracker.Clear();
    }

    // Migrations du schéma : voir Data/Migrations/DatabaseMigrations.cs (règles de rédaction incluses).

    /// <summary>Version de base attendue par cette version de l'application.</summary>
    public static int LatestVersion => DatabaseMigrations.All[^1].Version;

    /// <summary>Libellés des migrations à appliquer depuis <paramref name="fromVersion"/>, pour la demande
    /// de confirmation.</summary>
    public static IReadOnlyList<string> PendingMigrations(int fromVersion) =>
        DatabaseMigrations.All.Where(m => m.Version > fromVersion).Select(m => $"v{m.Version} : {m.Description}").ToList();

    /// <summary>Libellés des mises à jour à appliquer sur un serveur depuis <paramref name="fromVersion"/>.</summary>
    public static IReadOnlyList<string> PendingServerMigrations(int fromVersion) =>
        DatabaseMigrations.ServerPlan(fromVersion).Select(m => $"v{m.Version} : {m.Description}").ToList();

    /// <summary>La base n'existe pas encore (fichier absent, ou serveur sans les tables de l'application).</summary>
    public bool IsNewDatabase()
    {
        if (!IsServer) return !File.Exists(_target.FilePath);
        using var ctx = Create();
        if (!ctx.Database.CanConnect()) return true;
        return !ServerSql.HasAppTables(ctx);
    }

    /// <summary>Version actuelle de la base (0 = base antérieure au système de version ou vide).</summary>
    public int GetDatabaseVersion()
    {
        using var ctx = Create();
        return ReadVersion(ctx);
    }

    /// <summary>Version de l'application qui a mis la base à jour en dernier, pour les messages d'erreur.</summary>
    public string? GetLastMigratedByAppVersion()
    {
        using var ctx = Create();
        if (IsServer) return ServerSql.GetInfo(ctx, "AppVersion");
        if (!SchemaInspector.GetColumns(ctx, "DbInfo").Contains("Value")) return null;
        return ctx.Database.SqlQueryRaw<string>("""SELECT "Value" AS "Value" FROM "DbInfo" WHERE "Key" = 'AppVersion'""").FirstOrDefault();
    }

    /// <summary>Résultat du test de connexion à un serveur.</summary>
    public sealed record ServerTestResult(string ServerInfo, bool DatabaseExists, bool HasAppTables, bool? CanCreateDatabase);

    /// <summary>Test de connexion détaillé (écran de réglages) : lève une exception explicite en cas d'échec.
    /// Si la base demandée n'existe pas mais que le serveur répond, ce n'est pas une erreur : la base sera
    /// créée (si l'utilisateur en a le droit, indiqué quand le moteur permet de le savoir).</summary>
    public ServerTestResult TestConnection(int timeoutSeconds = 15)
    {
        if (!IsServer) throw new InvalidOperationException("Test réservé aux serveurs.");
        var server = _target.Server!.Clone();
        server.ConnectTimeoutSeconds = timeoutSeconds;
        using var ctx = new RelevDbContext(DbTarget.ForServer(server), false);
        var conn = ctx.Database.GetDbConnection();
        try
        {
            conn.Open();
        }
        catch (Exception ex) when (IsMissingDatabase(ex))
        {
            // Serveur joignable, base absente : on se connecte au niveau serveur pour le confirmer.
            var (info, canCreate) = ServerLevelConnect(server);
            return new ServerTestResult(info, false, false, canCreate);
        }
        try
        {
            return new ServerTestResult(conn.ServerVersion, true, ServerSql.HasAppTables(ctx), null);
        }
        finally
        {
            conn.Close();
        }
    }

    /// <summary>La connexion a échoué parce que la base demandée n'existe pas (et non parce que le serveur est
    /// injoignable ou que les identifiants sont refusés).</summary>
    internal static bool IsMissingDatabase(Exception ex) => ex switch
    {
        Npgsql.PostgresException pg => pg.SqlState == "3D000",
        MySqlConnector.MySqlException my => my.Number == 1049,
        Microsoft.Data.SqlClient.SqlException ms => ms.Number == 4060,
        _ => false
    };

    /// <summary>Connexion au serveur sans la base de l'application (base « postgres » / « master », ou aucune
    /// pour MariaDB) : retourne la version du serveur et, quand c'est vérifiable, le droit de créer une base.</summary>
    private static (string Info, bool? CanCreate) ServerLevelConnect(DbConnectionInfo server)
    {
        switch (server.Provider)
        {
            case DatabaseProvider.PostgreSql:
            {
                using var c = new Npgsql.NpgsqlConnection(server.BuildConnectionString("postgres"));
                c.Open();
                using var cmd = c.CreateCommand();
                cmd.CommandText = "SELECT rolcreatedb OR rolsuper FROM pg_roles WHERE rolname = current_user";
                return (c.ServerVersion, cmd.ExecuteScalar() as bool?);
            }
            case DatabaseProvider.SqlServer:
            {
                using var c = new Microsoft.Data.SqlClient.SqlConnection(server.BuildConnectionString("master"));
                c.Open();
                using var cmd = c.CreateCommand();
                cmd.CommandText = "SELECT CASE WHEN IS_SRVROLEMEMBER('sysadmin') = 1 OR IS_SRVROLEMEMBER('dbcreator') = 1 THEN 1 ELSE 0 END";
                return (c.ServerVersion, Convert.ToInt32(cmd.ExecuteScalar()) == 1);
            }
            default:
            {
                using var c = new MySqlConnector.MySqlConnection(server.BuildConnectionString(""));
                c.Open();
                return (c.ServerVersion, null);
            }
        }
    }

    /// <summary>Crée la base si besoin, sinon applique les migrations manquantes (sauvegarde préalable du
    /// fichier). Réservé au poste rédacteur (voir DbWriteLock).</summary>
    public void EnsureDatabaseUpToDate(string appVersion)
    {
        if (_readOnly) return;

        if (IsServer)
        {
            EnsureServerUpToDate(appVersion);
            return;
        }

        using var ctx = Create();
        if (ctx.Database.EnsureCreated())
        {
            // Base neuve : EnsureCreated vient de créer directement le schéma le plus récent.
            WriteVersion(ctx, LatestVersion, appVersion);
            EnsureDefaultSite(ctx);
            DatabaseWriteTracking.EnsureTriggers(ctx);
            return;
        }

        var current = ReadVersion(ctx);
        var pending = DatabaseMigrations.All.Where(m => m.Version > current).ToList();
        if (pending.Count == 0)
        {
            EnsureDefaultSite(ctx);
            DatabaseWriteTracking.EnsureTriggers(ctx);
            return;
        }

        Backup(ctx, current);
        var reached = current;
        foreach (var migration in pending)
        {
            using var transaction = ctx.Database.BeginTransaction();
            try
            {
                migration.Apply(ctx);
                WriteVersion(ctx, migration.Version, appVersion);
                transaction.Commit();
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"Échec de la mise à jour de la base vers la version {migration.Version} ({migration.Description}) : {ex.Message}\n" +
                    $"La base est restée en version {reached} ; une sauvegarde faite juste avant la mise à jour se trouve dans le dossier « Save DB » de RelevData.", ex);
            }
            reached = migration.Version;
            ctx.ChangeTracker.Clear();
        }

        EnsureDefaultSite(ctx);

        // Déclencheurs du suivi de la dernière saisie (après les migrations, pour que leurs réécritures de données
        // ne comptent pas comme une saisie).
        DatabaseWriteTracking.EnsureTriggers(ctx);
    }

    /// <summary>Serveur : crée la base et les tables au dernier schéma si elles n'existent pas, sinon applique
    /// les mises à jour du schéma propres aux serveurs (voir <see cref="DatabaseMigrations.ServerPlan"/>).</summary>
    private void EnsureServerUpToDate(string appVersion)
    {
        using var ctx = Create();
        var creator = ctx.GetService<IRelationalDatabaseCreator>();
        try
        {
            if (!creator.Exists()) creator.Create();
        }
        catch (Exception ex)
        {
            var inner = ex;
            while (inner.InnerException is not null) inner = inner.InnerException;
            throw new InvalidOperationException(
                $"La base « {_target.Server!.Database} » n'existe pas sur le serveur et n'a pas pu être créée : {inner.Message}\n" +
                "Créez-la sur le serveur, ou donnez à l'utilisateur le droit de créer des bases.", ex);
        }

        if (!ServerSql.HasAppTables(ctx))
        {
            creator.CreateTables();
            ServerSql.EnsureDbInfo(ctx);
            WriteVersion(ctx, LatestVersion, appVersion);
            EnsureDefaultSite(ctx);
            return;
        }

        ServerSql.EnsureDbInfo(ctx);
        var current = ReadVersion(ctx);
        var reached = current;
        foreach (var migration in DatabaseMigrations.ServerPlan(current))
        {
            try
            {
                migration.Apply(ctx);
                WriteVersion(ctx, migration.Version, appVersion);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"Échec de la mise à jour de la base du serveur vers la version {migration.Version} ({migration.Description}) : {ex.Message}\n" +
                    $"La base est restée en version {reached}.", ex);
            }
            reached = migration.Version;
            ctx.ChangeTracker.Clear();
        }
        if (reached < LatestVersion) WriteVersion(ctx, LatestVersion, appVersion);
        EnsureDefaultSite(ctx);
    }

    private int ReadVersion(RelevDbContext ctx)
    {
        if (!IsServer)
            return ctx.Database.SqlQueryRaw<int>("SELECT user_version AS \"Value\" FROM pragma_user_version").First();

        try
        {
            if (!ServerSql.HasAppTables(ctx)) return 0;
            var text = ServerSql.GetInfo(ctx, ServerSql.SchemaVersionKey);
            return int.TryParse(text, out var v) ? v : 0;
        }
        catch (System.Data.Common.DbException)
        {
            return 0; // table DbInfo absente
        }
    }

    private void WriteVersion(RelevDbContext ctx, int version, string appVersion)
    {
        if (IsServer)
        {
            ServerSql.SetInfo(ctx, ServerSql.SchemaVersionKey, version.ToString());
            ServerSql.SetInfo(ctx, "AppVersion", appVersion);
            return;
        }

        // PRAGMA n'accepte pas de paramètre ; version est un entier issu du code.
        ctx.Database.ExecuteSqlRaw("PRAGMA user_version = " + version + ";");
        ctx.Database.ExecuteSqlRaw("""CREATE TABLE IF NOT EXISTS "DbInfo" ("Key" TEXT NOT NULL PRIMARY KEY, "Value" TEXT NOT NULL);""");
        ctx.Database.ExecuteSqlRaw("""INSERT OR REPLACE INTO "DbInfo" ("Key", "Value") VALUES ('AppVersion', {0});""", appVersion);
    }

    /// <summary>Copie cohérente de la base (VACUUM INTO) avant toute migration, dans le dossier Save DB.</summary>
    private void Backup(RelevDbContext ctx, int fromVersion) => BackupTo(ctx, $"avant-maj-v{fromVersion}");

    /// <summary>Dossier des sauvegardes automatiques : RelevData\Save DB (créé au besoin).</summary>
    public static string BackupDirectory
    {
        get
        {
            var dir = Path.Combine(AppSettings.DataDirectoryPath, "Save DB");
            try { Directory.CreateDirectory(dir); } catch { /* dossier en lecture seule : l'erreur viendra à l'écriture */ }
            return dir;
        }
    }

    /// <summary>Copie cohérente de la base avant une opération destructive ; retourne le chemin de la copie.
    /// Toujours dans RelevData\Save DB : copie du fichier SQLite, ou fichier SQLite complet pour un serveur.</summary>
    public string CreateBackup(string reason)
    {
        Directory.CreateDirectory(BackupDirectory);
        if (IsServer)
        {
            var path = Path.Combine(BackupDirectory, $"serveur.{reason}-{DateTime.Now:yyyyMMdd-HHmmss}.bak");
            ExportTo(path);
            BackupService.Prune(BackupService.MaxToKeep);
            return path;
        }
        using var ctx = Create();
        return BackupTo(ctx, reason);
    }

    private string BackupTo(RelevDbContext ctx, string reason)
    {
        Directory.CreateDirectory(BackupDirectory);
        var backupPath = Path.Combine(BackupDirectory,
            $"{Path.GetFileName(_target.FilePath)}.{reason}-{DateTime.Now:yyyyMMdd-HHmmss}.bak");
        ctx.Database.ExecuteSqlRaw("VACUUM INTO {0};", backupPath);
        BackupService.Prune(BackupService.MaxToKeep);
        return backupPath;
    }

    /// <summary>Copie cohérente de la base vers un fichier SQLite choisi par l'utilisateur (bouton
    /// "Exporter la base de données", écran Paramètres). Sur un serveur, la copie est un fichier SQLite
    /// complet, réimportable par « Importer une sauvegarde ».</summary>
    public void ExportTo(string destinationPath)
    {
        if (IsServer)
        {
            if (File.Exists(destinationPath)) File.Delete(destinationPath);
            DatabaseCopier.Copy(_target, DbTarget.ForFile(destinationPath), appVersion: null, replaceDestination: true);
            return;
        }
        using var ctx = Create();
        ctx.Database.ExecuteSqlRaw("VACUUM INTO {0};", destinationPath);
    }
}
