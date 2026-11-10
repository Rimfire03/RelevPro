using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using RelevPro.Core.Services;

namespace RelevPro.Views.Dialogs;

/// <summary>Page « Réglages BDD avancés » (Paramètres > Base de données) : choix fichier SQLite / serveur,
/// réglages de connexion, test, et outil de migration de la base actuelle vers le serveur.</summary>
public partial class DatabaseServerWindow : Window
{
    private DatabaseProvider _provider = DatabaseProvider.PostgreSql;
    private bool _loading = true;
    private bool _busy;

    public DatabaseServerWindow()
    {
        InitializeComponent();

        var s = App.Settings;
        CurrentModeText.Text = "Base actuellement utilisée : " + App.DbFactory.Target.Describe();

        var info = s.DatabaseServer;
        HostBox.Text = info.Host;
        PortBox.Text = info.Port.ToString();
        DatabaseBox.Text = info.Database;
        UserBox.Text = info.UserName;
        PasswordBox.Password = info.Password;
        SslCheck.IsChecked = info.UseSsl;
        _provider = info.Provider == DatabaseProvider.Sqlite ? DatabaseProvider.PostgreSql : info.Provider;
        SelectProvider(_provider);

        if (s.UseDatabaseServer) ServerModeRadio.IsChecked = true; else FileModeRadio.IsChecked = true;
        _loading = false;
        UpdateEnabledState();
        UpdateHint();
    }

    private void SelectProvider(DatabaseProvider p)
    {
        foreach (ComboBoxItem item in ProviderCombo.Items)
            if ((string)item.Tag == p.ToString()) ProviderCombo.SelectedItem = item;
    }

