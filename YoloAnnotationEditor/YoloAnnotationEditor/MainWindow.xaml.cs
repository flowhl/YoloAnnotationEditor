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
using Velopack;
using YoloAnnotationEditor.Helpers;

namespace YoloAnnotationEditor;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    public MainWindow()
    {
        Loaded += MainWindow_Loaded;
        VelopackApp.Build().SetLogger(WpfVelopackLogManager.Logger).Run();
        InitializeComponent();
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
#if DEBUG
#else
                AppUpdateManager.CheckForUpdates();
#endif
        }
        catch (Exception ex)
        {
            Notify.sendWarn("Error checking for updates: " + ex.Message);
        }
    }
}