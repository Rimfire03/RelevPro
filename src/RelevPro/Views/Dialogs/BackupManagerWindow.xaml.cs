using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using RelevPro.Core.Services;

namespace RelevPro.Views.Dialogs;

/// <summary>Page « Gestion des sauvegardes automatiques » : tableau des sauvegardes du dossier Save DB (de la plus
/// récente à la plus ancienne), planification quotidienne / hebdomadaire / mensuelle et nombre de sauvegardes
/// conservées. Valable en mode fichier SQLite comme en mode serveur de base de données.</summary>
public partial class BackupManagerWindow : Window
{
    private static readonly string[] WeekDays = ["dimanche", "lundi", "mardi", "mercredi", "jeudi", "vendredi", "samedi"];
    private bool _loading = true;
    private bool _busy;

    public BackupManagerWindow()
    {
        InitializeComponent();

        foreach (var d in new[] { 1, 2, 3, 4, 5, 6, 0 }) WeekDayCombo.Items.Add(new ComboBoxItem { Content = WeekDays[d], Tag = d });
        for (var d = 1; d <= 28; d++) MonthDayCombo.Items.Add(new ComboBoxItem { Content = d.ToString(CultureInfo.InvariantCulture), Tag = d });

        var s = App.Settings;
        EnabledCheck.IsChecked = s.AutoBackupEnabled;
        SelectByTag(FrequencyCombo, s.AutoBackupFrequency);
        SelectByTag(WeekDayCombo, s.AutoBackupDayOfWeek);
        SelectByTag(MonthDayCombo, Math.Clamp(s.AutoBackupDayOfMonth, 1, 28));
        TimeBox.Text = BackupSchedule.TimeOfDay(s).ToString(@"hh\:mm");
        KeepBox.Text = s.MaxBackupsToKeep.ToString(CultureInfo.InvariantCulture);
        FolderText.Text = "Dossier : " + DbContextFactory.BackupDirectory +
                          (App.DbFactory.IsServer ? "  -  mode serveur : chaque sauvegarde est un fichier SQLite complet de la base du serveur." : "");
        _loading = false;

        UpdateVisibility();
        UpdateStatus();
        Reload();
        SaveSettingsButton.IsEnabled = false;
        App.AutoBackup.BackupCreated += OnAutoBackupCreated;
        Closed += (_, _) => App.AutoBackup.BackupCreated -= OnAutoBackupCreated;
    }

    private void OnAutoBackupCreated() => Dispatcher.BeginInvoke(() => { Reload(); UpdateStatus(); });

    private static void SelectByTag(ComboBox combo, object tag)
    {
        foreach (ComboBoxItem item in combo.Items)
            if (Equals(item.Tag?.ToString(), tag.ToString())) { combo.SelectedItem = item; return; }
        if (combo.Items.Count > 0) combo.SelectedIndex = 0;
    }

    private static string? TagOf(ComboBox combo) => (combo.SelectedItem as ComboBoxItem)?.Tag?.ToString();

    // ---- Réglages ----

    private void OnSettingChanged(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        SaveSettingsButton.IsEnabled = true;
    }