    private void OnModeChanged(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded && _loading) return;
        UpdateEnabledState();
    }

    private void UpdateEnabledState()
    {
        if (ServerPanel is null) return;
        var server = ServerModeRadio.IsChecked == true;
        ServerPanel.IsEnabled = !_busy;
        MigrationPanel.IsEnabled = !_busy;
        SaveButton.IsEnabled = !_busy;
        CloseButton.IsEnabled = !_busy;
        ServerPanel.Opacity = server || MigrationPanel.IsEnabled ? 1 : 0.6;
        SaveButton.Content = server ? "Enregistrer et redémarrer" : "Utiliser le fichier et redémarrer";
    }

    private void OnProviderChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ProviderCombo.SelectedItem is not ComboBoxItem item) return;
        var newProvider = Enum.Parse<DatabaseProvider>((string)item.Tag);
        if (!_loading && int.TryParse(PortBox.Text, out var port) && port == DatabaseProviderInfo.DefaultPort(_provider))
            PortBox.Text = DatabaseProviderInfo.DefaultPort(newProvider).ToString();
        _provider = newProvider;
        UpdateHint();
    }

    private void UpdateHint()
    {
        if (ProviderHint is null) return;
        ProviderHint.Text = _provider switch
        {
            DatabaseProvider.PostgreSql => "Conseillé pour cet usage : gratuit, accepte plusieurs postes connectés, très fiable. Port par défaut : 5432.",
            DatabaseProvider.MariaDb => "Compatible MariaDB 10 et MySQL 8. Port par défaut : 3306.",
            DatabaseProvider.SqlServer => "SQL Server 2017 ou plus récent (l'édition Express convient). Port par défaut : 1433.",
            _ => ""
        };
        if (!_loading && PortBox.Text.Trim().Length == 0) PortBox.Text = DatabaseProviderInfo.DefaultPort(_provider).ToString();
    }

    /// <summary>Réglages saisis, ou null (avec message) s'ils sont incomplets.</summary>
    private DbConnectionInfo? ReadForm()
    {
        if (!int.TryParse(PortBox.Text.Trim(), out var port) || port is < 1 or > 65535)
        {
            App.Dialogs.ShowMessage("Réglages incomplets", "Le port doit être un nombre entre 1 et 65535.");
            return null;
        }
        if (HostBox.Text.Trim().Length == 0 || DatabaseBox.Text.Trim().Length == 0 || UserBox.Text.Trim().Length == 0)
        {
            App.Dialogs.ShowMessage("Réglages incomplets", "Renseignez le serveur, le nom de la base et l'utilisateur.");
            return null;
        }
        var info = new DbConnectionInfo
        {
            Provider = _provider,
            Host = HostBox.Text.Trim(),
            Port = port,
            Database = DatabaseBox.Text.Trim(),
            UserName = UserBox.Text.Trim(),
            UseSsl = SslCheck.IsChecked == true
        };
        info.Password = PasswordBox.Password;
        return info;
    }

    private void SetBusy(bool busy, string? migrationText = null)
    {
        _busy = busy;
        MigrationProgress.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        if (migrationText is not null) MigrationStatusText.Text = migrationText;
        UpdateEnabledState();
    }

    private static string Describe(Exception ex)
    {
        while (ex.InnerException is not null && ex is AggregateException or System.Reflection.TargetInvocationException)
            ex = ex.InnerException;
        return ex.Message;
    }

    private async void OnTestClick(object sender, RoutedEventArgs e)
    {
        var info = ReadForm();
        if (info is null) return;

        TestResultText.Foreground = (Brush)FindResource("BrushTextSecondary");
        TestResultText.Text = "Connexion en cours...";
        TestButton.ClearValue(BackgroundProperty);
        TestButton.ClearValue(ForegroundProperty);
        TestButton.IsEnabled = false;
        try
        {
            var result = await Task.Run(() => new DbContextFactory(DbTarget.ForServer(info)).TestConnection(timeoutSeconds: 8));
            if (!result.DatabaseExists && result.CanCreateDatabase == false)
            {
                SetTestButtonState(ok: false);
                TestResultText.Foreground = (Brush)FindResource("BrushDanger");
                TestResultText.Text = $"Serveur joignable (version {result.ServerInfo}) mais la base « {info.Database} » n'existe pas et l'utilisateur « {info.UserName} » n'a pas le droit de créer une base. " +
                                      "Créez-la sur le serveur, ou donnez-lui ce droit.";
            }
            else
            {
                SetTestButtonState(ok: true);
                TestResultText.Foreground = (Brush)FindResource("BrushPrimary");
                TestResultText.Text = $"Connexion réussie (version {result.ServerInfo}). " + (!result.DatabaseExists
                    ? $"La base « {info.Database} » n'existe pas encore : elle sera créée automatiquement" + (result.CanCreateDatabase == true ? "." : " (si l'utilisateur en a le droit).")
                    : result.HasAppTables ? "La base contient déjà des données de l'application." : "La base existe et est vide.");
            }
        }
        catch (Exception ex)
        {
            SetTestButtonState(ok: false);
            TestResultText.Foreground = (Brush)FindResource("BrushDanger");
            TestResultText.Text = "Échec de la connexion : " + Describe(ex);
        }
        finally
        {
            TestButton.IsEnabled = true;
        }
    }

    /// <summary>Bouton « Tester la connexion » : vert si le test a réussi, rouge sinon.</summary>
    private void SetTestButtonState(bool ok)
    {
        TestButton.Background = new SolidColorBrush(ok ? Color.FromRgb(0x16, 0xA3, 0x4A) : Color.FromRgb(0xDC, 0x26, 0x26));
        TestButton.Foreground = Brushes.White;
    }

    private async void OnMigrateClick(object sender, RoutedEventArgs e)
    {
        var info = ReadForm();
        if (info is null) return;

        var source = App.DbFactory.Target;
        if (source.IsServer && source.Server!.Provider == info.Provider &&
            string.Equals(source.Server.Host, info.Host, StringComparison.OrdinalIgnoreCase) &&
            source.Server.Port == info.Port && string.Equals(source.Server.Database, info.Database, StringComparison.OrdinalIgnoreCase))
        {
            App.Dialogs.ShowMessage("Migration impossible", "La base actuelle est déjà ce serveur.");
            return;
        }

        if (!App.Dialogs.ShowConfirm("Migrer vers le serveur",
                $"Toutes les données de la base actuelle vont être copiées vers :\n{info.Describe()}\n\n" +
                "La base actuelle reste intacte. Continuer ?"))
            return;

        var destination = DbTarget.ForServer(info);
        var progress = new Progress<string>(text => MigrationStatusText.Text = text);
        var replace = false;

        while (true)
        {
            SetBusy(true, "Migration en cours...");
            try
            {
                var report = await Task.Run(() => DatabaseCopier.Copy(source, destination, App.CurrentVersion, replace, progress));
                SetBusy(false);
                ShowReport(report);
                return;
            }
            catch (DatabaseCopier.DestinationNotEmptyException ex)
            {
                SetBusy(false, "");
                if (!App.Dialogs.ShowConfirm("Le serveur contient déjà des données",
                        ex.Message + "\n\nREMPLACER ces données par celles de la base actuelle ? Cette opération efface le contenu de la base du serveur.\n\n" +
                        "Seules les tables de ce logiciel sont concernées."))
                {
                    MigrationStatusText.Foreground = (Brush)FindResource("BrushDanger");
                    MigrationStatusText.Text = "Migration annulée : le serveur n'a pas été modifié.";
                    return;
                }
                replace = true;
            }
            catch (Exception ex)
            {
                SetBusy(false);
                MigrationStatusText.Foreground = (Brush)FindResource("BrushDanger");
                MigrationStatusText.Text = "Échec de la migration : " + Describe(ex) + "\nLe serveur n'a pas été modifié.";
                return;
            }
        }
    }

    /// <summary>Copie toute la base du serveur saisi ci-dessus vers le fichier SQLite du logiciel (chemin des
    /// Paramètres). Le serveur n'est pas modifié ; un fichier existant est sauvegardé (Save DB) avant remplacement.</summary>
    private async void OnMigrateToFileClick(object sender, RoutedEventArgs e)
    {
        var info = ReadForm();
        if (info is null) return;

        var source = DbTarget.ForServer(info);
        var filePath = App.Settings.ResolvedDatabasePath;
        var destination = DbTarget.ForFile(filePath);

        if (!App.Dialogs.ShowConfirm("Migrer vers un fichier SQLite",
                $"Toutes les données du serveur :\n{info.Describe()}\n\nvont être copiées dans le fichier SQLite :\n{filePath}\n\n" +
                "Le serveur reste intact. Continuer ?"))
            return;

        var progress = new Progress<string>(text => MigrationStatusText.Text = text);
        var replace = false;

        while (true)
        {
            SetBusy(true, "Migration en cours...");
            try
            {
                var report = await Task.Run(() => DatabaseCopier.Copy(source, destination, App.CurrentVersion, replace, progress));
                SetBusy(false);
                ShowReport(report, toFile: true);
                return;
            }
            catch (DatabaseCopier.DestinationNotEmptyException ex)
            {
                SetBusy(false, "");
                if (!App.Dialogs.ShowConfirm("Le fichier contient déjà des données",
                        ex.Message + "\n\nREMPLACER ces données par celles du serveur ? Une sauvegarde du fichier actuel est faite dans le dossier Save DB avant remplacement."))
                {
                    MigrationStatusText.Foreground = (Brush)FindResource("BrushDanger");
                    MigrationStatusText.Text = "Migration annulée : le fichier SQLite n'a pas été modifié.";
                    return;
                }
                try
                {
                    await Task.Run(() => new DbContextFactory(destination).CreateBackup("avant-migration-depuis-serveur"));
                }
                catch (Exception bex)
                {
                    MigrationStatusText.Foreground = (Brush)FindResource("BrushDanger");
                    MigrationStatusText.Text = "Sauvegarde du fichier actuel impossible, migration annulée : " + Describe(bex);
                    return;
                }
                replace = true;
            }
            catch (Exception ex)
            {
                SetBusy(false);
                MigrationStatusText.Foreground = (Brush)FindResource("BrushDanger");
                MigrationStatusText.Text = "Échec de la migration : " + Describe(ex) + "\nLe serveur n'a pas été modifié.";
                return;
            }
        }
    }
    private void ShowReport(CopyReport report, bool toFile = false)
    {
        var lines = report.Tables.Where(t => t.SourceRows > 0 || t.DestinationRows > 0)
            .Select(t => $"{t.Table} : {t.DestinationRows}" + (t.SourceRows == t.DestinationRows ? "" : $" (attendu {t.SourceRows})"));
        MigrationStatusText.Foreground = (Brush)FindResource(report.IsComplete ? "BrushPrimary" : "BrushDanger");
        MigrationStatusText.Text = (report.IsComplete
            ? $"Migration terminée : {report.TotalRows} lignes copiées et vérifiées.\n"
            : "Migration terminée MAIS des écarts ont été constatés :\n") + string.Join("\n", lines);

        if (!report.IsComplete) return;
        if (toFile)
        {
            if (App.Dialogs.ShowConfirm("Migration terminée",
                    $"{report.TotalRows} lignes copiées dans le fichier SQLite.{"\n\n"}Utiliser le fichier SQLite dès maintenant ? Le logiciel va redémarrer."))
            {
                FileModeRadio.IsChecked = true;
                SaveSettings(null);
            }
            return;
        }
        if (App.Dialogs.ShowConfirm("Migration terminée",
                $"{report.TotalRows} lignes copiées vers le serveur.\n\nUtiliser le serveur dès maintenant ? Le logiciel va redémarrer."))
        {
            ServerModeRadio.IsChecked = true;
            SaveSettings(DbConnectionInfoFromFormOrNull());
        }
    }

    private DbConnectionInfo? DbConnectionInfoFromFormOrNull() => ReadForm();

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        if (ServerModeRadio.IsChecked == true)
        {
            var info = ReadForm();
            if (info is null) return;
            if (!App.Dialogs.ShowConfirm("Changer de base de données",
                    $"Le logiciel va utiliser le serveur :\n{info.Describe()}\n\nIl va redémarrer. Si ce serveur est vide, il sera initialisé ; pour y reprendre vos données actuelles, utilisez d'abord « Migrer la base actuelle vers ce serveur ». Continuer ?"))
                return;
            SaveSettings(info);
        }
        else
        {
            if (!App.Settings.UseDatabaseServer)
            {
                App.Dialogs.ShowMessage("Réglages avancés", "Le logiciel utilise déjà le fichier SQLite.");
                return;
            }
            if (!App.Dialogs.ShowConfirm("Changer de base de données",
                    "Le logiciel va utiliser de nouveau le fichier SQLite (les données du serveur ne sont pas copiées dans ce fichier : utilisez avant « Exporter la base de données » si besoin). Il va redémarrer. Continuer ?"))
                return;
            SaveSettings(null);
        }
    }

    /// <summary>Enregistre le mode choisi puis redémarre ; <paramref name="server"/> null = retour au fichier.</summary>
    private void SaveSettings(DbConnectionInfo? server)
    {
        if (server is not null)
        {
            App.Settings.DatabaseServer = server;
            App.Settings.UseDatabaseServer = true;
        }
        else
        {
            App.Settings.UseDatabaseServer = false;
        }
        App.Settings.Save();
        App.Restart();
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();
}
