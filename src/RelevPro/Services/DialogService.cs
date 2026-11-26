using System.Windows;
using RelevPro.Views.Dialogs;

namespace RelevPro.Services;

public interface IDialogService
{
    void ShowMessage(string title, string message);
    bool ShowConfirm(string title, string message);
    bool EditFields(string title, List<EditField> fields);
    /// <summary>Boîte à plusieurs boutons : retourne l'index du bouton choisi, ou -1 si la boîte est fermée sans choix.</summary>
    int ShowChoice(string title, string message, params string[] buttons);
}

public class DialogService : IDialogService
{
    public void ShowMessage(string title, string message) =>
        MessageBox.Show(Application.Current.MainWindow, message, title, MessageBoxButton.OK, MessageBoxImage.Information);

    public bool ShowConfirm(string title, string message) =>
        MessageBox.Show(Application.Current.MainWindow, message, title, MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;

    public int ShowChoice(string title, string message, params string[] buttons)
    {
        var result = -1;
        var window = new Window
        {
            Title = title, Owner = Application.Current.MainWindow, WindowStartupLocation = WindowStartupLocation.CenterOwner,
            SizeToContent = SizeToContent.WidthAndHeight, MaxWidth = 560, ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false
        };
        var panel = new System.Windows.Controls.StackPanel { Margin = new Thickness(20) };
        panel.Children.Add(new System.Windows.Controls.TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 14) });
        for (var i = 0; i < buttons.Length; i++)
        {
            var index = i;
            var b = new System.Windows.Controls.Button { Content = buttons[i], Padding = new Thickness(10, 6, 10, 6), Margin = new Thickness(0, 0, 0, 6), IsDefault = i == 0, IsCancel = i == buttons.Length - 1 };
            if (i == 0 && Application.Current.TryFindResource("PrimaryButton") is Style primary) b.Style = primary;
            else if (Application.Current.TryFindResource("SecondaryButton") is Style secondary) b.Style = secondary;
            b.Click += (_, _) => { result = index; window.Close(); };
            panel.Children.Add(b);
        }
        window.Content = panel;
        window.ShowDialog();
        return result;
    }

    public bool EditFields(string title, List<EditField> fields)
    {
        var window = new DynamicEditWindow(title, fields) { Owner = Application.Current.MainWindow };
        return window.ShowDialog() == true;
    }
}
