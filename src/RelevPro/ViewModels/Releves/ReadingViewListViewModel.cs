using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using RelevPro.Core.Models;
using RelevPro.Services;

namespace RelevPro.ViewModels.Releves;

public sealed record ViewRow(ReadingView View, int MeterLines, int TaskLines)
{
    public string Nom => View.Nom;
    public string Summary => $"{MeterLines} compteur(s), {TaskLines} tâche(s)";
}

/// <summary>Page du menu « Relevés » : création, renommage et suppression des vues de relevé.</summary>
public partial class ReadingViewListViewModel : ObservableObject
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
        foreach (var v in App.Db.ReadingViews.AsNoTracking().OrderBy(v => v.Ordre).ThenBy(v => v.Nom).ToList())
            Rows.Add(new ViewRow(v, lines[v.Id].Count(l => l.Kind == LineKind.Compteur), lines[v.Id].Count(l => l.Kind == LineKind.Tache)));
    }

    private static bool Edit(string title, ref string name)
    {
        var n = name;
        var ok = App.Dialogs.EditFields(title, new List<EditField> { EditField.Text("Nom de la vue", () => n, v => n = v, required: true) });
        name = n.Trim();
        return ok && name.Length > 0;
    }

    [RelayCommand]
    private void Add()
    {
        if (!App.GuardWritable()) return;
        var name = string.Empty;
        if (!Edit("Nouvelle vue de relevé", ref name)) return;
        App.Db.ReadingViews.Add(new ReadingView { Nom = name, Ordre = (App.Db.ReadingViews.Max(v => (int?)v.Ordre) ?? -1) + 1 });
        App.Db.SaveChanges();
        Load();
        _main.RebuildViewItems();
    }

    [RelayCommand]
    private void Rename(ViewRow row)
    {
        if (!App.GuardWritable()) return;
        var name = row.View.Nom;
        if (!Edit("Renommer la vue", ref name)) return;
        App.Db.ReadingViews.First(v => v.Id == row.View.Id).Nom = name;
        App.Db.SaveChanges();
        Load();
        _main.RebuildViewItems();
    }

    [RelayCommand]
    private void Delete(ViewRow row)
    {
        if (!App.GuardWritable()) return;
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
