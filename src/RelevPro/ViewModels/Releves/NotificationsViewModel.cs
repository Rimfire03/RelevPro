using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RelevPro.Core.Services;

namespace RelevPro.ViewModels.Releves;

public sealed record NotificationRow(AppNotification Item)
{
    public string Icon => Item.Level switch { NotificationLevel.Late => "🔴", NotificationLevel.Anomaly => "⚠", _ => "🟠" };
    public string Title => Item.Title;
    public string Detail => Item.Detail;
    public string ViewName => Item.ViewName;
    public bool CanOpen => Item.ViewId != 0;
}

/// <summary>Centre de notifications : saisies en retard, à faire, et valeurs forcées récentes.</summary>
public partial class NotificationsViewModel : ObservableObject
{
    private readonly MainViewModel _main;
    public ObservableCollection<NotificationRow> Rows { get; } = new();
    public string Summary { get; }

    public NotificationsViewModel(MainViewModel main)
    {
        _main = main;
        var items = NotificationService.Build(App.Db, DateTime.Today);
        foreach (var n in items) Rows.Add(new NotificationRow(n));
        Summary = items.Count == 0
            ? "Rien à signaler."
            : $"{items.Count(i => i.Level == NotificationLevel.Late)} en retard, {items.Count(i => i.Level == NotificationLevel.Pending)} à faire, " +
              $"{items.Count(i => i.Level == NotificationLevel.Anomaly)} valeur(s) forcée(s) ces {NotificationService.AnomalyDays} derniers jours.";
    }

    [RelayCommand] private void Open(NotificationRow row) { if (row.CanOpen) _main.OpenView(row.Item.ViewId); }
}
