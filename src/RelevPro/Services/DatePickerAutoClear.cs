using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace RelevPro.Services;

/// <summary>
/// Pour les champs « Date du changement » (écrans sans mois à cocher) : la date saisie est traitée tout de suite
/// (enregistrement, ou fenêtre de confirmation) puis le champ doit redevenir vide. Le DatePicker de WPF garde sinon le
/// texte tapé dans sa zone de saisie, même quand la valeur liée est remise à vide : il le revalide alors à la
/// prochaine perte du focus et propose de nouveau le même changement.
/// </summary>
public static class DatePickerAutoClear
{
    public static readonly DependencyProperty EnabledProperty = DependencyProperty.RegisterAttached(
        "Enabled", typeof(bool), typeof(DatePickerAutoClear), new PropertyMetadata(false, OnEnabledChanged));

    public static bool GetEnabled(DependencyObject d) => (bool)d.GetValue(EnabledProperty);
    public static void SetEnabled(DependencyObject d, bool value) => d.SetValue(EnabledProperty, value);

    private static void OnEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not DatePicker picker) return;
        picker.SelectedDateChanged -= OnSelectedDateChanged;
        if ((bool)e.NewValue) picker.SelectedDateChanged += OnSelectedDateChanged;
    }

    private static void OnSelectedDateChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is not DatePicker picker || picker.SelectedDate is null) return;

        // Après le traitement de la date (la fenêtre de confirmation éventuelle est déjà fermée) : on vide à la fois la
        // valeur et le texte de la zone de saisie.
        picker.Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
        {
            picker.SelectedDate = null;
            picker.Text = string.Empty;
        });
    }
}
