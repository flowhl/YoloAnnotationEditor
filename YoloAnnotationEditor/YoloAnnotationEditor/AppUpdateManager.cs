using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Printing;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using Velopack;
using Velopack.Locators;
using Velopack.Logging;
using Velopack.Sources;
using YoloAnnotationEditor.Helpers;
using MessageBox = System.Windows.MessageBox;

namespace YoloAnnotationEditor
{
    public static class AppUpdateManager
    {
        public static void CheckForUpdates()
        {
            UpdateProgressWindow progressWindow = null;

            try
            {
                Trace.WriteLine("Checking for updates");
                //Create logger
                var locator = VelopackLocator.CreateDefaultForPlatform(WpfVelopackLogManager.Logger);

                //Create updater
                var githubSource = new GithubSource("https://github.com/flowhl/YoloAnnotationEditor", null, false);
                var mgr = new UpdateManager(githubSource, new UpdateOptions { AllowVersionDowngrade = false }, locator);

                // Show progress window for checking updates
                progressWindow = new UpdateProgressWindow();
                progressWindow.Show();
                progressWindow.UpdateStatus("Checking for updates...");
                progressWindow.SetIndeterminate(true);

                // check for new version
                var newVersion = mgr.CheckForUpdates();
                Trace.WriteLine($"Found Version {newVersion?.TargetFullRelease?.Version.ToString()}");
                if (newVersion == null)
                {
                    Trace.WriteLine($"New Version is null, closing update window");
                    progressWindow.Close();
                    return; // no update available
                }

                progressWindow.Close();

                //ask user if they want to update
                var result = MessageBox.Show($"New version {newVersion?.TargetFullRelease?.Version.ToString()} available. Do you want to update?", "Update available", MessageBoxButton.YesNo);

                if (result == MessageBoxResult.No)
                    return;

                // Reopen progress window for download and installation
                progressWindow = new UpdateProgressWindow();
                progressWindow.Show();
                progressWindow.UpdateStatus("Downloading update...");
                progressWindow.UpdateProgress(0, $"Downloading version {newVersion?.TargetFullRelease?.Version.ToString()}");
                Trace.WriteLine($"Starting download of version {newVersion?.TargetFullRelease?.Version.ToString()}");

                //Subscribe to log events
                WpfVelopackLogManager.Logger.LogUpdated += (s, m) =>
                {
                    progressWindow?.UpdateStatus(m);
                };

                // download new version with progress tracking
                var updateTask =  mgr.DownloadUpdatesAsync(newVersion, progress =>
                {
                    Trace.WriteLine($"Download progress: {progress}%");
                    progressWindow.UpdateProgress(progress, $"Downloaded {progress}%");
                });

                Trace.WriteLine("Waiting for download to complete");
                updateTask.Wait();
                Trace.WriteLine("Download completed");

                progressWindow.UpdateStatus("Installing update...");
                progressWindow.SetIndeterminate(true);

                // install new version and restart app
                mgr.ApplyUpdatesAndRestart(newVersion);
            }
            catch (Exception ex)
            {
                progressWindow?.Close();
                MessageBox.Show($"Update failed: {ex.Message}", "Update Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
