using System.Text.Json;
using System.Text.Json.Serialization;

namespace RelevPro.Core.Services;

public class AppSettings
{
    // Chemins par défaut relatifs à RelevData (voir DataDirectory) : seul l'exécutable doit rester à la
    // racine du dossier publié, tout le reste (base de données, exports, réglages) va dans RelevData.
    public string DatabasePath { get; set; } = "relevpro.db";
    public string ExportPath { get; set; } = "exports";

    public bool AutoUpdateEnabled { get; set; } = true;

    /// <summary>Canal de mise à jour : "Main" (dernière release stable) ou "Dev" (dernière release, pré-versions
    /// comprises).</summary>
    public string UpdateChannel { get; set; } = UpdateChannels.Main;

    /// <summary>Version du logiciel au dernier démarrage : si elle change, la base est sauvegardée (Save DB) avant\r\n    /// d'être ouverte par la nouvelle version.</summary>
    public string? LastRunVersion { get; set; }

    // Sauvegarde automatique planifiée (voir BackupSchedule) et nombre de sauvegardes conservées (0 = illimité).
    public bool AutoBackupEnabled { get; set; }
    public string AutoBackupFrequency { get; set; } = BackupSchedule.Daily;
    public string AutoBackupTime { get; set; } = "02:00";
    public int AutoBackupDayOfWeek { get; set; } = (int)DayOfWeek.Monday;
    public int AutoBackupDayOfMonth { get; set; } = 1;
    public int MaxBackupsToKeep { get; set; } = 30;
    public DateTime? AutoBackupLastRun { get; set; }

    /// <summary>Noms des sauvegardes (Save DB) protégées contre la suppression automatique.</summary>
    public List<string> ProtectedBackups { get; set; } = new();


    /// <summary>Module « MultiSite » (nécessite la fonctionnalité de licence « multisite ») : plusieurs sites aux données séparées, seuls les Paramètres sont communs.</summary>
    public bool MultiSiteEnabled { get; set; } = false;

    /// <summary>Site ouvert automatiquement au démarrage (null = demander le site à chaque ouverture).</summary>
    public int? DefaultSiteId { get; set; }

    /// <summary>Dernier site ouvert : sert de site actif quand le module n'est plus utilisable (désactivé ou hors licence).</summary>
    public int? LastSiteId { get; set; }


    /// <summary>Dossier contenant tout ce qui n'est pas l'exécutable lui-même (base de données, exports,
    /// réglages) : seul l'exécutable doit rester à la racine du dossier publié.</summary>
    private static string DataDirectory => Path.Combine(AppContext.BaseDirectory, "RelevData");

    /// <summary>Dossier des données locales (réglages, sauvegardes de serveur).</summary>
    public static string DataDirectoryPath => DataDirectory;

    /// <summary>Utiliser un serveur de base de données (réglages dans <see cref="DatabaseServer"/>) au lieu du
    /// fichier SQLite <see cref="DatabasePath"/>. Le fichier n'est jamais supprimé en passant sur un serveur.</summary>
    public bool UseDatabaseServer { get; set; }

    /// <summary>Dernier réglage de serveur saisi (conservé même si on revient au fichier).</summary>
    public DbConnectionInfo DatabaseServer { get; set; } = new();

    /// <summary>Base à ouvrir selon les réglages.</summary>
    [JsonIgnore]
    public DbTarget ResolvedTarget =>
        UseDatabaseServer ? DbTarget.ForServer(DatabaseServer) : DbTarget.ForFile(ResolvedDatabasePath);

    private static string SettingsFilePath => Path.Combine(DataDirectory, "settings.json");

    public static AppSettings Load()
    {
        MigrateLegacyLayoutIfNeeded();

        try
        {
            if (File.Exists(SettingsFilePath))
            {
                var json = File.ReadAllText(SettingsFilePath);
                var settings = JsonSerializer.Deserialize<AppSettings>(json);
                if (settings != null) return settings;
            }
        }
        catch
        {
            // fichier corrompu ou illisible : on repart sur les valeurs par défaut
        }

        var defaults = new AppSettings();
        defaults.Save();
        return defaults;
    }

    public void Save()
    {
        Directory.CreateDirectory(DataDirectory);
        var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(SettingsFilePath, json);
    }

