using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace RelevPro;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContextChanged += (_, e) =>
        {
            if (e.NewValue is not ViewModels.MainViewModel vm) return;
            vm.PropertyChanged += (_, a) =>
            {
                if (a.PropertyName is nameof(ViewModels.MainViewModel.NotificationBadge) or nameof(ViewModels.MainViewModel.SelectedItem)) ApplyBell(vm);
            };
            ApplyBell(vm);
        };
    }

    /// <summary>Cloche du core TomLine : nombre d'alertes (saisies en retard), ambre tant que la page n'est pas ouverte.</summary>
    private void ApplyBell(ViewModels.MainViewModel vm) =>
        TomLine.UI.Wpf.Controls.NotificationBell.Apply(NotificationNav, vm.NotificationBadge, vm.SelectedItem == vm.NotificationsItem);

    /// <summary>TreeView.SelectedItem est en lecture seule (pas de binding direct possible) : on répercute
    /// la sélection sur le ViewModel manuellement, comme le faisait le binding bidirectionnel de l'ancienne
    /// ListBox à plat.</summary>
    private void NavigationTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (DataContext is ViewModels.MainViewModel vm && e.NewValue is ViewModels.NavigationItem item)
            vm.SelectedItem = item;
    }
}