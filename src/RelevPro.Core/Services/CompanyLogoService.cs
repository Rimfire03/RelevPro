using RelevPro.Core.Data;
using RelevPro.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace RelevPro.Core.Services;

/// <summary>Logo de l'entreprise, stocké dans la base (table SharedAssets) pour être commun à tous les
/// postes. Affiché dans la barre latérale et en tête des impressions et des PDF.</summary>
public static class CompanyLogoService
{
    private const string Key = "CompanyLogo";

    /// <summary>Taille maximale acceptée pour le fichier image.</summary>
    public const int MaxBytes = 2 * 1024 * 1024;

    public static byte[]? Get(RelevDbContext ctx) =>
        ctx.SharedAssets.AsNoTracking().Where(a => a.Key == Key).Select(a => a.Data).FirstOrDefault();

    public static void Set(RelevDbContext ctx, byte[] data)
    {
        var existing = ctx.SharedAssets.FirstOrDefault(a => a.Key == Key);
        if (existing is null) ctx.SharedAssets.Add(new SharedAsset { Key = Key, Data = data });
        else existing.Data = data;
        ctx.SaveChanges();
    }

    public static void Remove(RelevDbContext ctx)
    {
        var existing = ctx.SharedAssets.FirstOrDefault(a => a.Key == Key);
        if (existing is null) return;
        ctx.SharedAssets.Remove(existing);
        ctx.SaveChanges();
    }
}
