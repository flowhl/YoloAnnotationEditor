using OpenCvSharp;
using OpenCvSharp.Internal.Vectors;
using OpenTK.Graphics.OpenGL;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Windows.Threading;
using YoloDotNet;
using YoloDotNet.Enums;
using YoloDotNet.Models;
using MessageBox = System.Windows.MessageBox;
using UserControl = System.Windows.Controls.UserControl;

namespace YoloAnnotationEditor
{
    public partial class YoloPreview : UserControl
    {
        #region Fields
        private Yolo _yolo = null;
        private SKImage _currentFrame = null;
        private Dispatcher _dispatcher = null;
        private VideoCapture _capture = null;
        private bool _isRunning = false;
        private bool _runDetection = false;
        private Task _captureTask = null;
        private CancellationTokenSource _cancellationTokenSource = null;
        private SKRect _rect;
        private List<WebcamDevice> _webcamDevices = new List<WebcamDevice>();
        private List<ScreenDevice> _screenDevices = new List<ScreenDevice>();
        private int _selectedDeviceIndex = 0;
        private bool _isWebcamSource = true;
        #endregion

        #region Constants
        private const int DEFAULT_WIDTH = 1080;
        private const int DEFAULT_HEIGHT = 608;
        private const int FPS = 30;
        private const string FRAME_FORMAT_EXTENSION = ".png";
        #endregion

        #region Classes
        private class WebcamDevice
        {
            public int Index { get; set; }
            public string Name { get; set; }

            public override string ToString() => Name;
        }

        private class ScreenDevice
        {
            public int Index { get; set; }
            public System.Windows.Forms.Screen Screen { get; set; }

            public override string ToString() => $"Screen {Index + 1}: {Screen.Bounds.Width}x{Screen.Bounds.Height}";
        }
        #endregion

        public YoloPreview()
        {
            InitializeComponent();

            _dispatcher = Dispatcher.CurrentDispatcher;

            // Initialize default frame
            _currentFrame = SKImage.FromBitmap(new SKBitmap(DEFAULT_WIDTH, DEFAULT_HEIGHT));
            _rect = new SKRect(0, 0, DEFAULT_WIDTH, DEFAULT_HEIGHT);

            Loaded += YoloPreview_Loaded;
            Unloaded += YoloPreview_Unloaded;
        }

        private void YoloPreview_Loaded(object sender, RoutedEventArgs e)
        {
            // Populate devices
            PopulateDevices();

            // Set initial device selection to webcams
            UpdateDeviceComboBox();
        }

        private void YoloPreview_Unloaded(object sender, RoutedEventArgs e)
        {
            StopCapture();
            DisposeResources();
        }

        private void PopulateDevices()
        {
            PopulateWebcamDevices();
            PopulateScreenDevices();
        }

        private void PopulateWebcamDevices()
        {
            _webcamDevices.Clear();

            // Enumerate webcams
            int deviceCount = 10; // Try up to 10 devices
            for (int i = 0; i < deviceCount; i++)
            {
                try
                {
                    using (var capture = new VideoCapture(i))
                    {
                        if (capture.IsOpened())
                        {
                            string deviceName = $"Camera {i + 1}";
                            _webcamDevices.Add(new WebcamDevice { Index = i, Name = deviceName });
                        }
                    }
                }
                catch { /* Device not available */ }
            }
        }

        private void PopulateScreenDevices()
        {
            _screenDevices.Clear();

            // Enumerate screens
            var screens = System.Windows.Forms.Screen.AllScreens;
            for (int i = 0; i < screens.Length; i++)
            {
                _screenDevices.Add(new ScreenDevice { Index = i, Screen = screens[i] });
            }
        }

        private void UpdateDeviceComboBox()
        {
            if (DeviceComboBox == null) return;

            DeviceComboBox.Items.Clear();

            if (_isWebcamSource)
            {
                foreach (var device in _webcamDevices)
                {
                    DeviceComboBox.Items.Add(device);
                }
            }
            else
            {
                foreach (var device in _screenDevices)
                {
                    DeviceComboBox.Items.Add(device);
                }
            }

            if (DeviceComboBox.Items.Count > 0)
            {
                DeviceComboBox.SelectedIndex = 0;
            }
        }

        private void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            PopulateDevices();
            UpdateDeviceComboBox();
        }

