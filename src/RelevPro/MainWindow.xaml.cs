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
    }

    /// <summary>TreeView.SelectedItem est en lecture seule (pas de binding direct possible) : on répercute
    /// la sélection sur le ViewModel manuellement, comme le faisait le binding bidirectionnel de l'ancienne
    /// ListBox à plat.</summary>
    private void NavigationTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (DataContext is ViewModels.MainViewModel vm && e.NewValue is ViewModels.NavigationItem item)
            vm.SelectedItem = item;
    }
}