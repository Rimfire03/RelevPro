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
    /// <summary>Base SQLite existante choisie (aucune création).</summary>
    public string? ExistingPath { get; set; }

    /// <summary>Nouvelle base SQLite à créer.</summary>
    public string DatabasePath { get; set; } = "";

    /// <summary>Serveur de base de données choisi (PostgreSQL, MariaDB/MySQL, SQL Server) ; null = SQLite.</summary>
    public DbConnectionInfo? Server { get; set; }

    /// <summary>Le serveur contient déjà les tables de l'application : on s'y connecte sans rien créer.</summary>
    public bool ServerHasData { get; set; }

    /// <summary>Une base vide va être créée (SQLite ou serveur) : le site et la première vue sont à créer.</summary>
    public bool IsNewDatabase => ExistingPath is null && !ServerHasData;

    public string SiteName { get; set; } = Site.DefaultName;
    public string ViewName { get; set; } = "";
    public string ViewFamily { get; set; } = "";
    public Recurrence ViewRecurrence { get; set; } = Recurrence.Hebdo;
}

/// <summary>Assistant de première configuration (TomLine.UI.Wpf.WizardWindow) : base de données (fichier local ou
/// serveur), site, première vue. Rien n'est créé ici : l'application applique <see cref="Result"/> une fois la base ouverte.</summary>
public sealed class FirstRunWizard : WizardWindow
{
    private static readonly DatabaseProvider[] ServerProviders = { DatabaseProvider.PostgreSql, DatabaseProvider.MariaDb, DatabaseProvider.SqlServer };

    public FirstRunResult Result { get; } = new();

    private bool _server; // mode choisi à l'accueil : serveur ou fichier local

