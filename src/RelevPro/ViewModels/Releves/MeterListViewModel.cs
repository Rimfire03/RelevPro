using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using RelevPro.Core.Models;
using RelevPro.Core.Services;
using RelevPro.Views.Dialogs;
using RelevPro.Services;

namespace RelevPro.ViewModels.Releves;

public sealed record MeterRow(Meter Meter, int Views, int Readings)
{
    public string Nom => Meter.Nom;
    public string Unite => Meter.Unite;
    public string Famille => Meter.Famille;
    public string Type => Meter.Cumulatif ? "Cumulatif" : "Mesure / niveau";
    public string Seuil => Meter.SeuilHaut > 0 ? $"Alarme > {Meter.SeuilHaut:0.##}" : "-";
    public string Usage => $"{Views} vue(s), {Readings} relevé(s)";
}

/// <summary>Menu « Compteurs » : création, modification et suppression des compteurs (partagés entre les vues).</summary>
public partial class MeterListViewModel : EditableScreenViewModel
{
    private readonly MainViewModel _main;
    public ObservableCollection<MeterRow> Rows { get; } = new();

    public MeterListViewModel(MainViewModel main)
    {
        _main = main;
        CatalogService.EnsureDefaults(App.Db);
        Load();
    }

    private void Load()
    {
        Rows.Clear();
        var views = App.Db.ViewLines.AsNoTracking().Where(l => l.MeterId != null).Select(l => l.MeterId!.Value).ToList().ToLookup(i => i);
        var readings = App.Db.MeterReadings.AsNoTracking().Select(r => r.MeterId).ToList().ToLookup(i => i);
        foreach (var m in App.Db.Meters.AsNoTracking().OrderBy(m => m.Famille).ThenBy(m => m.Ordre).ThenBy(m => m.Nom).ToList())
            Rows.Add(new MeterRow(m, views[m.Id].Count(), readings[m.Id].Count()));
    }

    /// <summary>Liste déroulante : « (aucune) » puis le catalogue (plus la valeur actuelle si elle n'y figure plus).</summary>
    private static List<string> Choices(CatalogKind kind, string current)
    {
        var l = new List<string> { "(aucune)" };
        l.AddRange(CatalogService.Names(App.Db, kind));
        if (current.Length > 0 && !l.Contains(current)) l.Add(current);
        return l;
    }

    [RelayCommand] private void ManageUnits() { if (!GuardEdit()) return; new CatalogWindow(CatalogKind.Unite, "Catalogue des unités") { Owner = System.Windows.Application.Current.MainWindow }.ShowDialog(); Load(); }
    [RelayCommand] private void ManageFamilies() { if (!GuardEdit()) return; new CatalogWindow(CatalogKind.Famille, "Familles de compteurs") { Owner = System.Windows.Application.Current.MainWindow }.ShowDialog(); Load(); }

    private static bool Edit(string title, Meter m)
    {
        var nom = m.Nom; var uniteList = Choices(CatalogKind.Unite, m.Unite); var unite = Math.Max(0, uniteList.IndexOf(m.Unite)); var famList = Choices(CatalogKind.Famille, m.Famille); var fam = Math.Max(0, famList.IndexOf(m.Famille)); var seuil = m.SeuilEcartPct; var haut = m.SeuilHaut; var type = m.Cumulatif ? 0 : 1;
        var ok = App.Dialogs.EditFields(title, new List<EditField>
        {
            EditField.Text("Nom du compteur", () => nom, v => nom = v, required: true),
            EditField.ComboField("Famille", famList, () => fam, v => fam = v),
            EditField.ComboField("Unité", uniteList, () => unite, v => unite = v),
            EditField.ComboField("Type", new List<string> { "Compteur cumulatif (index qui augmente, consommation calculée)", "Mesure ou niveau (peut baisser, pas de consommation)" }, () => type, v => type = v),
            EditField.DecimalField("Seuil d'alarme haut (augmentation max entre 2 relevés, ou valeur max d'une mesure ; 0 = aucun)", () => haut, v => haut = v),
            EditField.IntField("Écart toléré (% au-dessus de l'habituel avant alerte)", () => seuil, v => seuil = v)
        });
        if (!ok || string.IsNullOrWhiteSpace(nom)) return false;
        m.Nom = nom.Trim(); m.Unite = unite <= 0 ? "" : uniteList[unite]; m.Famille = fam <= 0 ? "" : famList[fam]; m.SeuilEcartPct = Math.Max(0, seuil); m.SeuilHaut = Math.Max(0, haut); m.Cumulatif = type == 0;
        return true;
    }

    [RelayCommand]
    private void Add()
    {
        if (!App.GuardWritable() || !GuardEdit()) return;
        var m = new Meter { Ordre = (App.Db.Meters.Max(x => (int?)x.Ordre) ?? -1) + 1 };
        if (!Edit("Nouveau compteur", m)) return;
        App.Db.Meters.Add(m);
        App.Db.SaveChanges();
        Load();
    }

    [RelayCommand]
    private void EditMeter(MeterRow row)
    {
        if (!App.GuardWritable() || !GuardEdit()) return;
        var m = App.Db.Meters.First(x => x.Id == row.Meter.Id);
        if (!Edit("Modifier le compteur", m)) { App.Db.Entry(m).Reload(); return; }
        App.Db.SaveChanges();
        Load();
    }

    [RelayCommand]
    private void Delete(MeterRow row)
    {
        if (!App.GuardWritable() || !GuardEdit()) return;
        if (!App.Dialogs.ShowConfirm("Supprimer le compteur",
                $"Supprimer « {row.Nom} » ? Ses {row.Readings} relevé(s) et ses lignes dans {row.Views} vue(s) seront supprimés définitivement."))
            return;
        App.Db.Meters.Remove(App.Db.Meters.First(x => x.Id == row.Meter.Id));
        App.Db.SaveChanges();
        Load();
        _main.RebuildViewItems();
        _main.RefreshNotificationBadge();
    }
}
