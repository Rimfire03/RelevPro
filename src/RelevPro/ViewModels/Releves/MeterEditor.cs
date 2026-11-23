using RelevPro.Core.Models;
using RelevPro.Core.Services;
using RelevPro.Services;

namespace RelevPro.ViewModels.Releves;

/// <summary>Dialogue de saisie d'un compteur. Chaque compteur appartient à une seule vue de relevé (créé depuis la vue).</summary>
internal static class MeterEditor
{
    /// <summary>Liste déroulante : « (aucune) » puis le catalogue (plus la valeur actuelle si elle n'y figure plus).</summary>
    private static List<string> Choices(CatalogKind kind, string current)
    {
        var l = new List<string> { "(aucune)" };
        l.AddRange(CatalogService.Names(App.Db, kind));
        if (current.Length > 0 && !l.Contains(current)) l.Add(current);
        return l;
    }

    /// <summary>Champs du compteur ; <paramref name="extra"/> ajoute des champs propres à l'appelant (récurrence, section).
    /// Renvoie faux si annulé : le compteur n'est alors pas modifié.</summary>
    public static bool Edit(string title, Meter m, IEnumerable<EditField>? extra = null)
    {
        var nom = m.Nom; var uniteList = Choices(CatalogKind.Unite, m.Unite); var unite = Math.Max(0, uniteList.IndexOf(m.Unite)); var famList = Choices(CatalogKind.Famille, m.Famille); var fam = Math.Max(0, famList.IndexOf(m.Famille)); var seuil = m.SeuilEcartPct; var haut = m.SeuilHaut; var etat = m.Arrete ? 1 : 0; var cap = m.Capacite; var type = m.Cumulatif ? 0 : 1;
        var fields = new List<EditField>
        {
            EditField.Text("Nom du compteur", () => nom, v => nom = v, required: true),
        };
        if (extra is not null) fields.AddRange(extra);
        fields.AddRange(new[]
        {
            EditField.ComboField("État", new List<string> { "En service", "Arrêté / ignoré (plus de saisie ni de notification, historique conservé)" }, () => etat, v => etat = v),
            EditField.DecimalField("Capacité (valeur avant retour à 0, ex. 100000 ; 0 = automatique)", () => cap, v => cap = v),
            EditField.ComboField("Famille", famList, () => fam, v => fam = v),
            EditField.ComboField("Unité", uniteList, () => unite, v => unite = v),
            EditField.ComboField("Type", new List<string> { "Compteur cumulatif (index qui augmente, consommation calculée)", "Mesure ou niveau (peut baisser, pas de consommation)" }, () => type, v => type = v),
            EditField.DecimalField("Seuil d'alarme haut (augmentation max entre 2 relevés, ou valeur max d'une mesure ; 0 = aucun)", () => haut, v => haut = v),
            EditField.IntField("Écart toléré (% au-dessus de l'habituel avant alerte)", () => seuil, v => seuil = v)
        });
        if (!App.Dialogs.EditFields(title, fields) || string.IsNullOrWhiteSpace(nom)) return false;
        m.Nom = nom.Trim(); m.Unite = unite <= 0 ? "" : uniteList[unite]; m.Famille = fam <= 0 ? "" : famList[fam]; m.SeuilEcartPct = Math.Max(0, seuil); m.SeuilHaut = Math.Max(0, haut); m.Cumulatif = type == 0; m.Arrete = etat == 1; m.Capacite = Math.Max(0, cap);
        return true;
    }
}
