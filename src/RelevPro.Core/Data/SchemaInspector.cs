using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace RelevPro.Core.Data;

/// <summary>Lecture de la structure réelle d'une base SQLite (colonnes existantes), pour les migrations
/// idempotentes.</summary>
internal static class SchemaInspector
{
    public static HashSet<string> GetColumns(RelevDbContext ctx, string table)
    {
        var cols = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var conn = ctx.Database.GetDbConnection();
        var mustClose = conn.State != System.Data.ConnectionState.Open;
        if (mustClose) conn.Open();
        try
        {
            using var cmd = conn.CreateCommand();
            cmd.Transaction = ctx.Database.CurrentTransaction?.GetDbTransaction();
            cmd.CommandText = $"PRAGMA table_info(\"{table}\")";
            using var reader = cmd.ExecuteReader();
            while (reader.Read()) cols.Add(reader.GetString(1));
        }
        finally
        {
            if (mustClose) conn.Close();
        }
        return cols;
    }
}
