using System.Windows;
using RelevPro.Views.Dialogs;

namespace RelevPro.Services;

public interface IDialogService
{
    void ShowMessage(string title, string message);
    bool ShowConfirm(string title, string message);
    bool EditFields(string title, List<EditField> fields);
}

public class DialogService : IDialogService
{
    public void ShowMessage(string title, string message) =>
        MessageBox.Show(Application.Current.MainWindow, message, title, MessageBoxButton.OK, MessageBoxImage.Information);

    public bool ShowConfirm(string title, string message) =>
        MessageBox.Show(Application.Current.MainWindow, message, title, MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;

    public bool EditFields(string title, List<EditField> fields)
    {
        var window = new DynamicEditWindow(title, fields) { Owner = Application.Current.MainWindow };
        return window.ShowDialog() == true;
    }
}