        private void BrowseButton_Click(object sender, RoutedEventArgs e)
        {
            var openFileDialog = new OpenFileDialog
            {
                Filter = "ONNX Models (*.onnx)|*.onnx|All files (*.*)|*.*",
                Title = "Select ONNX Model"
            };

            if (openFileDialog.ShowDialog() == DialogResult.OK)
            {
                OnnxPathTextBox.Text = openFileDialog.FileName;

                // Dispose existing YOLO model if exists
                _yolo?.Dispose();
                _yolo = null;

                try
                {
                    // Create new YOLO model
                    _yolo = new Yolo(new YoloOptions()
                    {
                        OnnxModel = openFileDialog.FileName,
                        ModelType = ModelType.ObjectDetection,
                        Cuda = true,
                        PrimeGpu = false
                    });

                    MessageBox.Show("YOLO model loaded successfully.", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Failed to load YOLO model: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    OnnxPathTextBox.Text = string.Empty;
                }
            }
        }

        private void SourceType_Changed(object sender, RoutedEventArgs e)
        {
            _isWebcamSource = WebcamRadioButton.IsChecked ?? true;
            UpdateDeviceComboBox();
            StopCapture();
        }

        private void DeviceComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            _selectedDeviceIndex = DeviceComboBox.SelectedIndex;

            if (_isRunning)
            {
                StopCapture();
                StartCapture();
            }
        }

