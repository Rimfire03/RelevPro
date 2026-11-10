using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using RelevPro.Core.Models;
using RelevPro.Core.Services;
using RelevPro.Services;

namespace RelevPro.ViewModels.Releves;

public sealed record ViewRow(ReadingView View, int MeterLines, int TaskLines)
{
    public string Nom => View.Nom;
    public string Famille => View.Famille;
    public string RecurrenceName => Periods.Name(View.Recurrence);
    public string Summary => $"{MeterLines} compteur(s), {TaskLines} tâche(s)";
}

/// <summary>Page du menu « Relevés » : création, renommage et suppression des vues de relevé.</summary>
public partial class ReadingViewListViewModel : EditableScreenViewModel
{
    private readonly MainViewModel _main;
    public ObservableCollection<ViewRow> Rows { get; } = new();

    public ReadingViewListViewModel(MainViewModel main)
    {
        _main = main;
        Load();
    }

    private void Load()
    {
        Rows.Clear();
        var lines = App.Db.ViewLines.AsNoTracking().ToList().ToLookup(l => l.ReadingViewId);
        foreach (var v in App.Db.ReadingViews.AsNoTracking().OrderBy(v => v.Famille).ThenBy(v => v.Ordre).ThenBy(v => v.Nom).ToList())
            Rows.Add(new ViewRow(v, lines[v.Id].Count(l => l.Kind == LineKind.Compteur), lines[v.Id].Count(l => l.Kind == LineKind.Tache)));
    }

    private static readonly List<string> RecurrenceNames = Enum.GetValues<Recurrence>().Select(Periods.Name).ToList();

    /// <summary>Saisie nom, famille et fréquence de la vue.</summary>
    private static bool Edit(string title, ReadingView v)
    {
        var nom = v.Nom; var fam = v.Famille; var rec = (int)v.Recurrence;
        var ok = App.Dialogs.EditFields(title, new List<EditField>
        {
            EditField.Text("Nom de la vue", () => nom, x => nom = x, required: true),
            EditField.Text("Famille (regroupement dans le menu, vide = aucune)", () => fam, x => fam = x),
            EditField.ComboField("Fréquence de la vue (une colonne par période)", RecurrenceNames, () => rec, x => rec = x)
        });
        if (!ok || string.IsNullOrWhiteSpace(nom)) return false;
        v.Nom = nom.Trim(); v.Famille = fam.Trim(); v.Recurrence = (Recurrence)rec;
        return true;
    }

    [RelayCommand]
    private void Add()
    {
        if (!App.GuardWritable() || !GuardEdit()) return;
        var v = new ReadingView { Ordre = (App.Db.ReadingViews.Max(x => (int?)x.Ordre) ?? -1) + 1 };
        if (!Edit("Nouvelle vue de relevé", v)) return;
        App.Db.ReadingViews.Add(v);
        App.Db.SaveChanges();
        Load();
        _main.RebuildViewItems();
    }

    [RelayCommand]
    private void Rename(ViewRow row)
    {
        if (!App.GuardWritable() || !GuardEdit()) return;
        var v = App.Db.ReadingViews.First(x => x.Id == row.View.Id);
        if (!Edit("Modifier la vue", v)) { App.Db.Entry(v).Reload(); return; }
        // une ligne n'est jamais plus rapide que sa vue
        foreach (var l in App.Db.ViewLines.Where(l => l.ReadingViewId == v.Id && l.Recurrence < v.Recurrence).ToList()) l.Recurrence = v.Recurrence;
        App.Db.SaveChanges();
        Load();
        _main.RebuildViewItems();
    }

    [RelayCommand]
    private void Delete(ViewRow row)
    {
        if (!App.GuardWritable() || !GuardEdit()) return;
        if (!App.Dialogs.ShowConfirm("Supprimer la vue",
                $"Supprimer la vue « {row.Nom} » et ses lignes ? Les compteurs et leurs relevés sont conservés ; les tâches cochées de cette vue sont perdues."))
            return;
        App.Db.ReadingViews.Remove(App.Db.ReadingViews.First(v => v.Id == row.View.Id));
        App.Db.SaveChanges();
        Load();
        _main.RebuildViewItems();
        _main.RefreshNotificationBadge();
    }

    [RelayCommand] private void Open(ViewRow row) => _main.OpenView(row.View.Id);
}
