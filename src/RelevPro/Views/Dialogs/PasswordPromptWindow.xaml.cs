using System.Windows;

namespace RelevPro.Views.Dialogs;

/// <summary>Saisie d'un mot de passe. <see cref="Validate"/> décide si la saisie est acceptée ; sinon la fenêtre reste
/// ouverte avec un message d'erreur.</summary>
public partial class PasswordPromptWindow : Window
{
    private readonly Func<string, bool> _validate;

    public PasswordPromptWindow(string title, string message, Func<string, bool> validate)
    {
        InitializeComponent();
        Title = title;
        MessageText.Text = message;
        _validate = validate;
        Loaded += (_, _) => PasswordInput.Focus();
    }

    private void OnOk(object sender, RoutedEventArgs e)
    {
        if (_validate(PasswordInput.Password))
        {
            DialogResult = true;
            return;
        }
        ErrorText.Text = "Mot de passe incorrect.";
        ErrorText.Visibility = Visibility.Visible;
        PasswordInput.Clear();
        PasswordInput.Focus();
    }
}
