using RelevPro.ViewModels;

namespace RelevPro;

public partial class App
{
    /// <summary>À l'ouverture : s'il y a des saisies en retard, affiche directement le centre de notifications.
    /// Ne bloque jamais le démarrage.</summary>
    private static void NotificationsAtStartup()
    {
        try
        {
            if (Current.MainWindow?.DataContext is MainViewModel main && main.NotificationBadge > 0)
                main.ShowNotifications();
        }
        catch
        {
            // Confort : une erreur ici ne doit pas gêner l'ouverture.
        }
    }
}
