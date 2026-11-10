using Microsoft.EntityFrameworkCore;
using RelevPro.Core.Data;
using RelevPro.Core.Models;

namespace RelevPro.Core.Services;

/// <summary>Catalogues modifiables (unités, familles de compteurs) alimentant les listes déroulantes.</summary>
public static class CatalogService
{
    private static readonly string[] DefaultUnits = { "m³", "L", "kWh", "MWh", "h", "°C", "bar", "%", "mg/L", "u" };
    private static readonly string[] DefaultFamilies = { "Électricité", "Plomberie", "Chauffage", "Gaz", "Climatisation", "Traitement de l'eau" };

    public static List<string> Names(RelevDbContext db, CatalogKind kind) =>
        db.CatalogItems.AsNoTracking().Where(c => c.Kind == kind).OrderBy(c => c.Ordre).ThenBy(c => c.Nom).Select(c => c.Nom).ToList();

    /// <summary>Remplit un catalogue vide avec des valeurs usuelles, et y ajoute les unités / familles déjà saisies sur des compteurs.</summary>
    public static void EnsureDefaults(RelevDbContext db)
    {
        var changed = false;
        foreach (var (kind, defaults) in new[] { (CatalogKind.Unite, DefaultUnits), (CatalogKind.Famille, DefaultFamilies) })
        {
            var existing = db.CatalogItems.Where(c => c.Kind == kind).ToList();
            var next = existing.Count == 0 ? 0 : existing.Max(c => c.Ordre) + 1;
            var names = existing.Select(c => c.Nom).ToHashSet(StringComparer.CurrentCultureIgnoreCase);
            var wanted = existing.Count == 0 ? defaults.AsEnumerable() : Enumerable.Empty<string>();
            var used = db.Meters.AsNoTracking().Select(m => kind == CatalogKind.Unite ? m.Unite : m.Famille).Distinct().ToList();
            foreach (var n in wanted.Concat(used).Select(x => x.Trim()).Where(x => x.Length > 0))
                if (names.Add(n)) { db.CatalogItems.Add(new CatalogItem { Kind = kind, Nom = n, Ordre = next++ }); changed = true; }
        }
        if (changed) db.SaveChanges();
    }

    /// <summary>Ajoute une entrée (sans doublon). Retourne false si le nom est vide ou déjà présent.</summary>
    public static bool Add(RelevDbContext db, CatalogKind kind, string nom)
    {
        nom = nom.Trim();
        if (nom.Length == 0 || db.CatalogItems.AsEnumerable().Any(c => c.Kind == kind && string.Equals(c.Nom, nom, StringComparison.CurrentCultureIgnoreCase))) return false;
        var order = db.CatalogItems.Where(c => c.Kind == kind).Max(c => (int?)c.Ordre) ?? -1;
        db.CatalogItems.Add(new CatalogItem { Kind = kind, Nom = nom, Ordre = order + 1 });
        db.SaveChanges();
        return true;
    }

    /// <summary>Renomme une entrée et met à jour les compteurs qui l'utilisent.</summary>
    public static bool Rename(RelevDbContext db, CatalogKind kind, string oldName, string newName)
    {
        newName = newName.Trim();
        if (newName.Length == 0) return false;
        var item = db.CatalogItems.AsEnumerable().FirstOrDefault(c => c.Kind == kind && c.Nom == oldName);
        if (item is null) return false;
        if (!string.Equals(oldName, newName, StringComparison.CurrentCultureIgnoreCase)
            && db.CatalogItems.AsEnumerable().Any(c => c.Kind == kind && string.Equals(c.Nom, newName, StringComparison.CurrentCultureIgnoreCase))) return false;
        item.Nom = newName;
        foreach (var m in db.Meters.ToList())
        {
            if (kind == CatalogKind.Unite && m.Unite == oldName) m.Unite = newName;
            if (kind == CatalogKind.Famille && m.Famille == oldName) m.Famille = newName;
        }
        db.SaveChanges();
        return true;
    }

    /// <summary>Nombre de compteurs qui utilisent cette entrée.</summary>
    public static int Usage(RelevDbContext db, CatalogKind kind, string nom) =>
        db.Meters.AsNoTracking().AsEnumerable().Count(m => (kind == CatalogKind.Unite ? m.Unite : m.Famille) == nom);

    /// <summary>Supprime une entrée ; les compteurs qui l'utilisent perdent cette valeur.</summary>
    public static void Delete(RelevDbContext db, CatalogKind kind, string nom)
    {
        var item = db.CatalogItems.AsEnumerable().FirstOrDefault(c => c.Kind == kind && c.Nom == nom);
        if (item is null) return;
        foreach (var m in db.Meters.ToList())
        {
            if (kind == CatalogKind.Unite && m.Unite == nom) m.Unite = string.Empty;
            if (kind == CatalogKind.Famille && m.Famille == nom) m.Famille = string.Empty;
        }
        db.CatalogItems.Remove(item);
        db.SaveChanges();
    }
}
