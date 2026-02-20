using System.Windows;
using System.Windows.Input;
using ClaudeMon.ViewModels;

namespace ClaudeMon.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        MaxHeight = SystemParameters.WorkArea.Height;
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 1)
            DragMove();
    }

    private void Window_Deactivated(object sender, EventArgs e)
    {
        Hide();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Hide();
    }

    private void ProfileTab_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { Tag: ProfileViewModel profile }
            && DataContext is MainViewModel vm)
        {
            vm.SelectedProfile = profile;
        }
    }
}