    private readonly TextBox _path = new();
    private readonly ComboBox _provider = new() { Width = 260, HorizontalAlignment = HorizontalAlignment.Left };
    private readonly TextBox _host = new() { Text = "localhost" };
    private readonly TextBox _port = new() { Text = "5432" };
    private readonly TextBox _database = new() { Text = "relevpro" };
    private readonly TextBox _user = new();
    private readonly PasswordBox _password = new();
    private readonly CheckBox _ssl = new() { Content = "Connexion chiffrée (SSL/TLS)" };
    private readonly TextBlock _testResult = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0), MaxWidth = 560, HorizontalAlignment = HorizontalAlignment.Left };
    private readonly TextBox _site = new();
    private readonly TextBox _view = new();
    private readonly TextBox _family = new();
    private readonly ComboBox _rec = new() { Width = 220, HorizontalAlignment = HorizontalAlignment.Left };

    public FirstRunWizard(string defaultDatabasePath)
        : base("RelevPro - Première utilisation", logo: null, width: 740, height: 680)
    {
        _path.Text = defaultDatabasePath;
        _site.Text = Site.DefaultName;
        foreach (var p in ServerProviders) _provider.Items.Add(DatabaseProviderInfo.DisplayName(p) + (DatabaseProviderInfo.IsRecommended(p) ? " (conseillé)" : ""));
        _provider.SelectedIndex = 0;
        _provider.SelectionChanged += (_, _) => _port.Text = DatabaseProviderInfo.DefaultPort(CurrentProvider).ToString();
        foreach (var r in Enumerable.Range(0, 5)) _rec.Items.Add(Periods.Name((Recurrence)r));
        _rec.SelectedIndex = (int)Recurrence.Hebdo;

        Steps.Add(new WizardStep
        {
            Title = "Bienvenue dans RelevPro",
            Subtitle = "Relevés de compteurs et petites tâches. Où seront enregistrées vos données ?",
            HideNext = true,
            Build = () =>
            {
                var p = new StackPanel();
                p.Children.Add(ChoiceButton("Nouvelle installation (fichier local)",
                    "Crée une base SQLite locale vide, avec votre premier site et votre première vue de relevé.", true, () => { _server = false; ShowStep(1); }));
                p.Children.Add(ChoiceButton("Serveur de base de données",
                    "PostgreSQL, MariaDB / MySQL ou SQL Server : plusieurs postes partagent les mêmes données (un seul écrit à la fois). Crée les tables si le serveur est vide, sinon s'y connecte.", false, () => { _server = true; ShowStep(1); }));
                p.Children.Add(ChoiceButton("Ouvrir une base existante (.db)",
                    "Reprend une base ou une sauvegarde RelevPro déjà créée (autre poste, sauvegarde).", false, PickExisting));
                return p;
            }
        });

        Steps.Add(new WizardStep
        {
            Title = "Base de données",
            Subtitle = "Fichier local, ou connexion au serveur choisi à l'étape précédente.",
            Build = () => _server ? BuildServerStep() : BuildFileStep(),
            Validate = () => _server ? ValidateServer() : ValidateFile()
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

    private DatabaseProvider CurrentProvider => ServerProviders[Math.Max(0, _provider.SelectedIndex)];

    // ---- Étape « Base de données » : fichier local

    private UIElement BuildFileStep()
    {
        var browse = new Button { Content = "Parcourir…", Style = Sty("SecondaryButton"), Margin = new Thickness(0, 6, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };
        browse.Click += (_, _) =>
        {
            var d = new SaveFileDialog { Title = "Emplacement de la base", Filter = "Base de données SQLite (*.db)|*.db", FileName = Path.GetFileName(_path.Text), OverwritePrompt = false };
            if (d.ShowDialog(this) == true) _path.Text = d.FileName;
        };
        var p = new StackPanel();
        p.Children.Add(Field("Fichier de base de données (SQLite)", Reuse(_path), "Placez-le dans un dossier sauvegardé."));
        p.Children.Add(browse);
        return p;
    }

    private string? ValidateFile()
    {
        var path = _path.Text.Trim();
        if (path.Length == 0) return "Indiquez l'emplacement du fichier.";
        if (File.Exists(path)) return "Ce fichier existe déjà : choisissez un autre nom, ou ouvrez-le depuis l'accueil.";
        try { Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!); }
        catch (Exception ex) { return "Dossier inutilisable : " + ex.Message; }
        Result.Server = null;
        Result.ServerHasData = false;
        Result.DatabasePath = path;
        return null;
    }

    // ---- Étape « Base de données » : serveur

    private UIElement BuildServerStep()
    {
        var test = new Button { Content = "Tester la connexion", Style = Sty("SecondaryButton"), Margin = new Thickness(0, 4, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };
        test.Click += async (_, _) =>
        {
            _testResult.Foreground = System.Windows.Media.Brushes.Gray;
            _testResult.Text = "Test en cours…";
            try
            {
                var info = BuildServerInfo(8);
                var r = await Task.Run(() => new DbContextFactory(DbTarget.ForServer(info)).TestConnection(8));
                _testResult.Foreground = System.Windows.Media.Brushes.SeaGreen;
                _testResult.Text = Describe(r);
            }
            catch (Exception ex)
            {
                _testResult.Foreground = System.Windows.Media.Brushes.Firebrick;
                _testResult.Text = "Échec : " + Short(ex);
            }
        };

        var p = new StackPanel();
        p.Children.Add(Field("Moteur", Reuse(_provider)));
        p.Children.Add(Field("Serveur (nom ou adresse)", Reuse(_host)));
        p.Children.Add(Field("Port", Reuse(_port)));
        p.Children.Add(Field("Nom de la base", Reuse(_database), "Créée automatiquement si elle n'existe pas (droit de création requis)."));
        p.Children.Add(Field("Utilisateur", Reuse(_user)));
        p.Children.Add(Field("Mot de passe", Reuse(_password), "Chiffré pour votre session Windows (jamais écrit en clair)."));
        p.Children.Add(Reuse(_ssl));
        p.Children.Add(test);
        p.Children.Add(Reuse(_testResult));
        return p;
    }

    private DbConnectionInfo BuildServerInfo(int timeoutSeconds = 15)
    {
        if (string.IsNullOrWhiteSpace(_host.Text)) throw new InvalidOperationException("Indiquez le serveur.");
        if (string.IsNullOrWhiteSpace(_database.Text)) throw new InvalidOperationException("Indiquez le nom de la base.");
        if (!int.TryParse(_port.Text.Trim(), out var port) || port is < 1 or > 65535) throw new InvalidOperationException("Port invalide.");
        return new DbConnectionInfo
        {
            Provider = CurrentProvider,
            Host = _host.Text.Trim(),
            Port = port,
            Database = _database.Text.Trim(),
            UserName = _user.Text.Trim(),
            Password = _password.Password,
            UseSsl = _ssl.IsChecked == true,
            ConnectTimeoutSeconds = timeoutSeconds
        };
    }

    private string? ValidateServer()
    {
        DbConnectionInfo info;
        DbContextFactory.ServerTestResult r;
        try
        {
            info = BuildServerInfo(8);
            Cursor = System.Windows.Input.Cursors.Wait;
            r = new DbContextFactory(DbTarget.ForServer(info)).TestConnection(8);
        }
        catch (Exception ex) { return "Connexion impossible : " + Short(ex); }
        finally { Cursor = null; }

        if (!r.DatabaseExists && r.CanCreateDatabase == false)
            return "Le serveur répond, mais cet utilisateur n'a pas le droit de créer la base « " + info.Database + " » : créez-la d'abord, ou utilisez un autre compte.";

        info.ConnectTimeoutSeconds = 15;
        Result.Server = info;
        Result.ServerHasData = r.HasAppTables;
        if (r.HasAppTables)
        {
            // Base RelevPro déjà en place : on s'y connecte telle quelle, sans créer ni site ni vue.
            DialogResult = true;
        }
        return null;
    }

    private static string Describe(DbContextFactory.ServerTestResult r) =>
        !r.DatabaseExists ? $"Serveur joignable ({r.ServerInfo}). La base n'existe pas encore : elle sera créée."
        : r.HasAppTables ? $"Connexion réussie ({r.ServerInfo}). Base RelevPro existante : les données seront reprises."
        : $"Connexion réussie ({r.ServerInfo}). Base vide : les tables seront créées.";

    private static string Short(Exception ex)
    {
        var m = (ex.InnerException ?? ex).Message;
        return m.Length > 300 ? m[..300] + "…" : m;
    }

    // ---- Divers

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