    /// <summary>Chemin absolu de la base de données, résolu par rapport à <see cref="DataDirectory"/>
    /// si un chemin relatif a été configuré (mode portable) - inchangé si un chemin absolu a été choisi
    /// (ex. réseau, via "Parcourir...").</summary>
    [JsonIgnore]
    public string ResolvedDatabasePath =>
        Path.IsPathRooted(DatabasePath) ? DatabasePath : Path.Combine(DataDirectory, DatabasePath);

    [JsonIgnore]
    public string ResolvedExportPath =>
        Path.IsPathRooted(ExportPath) ? ExportPath : Path.Combine(DataDirectory, ExportPath);

    /// <summary>Migration ponctuelle depuis l'ancienne disposition (settings.json, data\relevpro.db,
    /// data\exports à côté de l'exécutable) vers RelevData : ne fait rien si RelevData existe déjà
    /// (déjà migré, ou installation neuve qui le créera directement) ou si aucun settings.json
    /// n'existe à l'ancien emplacement (rien à migrer). Ne déplace que les chemins relatifs (mode
    /// portable) : un chemin absolu choisi par l'utilisateur (ex. réseau) n'est jamais touché, ni le
    /// fichier qu'il désigne. Ne doit jamais empêcher le démarrage : toute erreur est avalée, au pire
    /// l'ancienne disposition reste utilisée et de nouvelles valeurs par défaut repartent à zéro.</summary>
    private static void MigrateLegacyLayoutIfNeeded()
    {
        var dataDir = DataDirectory;
        if (Directory.Exists(dataDir)) return;

        var legacySettingsPath = Path.Combine(AppContext.BaseDirectory, "settings.json");
        if (!File.Exists(legacySettingsPath)) return;

        try
        {
            Directory.CreateDirectory(dataDir);

            AppSettings legacy;
            try { legacy = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(legacySettingsPath)) ?? new AppSettings(); }
            catch { legacy = new AppSettings(); }

            legacy.DatabasePath = MigrateRelativeFile(legacy.DatabasePath, dataDir);
            legacy.ExportPath = MigrateRelativeDirectory(legacy.ExportPath, dataDir);

            var json = JsonSerializer.Serialize(legacy, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(Path.Combine(dataDir, "settings.json"), json);
            File.Delete(legacySettingsPath);
        }
        catch
        {
            // Migration best-effort : voir le résumé ci-dessus.
        }
    }

    /// <summary>Déplace le fichier désigné par un chemin relatif (ex. "data\relevpro.db") dans
    /// <paramref name="dataDir"/>, avec ses éventuelles sauvegardes ".bak" à côté, et retourne le
    /// nouveau chemin relatif (juste le nom de fichier, RelevData servant déjà de dossier dédié).
    /// Ne touche à rien pour un chemin déjà absolu.</summary>
    private static string MigrateRelativeFile(string configuredPath, string dataDir)
    {
        if (string.IsNullOrWhiteSpace(configuredPath) || Path.IsPathRooted(configuredPath)) return configuredPath;

        var oldFullPath = Path.Combine(AppContext.BaseDirectory, configuredPath);
        var fileName = Path.GetFileName(configuredPath);
        if (string.IsNullOrEmpty(fileName)) return configuredPath;
        var newFullPath = Path.Combine(dataDir, fileName);

        if (File.Exists(oldFullPath) && !File.Exists(newFullPath))
        {
            File.Move(oldFullPath, newFullPath);
            var oldDir = Path.GetDirectoryName(oldFullPath);
            if (oldDir is not null && Directory.Exists(oldDir))
            {
                foreach (var bak in Directory.GetFiles(oldDir, fileName + ".*.bak"))
                    File.Move(bak, Path.Combine(dataDir, Path.GetFileName(bak)));
            }
        }
        return fileName;
    }

    /// <summary>Même principe que <see cref="MigrateRelativeFile"/> pour un dossier (ex. "data\exports").</summary>
    private static string MigrateRelativeDirectory(string configuredPath, string dataDir)
    {
        if (string.IsNullOrWhiteSpace(configuredPath) || Path.IsPathRooted(configuredPath)) return configuredPath;

        var oldFullPath = Path.Combine(AppContext.BaseDirectory, configuredPath);
        var name = new DirectoryInfo(configuredPath).Name;
        if (string.IsNullOrEmpty(name)) return configuredPath;
        var newFullPath = Path.Combine(dataDir, name);

        if (Directory.Exists(oldFullPath) && !Directory.Exists(newFullPath))
            Directory.Move(oldFullPath, newFullPath);
        return name;
    }
}
