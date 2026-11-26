using System.IO;
using System.Text.Json;

namespace RelevPro.Services;

/// <summary>Colonnes masquées de la grille de saisie, mémorisées par vue et par utilisateur Windows
/// (%LOCALAPPDATA%\RelevPro\columns.json : non partagé avec les autres utilisateurs du poste ni de la base).</summary>
internal static class ColumnPrefs
{
    private static string FilePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RelevPro", "columns.json");

    private static Dictionary<int, List<string>> Read()
    {
        try { if (File.Exists(FilePath)) return JsonSerializer.Deserialize<Dictionary<int, List<string>>>(File.ReadAllText(FilePath)) ?? new(); }
        catch { /* fichier illisible : on repart de zéro */ }
        return new();
    }

    public static HashSet<string> Hidden(int viewId) => Read().TryGetValue(viewId, out var l) ? l.ToHashSet() : new();

    public static void Save(int viewId, IEnumerable<string> hidden)
    {
        try
        {
            var all = Read();
            var list = hidden.ToList();
            if (list.Count == 0) all.Remove(viewId); else all[viewId] = list;
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(all));
        }
        catch { /* dossier en lecture seule : le choix n'est alors pas mémorisé */ }
    }
}
