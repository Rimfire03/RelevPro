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
    };

    /// <summary>Dernière version déjà contenue dans le schéma créé par EF Core sur un serveur.</summary>
    internal const int ServerBaselineVersion = 1;

    internal static readonly (int Version, string Description, Action<RelevDbContext> Apply)[] ServerMigrations =
    {
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