    private void OnFrequencyChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        UpdateVisibility();
        SaveSettingsButton.IsEnabled = true;
    }

    private void UpdateVisibility()
    {
        var freq = TagOf(FrequencyCombo);
        var weekly = freq == BackupSchedule.Weekly;
        var monthly = freq == BackupSchedule.Monthly;
        DayLabel.Visibility = weekly || monthly ? Visibility.Visible : Visibility.Collapsed;
        WeekDayCombo.Visibility = weekly ? Visibility.Visible : Visibility.Collapsed;
        MonthDayCombo.Visibility = monthly ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnSaveSettings(object sender, RoutedEventArgs e)
    {
        if (!TimeSpan.TryParseExact(TimeBox.Text.Trim(), @"h\:mm", CultureInfo.InvariantCulture, out var time)
            && !TimeSpan.TryParseExact(TimeBox.Text.Trim(), @"hh\:mm", CultureInfo.InvariantCulture, out time))
        {
            SetAction("Heure invalide : utilisez le format HH:mm (ex. 02:00).", error: true);
            return;
        }
        if (!int.TryParse(KeepBox.Text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var keep) || keep < 0)
        {
            SetAction("Le nombre de sauvegardes à conserver doit être un entier positif (0 = illimité).", error: true);
            return;
        }

        var s = App.Settings;
        var wasEnabled = s.AutoBackupEnabled;
        s.AutoBackupEnabled = EnabledCheck.IsChecked == true;
        s.AutoBackupFrequency = TagOf(FrequencyCombo) ?? BackupSchedule.Daily;
        s.AutoBackupDayOfWeek = int.TryParse(TagOf(WeekDayCombo), out var dow) ? dow : (int)DayOfWeek.Monday;
        s.AutoBackupDayOfMonth = int.TryParse(TagOf(MonthDayCombo), out var dom) ? dom : 1;
        s.AutoBackupTime = time.ToString(@"hh\:mm");
        s.MaxBackupsToKeep = keep;
        // Activation : la première sauvegarde a lieu à la prochaine échéance, pas tout de suite.
        if (s.AutoBackupEnabled && (!wasEnabled || s.AutoBackupLastRun is null)) s.AutoBackupLastRun = DateTime.Now;
        s.Save();

        BackupService.MaxToKeep = keep;
        var removed = BackupService.Prune(keep);
        SaveSettingsButton.IsEnabled = false;
        UpdateStatus();
        Reload();
        SetAction("Réglages enregistrés" + (removed > 0 ? $" ({removed} ancienne(s) sauvegarde(s) supprimée(s))." : "."), error: false);
    }

    private void UpdateStatus()
    {
        var s = App.Settings;
        if (!s.AutoBackupEnabled)
        {
            ScheduleStatusText.Text = "Sauvegarde automatique désactivée.";
            return;
        }
        var last = s.AutoBackupLastRun is { } l ? l.ToString("dd/MM/yyyy HH:mm", CultureInfo.CurrentCulture) : "jamais";
        var next = BackupSchedule.Next(s, DateTime.Now);
        ScheduleStatusText.Text = $"Dernière sauvegarde automatique : {last}  -  prochaine : {next:dd/MM/yyyy HH:mm}" +
                                  (App.IsReadOnly ? "  (poste en lecture seule : aucune sauvegarde automatique)" : "") +
                                  (App.AutoBackup.LastError is { } err ? $"  -  dernière erreur : {err}" : "");
    }

    // ---- Tableau ----

    private void Reload()
    {
        var list = BackupService.List();
        BackupGrid.ItemsSource = list;
        var total = list.Sum(b => b.Size);
        SummaryText.Text = $"{list.Count} sauvegarde(s)  -  {total / 1024.0 / 1024.0:0.0} Mo au total";
    }

    private BackupEntry? Selected()
    {
        if (BackupGrid.SelectedItem is BackupEntry b) return b;
        SetAction("Sélectionnez d'abord une sauvegarde dans le tableau.", error: true);
        return null;
    }

    private void SetAction(string text, bool error)
    {
        ActionStatusText.Foreground = (Brush)FindResource(error ? "BrushDanger" : "BrushPrimary");
        ActionStatusText.Text = text;
    }

    private async void OnBackupNow(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        _busy = true;
        SetAction("Sauvegarde en cours...", error: false);
        try
        {
            var path = await App.AutoBackup.BackupNowAsync("manuelle");
            Reload();
            SetAction("Sauvegarde créée : " + System.IO.Path.GetFileName(path), error: false);
        }
        catch (Exception ex)
        {
            SetAction("Échec de la sauvegarde : " + ex.Message, error: true);
        }
        finally
        {
            _busy = false;
        }
    }

    private void OnRestore(object sender, RoutedEventArgs e)
    {
        if (_busy || Selected() is not { } b || !App.GuardWritable()) return;
        if (!App.Dialogs.ShowConfirm("Restaurer une sauvegarde",
                $"Cette opération va REMPLACER toutes les données actuelles par celles de la sauvegarde du {b.DateText} ({b.Kind}), " +
                "puis redémarrer l'application. Une sauvegarde de l'état actuel est faite juste avant. Continuer ?"))
            return;

        try
        {
            // La purge est suspendue : elle ne doit pas supprimer la sauvegarde choisie avant sa restauration.
            BackupService.PruneSuspended = true;
            App.DbFactory.CreateBackup("avant-restauration");
            App.ImportDatabaseBackup(b.Path);
        }
        catch (Exception ex)
        {
            BackupService.PruneSuspended = false;
            SetAction("Échec de la restauration : " + ex.Message, error: true);
        }
    }

    private void OnProtectClick(object sender, RoutedEventArgs e)
    {
        if (sender is not CheckBox { DataContext: BackupEntry b } box) return;
        b.IsProtected = box.IsChecked == true;
        if (b.IsProtected) BackupService.ProtectedFiles.Add(b.FileName); else BackupService.ProtectedFiles.Remove(b.FileName);
        App.Settings.ProtectedBackups = BackupService.ProtectedFiles.ToList();
        App.Settings.Save();
        SetAction(b.IsProtected
            ? "Sauvegarde protégée : elle ne sera pas supprimée automatiquement."
            : "Protection retirée : cette sauvegarde pourra être supprimée par la purge automatique.", error: false);
    }

    private void OnDelete(object sender, RoutedEventArgs e)
    {
        if (Selected() is not { } b) return;
        if (!App.Dialogs.ShowConfirm("Supprimer la sauvegarde", $"Supprimer définitivement {b.FileName} ?" +
                (b.IsProtected ? "\n\nCette sauvegarde est protégée : la suppression manuelle l'enlèvera quand même." : ""))) return;
        try
        {
            BackupService.Delete(b.Path);
            App.Settings.ProtectedBackups = BackupService.ProtectedFiles.ToList();
            App.Settings.Save();
            Reload();
            SetAction("Sauvegarde supprimée.", error: false);
        }
        catch (Exception ex)
        {
            SetAction("Suppression impossible : " + ex.Message, error: true);
        }
    }

    private void OnOpenFolder(object sender, RoutedEventArgs e) =>
        Process.Start(new ProcessStartInfo { FileName = DbContextFactory.BackupDirectory, UseShellExecute = true });

    private void OnClose(object sender, RoutedEventArgs e) => Close();
}
