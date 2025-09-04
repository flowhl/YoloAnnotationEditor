using System.Configuration;
using System.Data;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using Application = System.Windows.Application;
using MessageBox = System.Windows.MessageBox;

namespace YoloAnnotationEditor;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        SetupExceptionHandling();
        base.OnStartup(e);
    }

    private void SetupExceptionHandling()
    {
        // Handle UI thread exceptions
        this.DispatcherUnhandledException += App_DispatcherUnhandledException;

        // Handle non-UI thread exceptions
        AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;

        // Handle Task exceptions (async/await)
        TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;
    }

    // UI Thread exceptions (most common in WPF)
    private void App_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        var exception = e.Exception;

        // Show user-friendly message
        var result = MessageBox.Show(
            $"An unexpected error occurred:\n\n{GetUserFriendlyMessage(exception)}\n\nWould you like to continue? Click 'No' to exit the application.",
            "Unexpected Error",
            MessageBoxButton.YesNo,
            MessageBoxImage.Error);

        if (result == MessageBoxResult.No)
        {
            Current.Shutdown();
        }
        else
        {
            // Mark as handled so the app doesn't crash
            e.Handled = true;
        }
    }

    // Background thread exceptions
    private void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        var exception = e.ExceptionObject as Exception;

        // Show critical error message
        MessageBox.Show(
            $"A critical error occurred and the application must close:\n\n{GetUserFriendlyMessage(exception)}",
            "Critical Error",
            MessageBoxButton.OK,
            MessageBoxImage.Error);

        // App will terminate after this
    }

    // Unobserved Task exceptions (async/await without proper error handling)
    private void TaskScheduler_UnobservedTaskException(object sender, UnobservedTaskExceptionEventArgs e)
    {
        // Mark as observed to prevent app termination
        e.SetObserved();

        // Show warning to user
        Dispatcher.BeginInvoke(new Action(() =>
        {
            MessageBox.Show(
                $"A background operation failed:\n\n{GetUserFriendlyMessage(e.Exception.GetBaseException())}",
                "Background Operation Failed",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }));
    }

    private string GetUserFriendlyMessage(Exception exception)
    {
        // Provide user-friendly messages for common exceptions
        return exception switch
        {
            FileNotFoundException => "A required file was not found. Please check if all necessary files are present.",
            DirectoryNotFoundException => "A required folder was not found. Please check the file paths.",
            UnauthorizedAccessException => "Permission denied. Please run as administrator or check file permissions.",
            OutOfMemoryException => "The application ran out of memory. Try closing other applications.",
            ArgumentException => "Invalid input provided. Please check your data and try again.",
            InvalidOperationException => "The operation could not be completed. Please try again.",
            TimeoutException => "The operation timed out. Please check your network connection and try again.",
            _ => $"{exception?.GetType().Name}: {exception?.Message}"
        };
    }
}

