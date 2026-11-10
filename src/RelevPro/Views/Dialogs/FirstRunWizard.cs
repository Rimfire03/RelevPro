using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using RelevPro.Core.Models;
using RelevPro.Core.Services;
using TomLine.UI.Wpf.Controls;

namespace RelevPro.Views.Dialogs;

/// <summary>Choix faits dans l'assistant de première configuration.</summary>
public sealed class FirstRunResult
{
    /// <summary>Base existante choisie (aucune création) ; sinon <see cref="DatabasePath"/> reçoit la nouvelle base.</summary>
    public string? ExistingPath { get; set; }
    public string DatabasePath { get; set; } = "";
    public string SiteName { get; set; } = Site.DefaultName;
    public string ViewName { get; set; } = "";
    public string ViewFamily { get; set; } = "";
    public Recurrence ViewRecurrence { get; set; } = Recurrence.Hebdo;
}

/// <summary>Assistant de première configuration (TomLine.UI.Wpf.WizardWindow) : base de données, site, première vue.
/// Rien n'est créé ici : l'application applique <see cref="Result"/> une fois la base ouverte.</summary>
public sealed class FirstRunWizard : WizardWindow
{
    public FirstRunResult Result { get; } = new();

    private readonly TextBox _path = new();
    private readonly TextBox _site = new();
    private readonly TextBox _view = new();
    private readonly TextBox _family = new();
    private readonly ComboBox _rec = new() { Width = 220, HorizontalAlignment = HorizontalAlignment.Left };

    public FirstRunWizard(string defaultDatabasePath)
        : base("RelevPro - Première utilisation", logo: null, width: 740, height: 620)
    {
        _path.Text = defaultDatabasePath;
        _site.Text = Site.DefaultName;
        foreach (var r in Enum.GetValues<Recurrence>()) _rec.Items.Add(Periods.Name(r));
        _rec.SelectedIndex = (int)Recurrence.Hebdo;

        Steps.Add(new WizardStep
        {
            Title = "Bienvenue dans RelevPro",
            Subtitle = "Relevés de compteurs et petites tâches. Quelques questions pour démarrer.",
            HideNext = true,
            Build = () =>
            {
                var p = new StackPanel();
                p.Children.Add(ChoiceButton("Nouvelle installation",
                    "Crée une base de données locale vide, avec votre premier site et votre première vue de relevé.", true, () => ShowStep(1)));
                p.Children.Add(ChoiceButton("Ouvrir une base existante (.db)",
                    "Reprend une base ou une sauvegarde RelevPro déjà créée (autre poste, sauvegarde).", false, PickExisting));
                return p;
            }
        });

        Steps.Add(new WizardStep
        {
            Title = "Emplacement de la base",
            Subtitle = "Fichier SQLite qui contiendra les compteurs, les relevés et les vues. Placez-le dans un dossier sauvegardé.",
            Build = () =>
            {
                var browse = new Button { Content = "Parcourir…", Style = Sty("SecondaryButton"), Margin = new Thickness(0, 6, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };
                browse.Click += (_, _) =>
                {
                    var d = new SaveFileDialog { Title = "Emplacement de la base", Filter = "Base de données SQLite (*.db)|*.db", FileName = Path.GetFileName(_path.Text), OverwritePrompt = false };
                    if (d.ShowDialog(this) == true) _path.Text = d.FileName;
                };
                var p = new StackPanel();
                p.Children.Add(Field("Fichier de base de données", Reuse(_path)));
                p.Children.Add(browse);
                return p;
            },
            Validate = () =>
            {
                var path = _path.Text.Trim();
                if (path.Length == 0) return "Indiquez l'emplacement du fichier.";
                if (File.Exists(path)) return "Ce fichier existe déjà : choisissez un autre nom, ou ouvrez-le depuis l'accueil.";
                try { Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!); }
                catch (Exception ex) { return "Dossier inutilisable : " + ex.Message; }
                Result.DatabasePath = path;
                return null;
            }
        });

        Steps.Add(new WizardStep
        {
            Title = "Votre site",
            Subtitle = "Nom du site ou de l'établissement. D'autres sites pourront être ajoutés ensuite (option multisite).",
            Build = () =>
            {
                var p = new StackPanel();
                p.Children.Add(Field("Nom du site", Reuse(_site)));
                return p;
            },
            Validate = () =>
            {
                if (string.IsNullOrWhiteSpace(_site.Text)) return "Indiquez un nom de site.";
                Result.SiteName = _site.Text.Trim();
                return null;
            }
        });

        Steps.Add(new WizardStep
        {
            Title = "Première vue de relevé",
            Subtitle = "Une vue regroupe des compteurs et des tâches. Sa fréquence donne une colonne par période (semaine pour une vue hebdomadaire). Facultatif : laissez le nom vide pour la créer plus tard.",
            NextText = "Terminer",
            Build = () =>
            {
                var p = new StackPanel();
                p.Children.Add(Field("Nom de la vue", Reuse(_view), "Ex. Relevés hebdo chaufferie"));
                p.Children.Add(Field("Famille (regroupement dans le menu)", Reuse(_family), "Facultatif."));
                p.Children.Add(Field("Fréquence", Reuse(_rec), "Les lignes de la vue peuvent être plus lentes (mensuelles, trimestrielles...), jamais plus rapides."));
                return p;
            },
            Validate = () =>
            {
                Result.ViewName = _view.Text.Trim();
                Result.ViewFamily = _family.Text.Trim();
                Result.ViewRecurrence = (Recurrence)Math.Max(0, _rec.SelectedIndex);
                DialogResult = true;
                return null;
            }
        });

        Start();
    }

    private void PickExisting()
    {
        var d = new OpenFileDialog { Title = "Choisir une base de données existante", Filter = "Base de données SQLite (*.db)|*.db|Tous les fichiers (*.*)|*.*" };
        if (d.ShowDialog(this) != true) return;
        Result.ExistingPath = d.FileName;
        DialogResult = true;
    }

    /// <summary>Détache le contrôle de son ancien conteneur (l'étape est reconstruite à chaque affichage).</summary>
    private static T Reuse<T>(T c) where T : FrameworkElement
    {
        if (c.Parent is Panel p) p.Children.Remove(c);
        return c;
    }
}
