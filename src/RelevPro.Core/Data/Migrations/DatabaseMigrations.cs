using Microsoft.EntityFrameworkCore;

namespace RelevPro.Core.Data.Migrations;

/// <summary>Étapes de mise à jour du schéma, appliquées par <see cref="Services.DbContextFactory.EnsureDatabaseUpToDate"/>.
/// Règles : ne jamais modifier ni renuméroter une migration publiée, toujours en ajouter une à la fin (SQL brut), et
/// pour chaque migration ajouter l'équivalent portable dans <see cref="ServerMigrations"/> (corps vide si sans effet).
/// La version 1 est le schéma créé directement par EF Core sur une base neuve.</summary>
internal static class DatabaseMigrations
{
    internal static readonly (int Version, string Description, Action<RelevDbContext> Apply)[] All =
    {
        (1, "Schéma initial (compteurs, vues, relevés, tâches)", _ => { }),
        (2, "Vues : famille et récurrence", AddViewColumns),
    };

    /// <summary>Colonnes ajoutées en v2 (idempotent hors SQLite) : famille et récurrence des vues, groupe des lignes,
    /// seuil d'alarme et type des compteurs. Recurrence 0 = hebdomadaire : toute ligne existante reste valide dans sa vue.</summary>
    private static void AddViewColumns(RelevDbContext db)
    {
        Add(db, "ReadingViews", "Famille", "TEXT NOT NULL DEFAULT ''", "varchar(200) NOT NULL DEFAULT ''", "varchar(200) NOT NULL DEFAULT ''", "nvarchar(200) NOT NULL DEFAULT ''");
        Add(db, "ReadingViews", "Recurrence", "INTEGER NOT NULL DEFAULT 0", "integer NOT NULL DEFAULT 0", "int NOT NULL DEFAULT 0", "int NOT NULL DEFAULT 0");
        Add(db, "ViewLines", "Groupe", "TEXT NOT NULL DEFAULT ''", "varchar(200) NOT NULL DEFAULT ''", "varchar(200) NOT NULL DEFAULT ''", "nvarchar(200) NOT NULL DEFAULT ''");
        Add(db, "Meters", "SeuilHaut", "TEXT NOT NULL DEFAULT '0'", "numeric(18,4) NOT NULL DEFAULT 0", "decimal(18,4) NOT NULL DEFAULT 0", "decimal(18,4) NOT NULL DEFAULT 0");
        Add(db, "Meters", "Cumulatif", "INTEGER NOT NULL DEFAULT 1", "boolean NOT NULL DEFAULT true", "tinyint(1) NOT NULL DEFAULT 1", "bit NOT NULL DEFAULT 1");
    }

    private static void Add(RelevDbContext db, string table, string col, string sqlite, string pg, string maria, string mssql)
    {
        var p = db.Provider;
        if (p != Services.DatabaseProvider.Sqlite && ServerSql.ColumnExists(db, table, col)) return;
        var (type, add) = p switch
        {
            Services.DatabaseProvider.Sqlite => (sqlite, "ADD COLUMN"),
            Services.DatabaseProvider.PostgreSql => (pg, "ADD COLUMN"),
            Services.DatabaseProvider.SqlServer => (mssql, "ADD"),
            _ => (maria, "ADD COLUMN")
        };
        db.Database.ExecuteSqlRaw($"ALTER TABLE {ServerSql.Q(p, table)} {add} {ServerSql.Q(p, col)} {type}");
    }

    /// <summary>Dernière version déjà contenue dans le schéma créé par EF Core sur un serveur.</summary>
    internal const int ServerBaselineVersion = 1;

    internal static readonly (int Version, string Description, Action<RelevDbContext> Apply)[] ServerMigrations =
    {
        (2, "Vues : famille et récurrence", AddViewColumns),
    };

    internal static IReadOnlyList<(int Version, string Description, Action<RelevDbContext> Apply)> ServerPlan(int fromVersion)
    {
        var plan = new List<(int Version, string Description, Action<RelevDbContext> Apply)>();
        foreach (var m in All.Where(m => m.Version > Math.Max(fromVersion, ServerBaselineVersion)))
        {
            var server = ServerMigrations.Where(s => s.Version == m.Version).ToList();
            if (server.Count == 0)
                throw new InvalidOperationException(
                    $"Mise à jour de base v{m.Version} ({m.Description}) sans équivalent pour les serveurs (DatabaseMigrations.ServerMigrations).");
            plan.Add(server[0]);
        }
        return plan;
    }
}
