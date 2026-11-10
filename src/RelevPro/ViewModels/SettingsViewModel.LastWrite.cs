using System.Globalization;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using RelevPro.Core.Services;

namespace RelevPro.ViewModels;

/// <summary>« Dernière saisie enregistrée le jj/mm/aa à hh:mm » (carte « Base de données » de Paramètres) : date
/// relevée dans la base elle-même (voir <see cref="DatabaseWriteTracking"/>), donc commune à tous les postes. Relue à
/// chaque affichage de l'écran, après chaque enregistrement fait depuis lui, et toutes les 10 secondes tant que
/// l'application est ouverte (une saisie faite par un autre poste apparaît ainsi sans rouvrir l'écran).</summary>
public partial class SettingsViewModel
{
    [ObservableProperty] private string _lastWriteDisplay = string.Empty;

    private DispatcherTimer? _lastWriteTimer;

    private void InitializeLastWrite()
    {
        RefreshLastWrite();
        _lastWriteTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
        _lastWriteTimer.Tick += (_, _) => RefreshLastWrite();
        _lastWriteTimer.Start();
    }

    public void Reload()
    {
        RefreshLastWrite();
        OnPropertyChanged(nameof(DatabaseSizeDisplay));
    }

    public void RefreshLastWrite()
    {
        var at = DatabaseWriteTracking.ReadLastWrite(App.Db);
        LastWriteDisplay = at is DateTime d
            ? $"Dernière saisie enregistrée le {d.ToString("dd/MM/yy", CultureInfo.InvariantCulture)} à {d.ToString("HH:mm", CultureInfo.InvariantCulture)}"
            : "Dernière saisie enregistrée : aucune depuis la mise en place de ce suivi";
    }
}