        private void StartButton_Click(object sender, RoutedEventArgs e)
        {
            bool detectionEnabled = EnableDetectionCheckBox.IsChecked ?? true;

            if (detectionEnabled && _yolo == null)
            {
                MessageBox.Show("Please select a YOLO model for detection.", "Warning",
                                MessageBoxButton.OK, MessageBoxImage.Warning);
                // Set checkbox to disabled since we don't have a model
                EnableDetectionCheckBox.IsChecked = false;
                detectionEnabled = false;
            }

            if (DeviceComboBox.SelectedItem == null)
            {
                MessageBox.Show("Please select a device first.", "Error",
                               MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            // Set the detection flag based on checkbox and model availability
            _runDetection = detectionEnabled;

            StartCapture();
            StartButton.IsEnabled = false;
            StopButton.IsEnabled = true;
        }

        private void StopButton_Click(object sender, RoutedEventArgs e)
        {
            StopCapture();
            StartButton.IsEnabled = true;
            StopButton.IsEnabled = false;
        }

        private void StartCapture()
        {
            if (_isRunning)
                return;

            _cancellationTokenSource = new CancellationTokenSource();
            _isRunning = true;

            _captureTask = Task.Run(() => CaptureAsync(_cancellationTokenSource.Token));
        }

        private void StopCapture()
        {
            if (!_isRunning)
                return;

            _isRunning = false;
            _cancellationTokenSource?.Cancel();

            try
            {
                _captureTask?.Wait();
            }
            catch (AggregateException) { /* Task was canceled */ }

            _capture?.Dispose();
            _capture = null;
        }

        private async Task CaptureAsync(CancellationToken cancellationToken)
        {
            if (_isWebcamSource)
            {
                await CaptureWebcamAsync(cancellationToken);
            }
            else
            {
                await CaptureScreenAsync(cancellationToken);
            }
        }

        private async Task CaptureWebcamAsync(CancellationToken cancellationToken)
        {
            if (_selectedDeviceIndex < 0 || _selectedDeviceIndex >= _webcamDevices.Count)
                return;

            int deviceIndex = _webcamDevices[_selectedDeviceIndex].Index;

            // Configure webcam
            _capture = new VideoCapture(deviceIndex);
            _capture.Set(VideoCaptureProperties.Fps, FPS);
            _capture.Set(VideoCaptureProperties.FrameWidth, DEFAULT_WIDTH);
            _capture.Set(VideoCaptureProperties.FrameHeight, DEFAULT_HEIGHT);

            var mat = new Mat();

            while (!cancellationToken.IsCancellationRequested && _isRunning)
            {
                // Capture current frame from webcam
                _capture.Read(mat);

                if (mat.Empty())
                    continue;

                // Convert mat to byte array
                byte[] imageBytes;
                using (var ms = new MemoryStream())
                {
                    imageBytes = mat.ImEncode(FRAME_FORMAT_EXTENSION);
                }

                // Read buffer to an SKImage
                var newFrame = SKImage.FromEncodedData(imageBytes);

                // Only run detection if enabled AND we have a model
                if (_runDetection && _yolo != null)
                {
                    // Run inference on frame
                    List<ObjectDetection> results = _yolo.RunObjectDetection(newFrame);

                    // Draw results
                    newFrame = DrawDetections(newFrame, results);
                }

                // Update current frame and dispose old one
                var oldFrame = _currentFrame;
                _currentFrame = newFrame;
                oldFrame?.Dispose();

                // Update GUI
                await _dispatcher.InvokeAsync(() => VideoFeedFrame.InvalidateVisual());

                // Add a small delay to control CPU usage
                await Task.Delay(1000 / FPS, cancellationToken);
            }

            mat?.Dispose();
        }

        private async Task CaptureScreenAsync(CancellationToken cancellationToken)
        {
            if (_selectedDeviceIndex < 0 || _selectedDeviceIndex >= _screenDevices.Count)
                return;

            var screen = _screenDevices[_selectedDeviceIndex].Screen;

            var bitmap = new Bitmap(screen.Bounds.Width, screen.Bounds.Height);
            var graphics = Graphics.FromImage(bitmap);

            var mat = new Mat();

            while (!cancellationToken.IsCancellationRequested && _isRunning)
            {
                // Capture screen
                graphics.CopyFromScreen(
                    screen.Bounds.X,
                    screen.Bounds.Y,
                    0,
                    0,
                    bitmap.Size);

                // Convert bitmap to Mat
                using (var tempMat = OpenCvSharp.Extensions.BitmapConverter.ToMat(bitmap))
                {
                    // Resize if needed
                    if (tempMat.Width != DEFAULT_WIDTH || tempMat.Height != DEFAULT_HEIGHT)
                    {
                        Cv2.Resize(tempMat, mat, new OpenCvSharp.Size(DEFAULT_WIDTH, DEFAULT_HEIGHT));
                    }
                    else
                    {
                        tempMat.CopyTo(mat);
                    }
                }

                // Convert mat to byte array
                byte[] imageBytes;
                imageBytes = mat.ImEncode(FRAME_FORMAT_EXTENSION);

                // Read buffer to an SKImage
                var newFrame = SKImage.FromEncodedData(imageBytes);

                // Only run detection if enabled AND we have a model
                if (_runDetection && _yolo != null)
                {
                    // Run inference on frame
                    List<ObjectDetection> results = _yolo.RunObjectDetection(newFrame);

                    // Draw results
                    newFrame = DrawDetections(newFrame, results);
                }

                // Update current frame and dispose old one
                var oldFrame = _currentFrame;
                _currentFrame = newFrame;
                oldFrame?.Dispose();

                // Update GUI
                await _dispatcher.InvokeAsync(() => VideoFeedFrame.InvalidateVisual());

                // Add a small delay to control CPU usage
                await Task.Delay(1000 / FPS, cancellationToken);
            }

            mat?.Dispose();
            graphics?.Dispose();
            bitmap?.Dispose();
        }

        private SKImage DrawDetections(SKImage image, List<ObjectDetection> detections)
        {
            // Create a surface to draw on
            using var surface = SKSurface.Create(new SKImageInfo(image.Width, image.Height));
            var canvas = surface.Canvas;

            // Draw the image
            canvas.DrawImage(image, 0, 0);

            // Draw detection rectangles and labels
            foreach (var detection in detections)
            {
                using var paint = new SKPaint
                {
                    Style = SKPaintStyle.Stroke,
                    Color = SKColors.Red,
                    StrokeWidth = 2
                };

                // Draw bounding box
                var rect = detection.BoundingBox;
                canvas.DrawRect(rect, paint);

                // Draw label with confidence
                using var textPaint = new SKPaint
                {
                    Style = SKPaintStyle.Fill,
                    Color = SKColors.Red,
                    TextSize = 16
                };

                var text = $"{detection.Label.Name} :  {detection.Confidence:P1}";
                canvas.DrawText(text, rect.Left, rect.Top - 5, textPaint);
            }

            return surface.Snapshot();
        }

        private void UpdateVideoFeedFrame(object sender, SkiaSharp.Views.Desktop.SKPaintSurfaceEventArgs e)
        {
            using var canvas = e.Surface.Canvas;
            canvas.DrawImage(_currentFrame, _rect);
            canvas.Flush();
        }

        private void DisposeResources()
        {
            _yolo?.Dispose();
            _yolo = null;

            _currentFrame?.Dispose();
            _currentFrame = null;
        }

        private void EnableDetectionCheckBox_CheckedChanged(object sender, RoutedEventArgs e)
        {
            bool detectionEnabled = EnableDetectionCheckBox.IsChecked ?? true;

            if (detectionEnabled && _yolo == null && this.IsLoaded)
            {
                MessageBox.Show("Please select a YOLO model first to enable detection.",
                                "Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
                // Prevent enabling detection without a model
                EnableDetectionCheckBox.IsChecked = false;
                return;
            }

            // Update the detection flag if we're currently running
            if (_isRunning)
            {
                _runDetection = detectionEnabled;
            }
        }
    }
}