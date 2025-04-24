using Notifications.Wpf;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace YoloAnnotationEditor
{
    public static class Notify
    {
        private static bool endLogging = false;
        public static NotificationManager NotifyManager = new NotificationManager();
        public static void sendInfo(string message)
        {
            if (endLogging) return;
            NotifyManager.Show(new NotificationContent
            {
                Title = "Info",
                Message = message,
                Type = NotificationType.Information
            });
        }
        public static void sendWarn(string message)
        {
            if (endLogging) return;
            NotifyManager.Show(new NotificationContent
            {
                Title = "Warning!",
                Message = message,
                Type = NotificationType.Warning
            });
        }
        public static void sendSuccess(string message)
        {
            if (endLogging) return;
            NotifyManager.Show(new NotificationContent
            {
                Title = "Success!",
                Message = message,
                Type = NotificationType.Success
            });
        }
        public static void sendError(string message)
        {
            if (endLogging) return;
            NotifyManager.Show(new NotificationContent
            {
                Title = "Error!",
                Message = message,
                Type = NotificationType.Error
            });
        }
        public static void EndLogging()
        {
            endLogging = true;
        }
        public static void ResumeLogging()
        {
            endLogging = false;
        }
        public static void CleanUp()
        {
            var Window = NotifyManager?.GetType()?.GetField("_window", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
            var windowInstance = Window?.GetValue(Window) as NotificationsOverlayWindow;
            windowInstance?.Close();
        }
    }
}
