using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Velopack.Logging;

namespace YoloAnnotationEditor.Helpers
{
    public static class WpfVelopackLogManager
    {
        private static WpfVelopackLogger _logger;
        public static WpfVelopackLogger Logger
        {
            get
            {
                if (_logger == null)
                {
                    _logger = new WpfVelopackLogger();
                }
                return _logger;
            }
        }
    }

    public class WpfVelopackLogger : IVelopackLogger, INotifyPropertyChanged
    {
        public string LogMessage { get; private set; } = string.Empty;

        public event PropertyChangedEventHandler? PropertyChanged;

        //custom event when log message is updated
        public event EventHandler<string>? LogUpdated;

        public void Log(VelopackLogLevel logLevel, string? message, Exception? exception)
        {
            LogMessage = $"{message}";
            Trace.WriteLine($"[{logLevel}] {message} {exception?.Message}");
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(LogMessage)));
            LogUpdated?.Invoke(this, LogMessage);
        }
    }
}
