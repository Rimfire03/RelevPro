using System.Windows;
using System.Windows.Controls;
using RelevPro.Core.Models;
using RelevPro.Core.Services;
using RelevPro.Services;

namespace RelevPro.Views.Dialogs;

/// <summary>Gestion d'un catalogue (unités ou familles de compteurs) : ajout, renommage, suppression.</summary>
public sealed class CatalogWindow : Window
{
    private readonly CatalogKind _kind;
    private readonly ListBox _list = new() { MinHeight = 260, Margin = new Thickness(0, 0, 0, 10) };
    private readonly TextBox _new = new() { Width = 220 };

    public CatalogWindow(CatalogKind kind, string title)
    {
        _kind = kind;
        Title = title;
        Width = 420; Height = 500;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        Background = (System.Windows.Media.Brush)Application.Current.FindResource("BrushSurface");

        Button B(string text, string style, Action a, bool isDefault = false)
        {
            var b = new Button { Content = text, Style = (Style)Application.Current.FindResource(style), Margin = new Thickness(0, 0, 8, 0), IsDefault = isDefault };
            b.Click += (_, _) => a();
            return b;
        }

        var add = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
        add.Children.Add(_new);
        var addButton = B("Ajouter", "PrimaryButton", Add, true);
        addButton.Margin = new Thickness(8, 0, 0, 0);
        add.Children.Add(addButton);

        var actions = new StackPanel { Orientation = Orientation.Horizontal };
        actions.Children.Add(B("Renommer", "SecondaryButton", Rename));
        actions.Children.Add(B("Supprimer", "DangerButton", Delete));
        actions.Children.Add(B("Fermer", "SecondaryButton", Close));

        var root = new StackPanel { Margin = new Thickness(20) };
        root.Children.Add(new TextBlock { Text = kind == CatalogKind.Unite ? "Unités proposées dans la liste déroulante des compteurs." : "Familles proposées dans la liste déroulante des compteurs.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10) });
        root.Children.Add(add);
        root.Children.Add(_list);
        root.Children.Add(actions);
        Content = root;
        Reload();
    }

    private void Reload() => _list.ItemsSource = CatalogService.Names(App.Db, _kind);

    private void Add()
    {
        if (!App.GuardWritable()) return;
        if (!CatalogService.Add(App.Db, _kind, _new.Text))
        {
            if (_new.Text.Trim().Length > 0) App.Dialogs.ShowMessage("Déjà présent", $"« {_new.Text.Trim()} » existe déjà.");
            return;
        }
        _new.Clear();
        Reload();
    }

    private void Rename()
    {
        if (_list.SelectedItem is not string old || !App.GuardWritable()) return;
        var name = old;
        if (!App.Dialogs.EditFields("Renommer", new List<EditField> { EditField.Text("Nom", () => name, v => name = v, required: true) })) return;
        if (!CatalogService.Rename(App.Db, _kind, old, name)) App.Dialogs.ShowMessage("Renommer", "Nom vide ou déjà utilisé.");
        Reload();
    }

    private void Delete()
    {
        if (_list.SelectedItem is not string name || !App.GuardWritable()) return;
        var used = CatalogService.Usage(App.Db, _kind, name);
        var msg = used > 0 ? $"« {name} » est utilisé par {used} compteur(s) : ils n'auront plus de valeur. Supprimer ?" : $"Supprimer « {name} » ?";
        if (!App.Dialogs.ShowConfirm("Supprimer", msg)) return;
        CatalogService.Delete(App.Db, _kind, name);
        Reload();
    }
}
