using Microsoft.EntityFrameworkCore;
using RelevPro.Core.Models;
using RelevPro.Views.Dialogs;

namespace RelevPro;

public partial class App
{
    /// <summary>Choix de l'assistant de première configuration, appliqués une fois la base créée et le site choisi.</summary>
    private static FirstRunResult? PendingFirstRun { get; set; }

    /// <summary>Nom du premier site (la base neuve contient un site par défaut). À appeler avant le choix du site.</summary>
    private static void ApplyFirstRunSite()
    {
        if (PendingFirstRun is null) return;
        var site = Db.Sites.OrderBy(s => s.Id).FirstOrDefault();
        if (site is null) return;
        site.Nom = PendingFirstRun.SiteName;
        Db.SaveChanges();
    }

    /// <summary>Première vue de relevé demandée par l'assistant (le site courant est déjà choisi).</summary>
    private static void ApplyFirstRunView()
    {
        var r = PendingFirstRun;
        PendingFirstRun = null;
        if (r is null || r.ViewName.Length == 0) return;
        Db.ReadingViews.Add(new ReadingView { Nom = r.ViewName, Famille = r.ViewFamily, Recurrence = r.ViewRecurrence });
        Db.SaveChanges();
    }
}
