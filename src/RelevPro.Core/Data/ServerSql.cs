using RelevPro.Core.Services;
using Microsoft.EntityFrameworkCore;

namespace RelevPro.Core.Data;

/// <summary>Petit SQL portable pour les tables techniques hors modèle EF (<c>DbInfo</c> : version du schéma,
/// version du logiciel, dernière saisie) sur les serveurs PostgreSQL, MariaDB/MySQL et SQL Server. Les noms
/// sont toujours des constantes du code (jamais saisis par l'utilisateur) ; les valeurs passent en paramètres.</summary>
internal static class ServerSql
{
    public const string SchemaVersionKey = "SchemaVersion";

    public static string Q(DatabaseProvider p, string ident) => p switch
    {
        DatabaseProvider.MariaDb => $"`{ident}`",
        DatabaseProvider.SqlServer => $"[{ident}]",
        _ => $"\"{ident}\""
    };

    public static void EnsureDbInfo(RelevDbContext ctx)
    {
        var p = ctx.Provider;
        var sql = p switch
        {
            DatabaseProvider.PostgreSql => """CREATE TABLE IF NOT EXISTS "DbInfo" ("Key" varchar(100) NOT NULL PRIMARY KEY, "Value" text NOT NULL);""",
            DatabaseProvider.MariaDb => "CREATE TABLE IF NOT EXISTS `DbInfo` (`Key` varchar(100) NOT NULL PRIMARY KEY, `Value` longtext NOT NULL);",
            DatabaseProvider.SqlServer => "IF OBJECT_ID(N'DbInfo', N'U') IS NULL CREATE TABLE [DbInfo] ([Key] nvarchar(100) NOT NULL PRIMARY KEY, [Value] nvarchar(max) NOT NULL);",
            DatabaseProvider.Sqlite => """CREATE TABLE IF NOT EXISTS "DbInfo" ("Key" TEXT NOT NULL PRIMARY KEY, "Value" TEXT NOT NULL);""",
            _ => throw new NotSupportedException()
        };
        ctx.Database.ExecuteSqlRaw(sql);
    }

    public static string? GetInfo(RelevDbContext ctx, string key)
    {
        var p = ctx.Provider;
        var sql = $"SELECT {Q(p, "Value")} AS {Q(p, "Value")} FROM {Q(p, "DbInfo")} WHERE {Q(p, "Key")} = {{0}}";
        return ctx.Database.SqlQueryRaw<string>(sql, key).AsEnumerable().FirstOrDefault();
    }

    public static void SetInfo(RelevDbContext ctx, string key, string value)
    {
        var p = ctx.Provider;
        var table = Q(p, "DbInfo");
        var updated = ctx.Database.ExecuteSqlRaw(
            $"UPDATE {table} SET {Q(p, "Value")} = {{0}} WHERE {Q(p, "Key")} = {{1}}", value, key);
        if (updated == 0)
            ctx.Database.ExecuteSqlRaw(
                $"INSERT INTO {table} ({Q(p, "Key")}, {Q(p, "Value")}) VALUES ({{0}}, {{1}})", key, value);
    }

    /// <summary>La colonne existe-t-elle dans la table (schéma courant) ?</summary>
    public static bool ColumnExists(RelevDbContext ctx, string table, string column)
    {
        var sql = ctx.Provider switch
        {
            DatabaseProvider.PostgreSql => """SELECT count(*)::int AS "Value" FROM information_schema.columns WHERE table_schema = current_schema() AND table_name = {0} AND column_name = {1}""",
            DatabaseProvider.MariaDb => "SELECT CAST(count(*) AS SIGNED) AS `Value` FROM information_schema.columns WHERE table_schema = DATABASE() AND table_name = {0} AND column_name = {1}",
            DatabaseProvider.SqlServer => "SELECT CAST(count(*) AS int) AS [Value] FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = {0} AND COLUMN_NAME = {1}",
            _ => throw new NotSupportedException()
        };
        return ctx.Database.SqlQueryRaw<int>(sql, table, column).AsEnumerable().First() > 0;
    }

    /// <summary>La base du serveur contient-elle déjà les tables de l'application ?</summary>
    public static bool HasAppTables(RelevDbContext ctx)
    {
        var p = ctx.Provider;
        var sql = p switch
        {
            DatabaseProvider.PostgreSql => """SELECT count(*)::int AS "Value" FROM information_schema.tables WHERE table_schema = current_schema() AND table_name = 'Meters'""",
            DatabaseProvider.MariaDb => "SELECT CAST(count(*) AS SIGNED) AS `Value` FROM information_schema.tables WHERE table_schema = DATABASE() AND table_name = 'Meters'",
            DatabaseProvider.SqlServer => "SELECT CAST(count(*) AS int) AS [Value] FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'Meters'",
            _ => throw new NotSupportedException()
        };
        return ctx.Database.SqlQueryRaw<int>(sql).AsEnumerable().First() > 0;
    }
}
