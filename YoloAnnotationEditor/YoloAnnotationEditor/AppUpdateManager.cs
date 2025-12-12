using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Printing;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using Velopack.Sources;
using Velopack;
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
                var githubSource = new GithubSource("https://github.com/flowhl/YoloAnnotationEditor", null, false);
                var mgr = new UpdateManager(githubSource, new UpdateOptions { AllowVersionDowngrade = false });

                // Show progress window for checking updates
                progressWindow = new UpdateProgressWindow();
                progressWindow.Show();
                progressWindow.UpdateStatus("Checking for updates...");
                progressWindow.SetIndeterminate(true);

                // check for new version
                var newVersion = mgr.CheckForUpdates();
                if (newVersion == null)
                {
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

                // download new version with progress tracking
                mgr.DownloadUpdates(newVersion, progress =>
                {
                    progressWindow.UpdateProgress(progress, $"Downloaded {progress}%");
                });

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
