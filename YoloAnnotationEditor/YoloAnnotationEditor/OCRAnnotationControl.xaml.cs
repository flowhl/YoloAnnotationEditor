using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using OpenCvSharp;
using System.Globalization;
using System.Windows.Data;
using System.Text.RegularExpressions;
using System.Runtime.CompilerServices;
using MessageBox = System.Windows.MessageBox;
using UserControl = System.Windows.Controls.UserControl;
using Window = System.Windows.Window;
using Sdcb.PaddleOCR.Models;
using Sdcb.PaddleOCR;
using Sdcb.PaddleOCR.Models.Local;
using Sdcb.PaddleInference;

namespace YoloAnnotationEditor
{
    public partial class OCRAnnotationControl : UserControl, INotifyPropertyChanged
    {
        private AnnotationDataSet _dataSet;
        private bool _isDataModified = false;

        private PaddleOcrAll _ocr = null;
        private FullOcrModel _model = null;

        public AnnotationDataSet DataSet
        {
            get => _dataSet;
            set
            {
                _dataSet = value;
                OnPropertyChanged();
            }
        }

        // Event to notify parent window when data is modified
        public event EventHandler DataModified;

        public OCRAnnotationControl()
        {
            InitializeComponent();
            DataSet = new AnnotationDataSet();
            DataContext = DataSet;

            // Subscribe to selection changes and text changes
            DataSet.PropertyChanged += DataSet_PropertyChanged;

            try
            {
                // Load default OCR model (English V4)
                _model = LocalFullModels.EnglishV4;
                _ocr = new PaddleOcrAll(_model, device: PaddleDevice.Onnx());
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error initializing OCR model: {ex.Message}", "OCR Initialization Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }

            // Subscribe to parent window closing if available
            Loaded += (s, e) =>
        {
            var parentWindow = Window.GetWindow(this);
            if (parentWindow != null)
            {
                parentWindow.Closing += ParentWindow_Closing;
            }
        };
        }

        private void DataSet_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(AnnotationDataSet.SelectedAnnotation))
            {
                UpdateCurrentIndexText();
                SaveLabelsFile();
            }
        }

        private void UpdateCurrentIndexText()
        {
            if (DataSet.SelectedAnnotation != null && DataSet.Annotations.Count > 0)
            {
                int currentIndex = DataSet.Annotations.IndexOf(DataSet.SelectedAnnotation) + 1;
                CurrentIndexText.Text = $"Current: {currentIndex}";
            }
            else
            {
                CurrentIndexText.Text = "Current: -";
            }

            // Update labeled count
            int labeledCount = DataSet.Annotations.Count(a => a.HasLabel);
            int totalCount = DataSet.Annotations.Count;
            LabeledCountText.Text = $"Labeled: {labeledCount}/{totalCount}";
        }

        private async void OpenFolderButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new System.Windows.Forms.FolderBrowserDialog();
            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                await LoadFolderAsync(dialog.SelectedPath);
            }
        }

        private async Task LoadFolderAsync(string folderPath)
        {
            try
            {
                OpenFolderButton.IsEnabled = false;
                FolderPathText.Text = "Loading...";

                // Save current work if any
                if (_isDataModified && !string.IsNullOrEmpty(DataSet.FolderPath))
                {
                    SaveLabelsFile();
                }

                DataSet.FolderPath = folderPath;
                FolderPathText.Text = folderPath;

                // Check file types and handle conversions
                var imageFiles = Directory.GetFiles(folderPath, "*.*", SearchOption.TopDirectoryOnly)
                    .Where(f => IsImageFile(f)).ToList();

                if (!imageFiles.Any())
                {
                    MessageBox.Show("No image files found in the selected folder.", "No Images",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                // Convert PNG to JPG if needed
                var pngFiles = imageFiles.Where(f => Path.GetExtension(f).ToLower() == ".png").ToList();
                if (pngFiles.Any())
                {
                    var result = MessageBox.Show(
                        $"Found {pngFiles.Count} PNG files. Convert them to JPG?",
                        "Convert PNG to JPG",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Question);

                    if (result == MessageBoxResult.Yes)
                    {
                        await ConvertPngToJpgAsync(pngFiles);
                        // Refresh image files list
                        imageFiles = Directory.GetFiles(folderPath, "*.*", SearchOption.TopDirectoryOnly)
                            .Where(f => IsImageFile(f)).ToList();
                    }
                }

                // Check if JPGs need renaming to ascending numbers
                var jpgFiles = imageFiles.Where(f => Path.GetExtension(f).ToLower() == ".jpg" ||
                                                      Path.GetExtension(f).ToLower() == ".jpeg").ToList();

                if (jpgFiles.Any() && !AreFilesNumberedSequentially(jpgFiles))
                {
                    var result = MessageBox.Show(
                        "JPG files are not numbered sequentially (0.jpg, 1.jpg, etc.). Rename them?",
                        "Rename Files",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Question);

                    if (result == MessageBoxResult.Yes)
                    {
                        await RenameFilesToSequentialAsync(jpgFiles);
                        // Refresh image files list
                        imageFiles = Directory.GetFiles(folderPath, "*.*", SearchOption.TopDirectoryOnly)
                            .Where(f => IsImageFile(f)).ToList();
                    }
                }

                // Load labels file and create annotations
                await LoadAnnotationsAsync(folderPath, imageFiles);

                ImageCountText.Text = $"Images: {DataSet.Annotations.Count}";

                if (DataSet.Annotations.Any())
                {
                    DataSet.SelectedAnnotation = DataSet.Annotations.First();
                    UpdateCurrentIndexText(); // Update counts after loading
                    SaveLabelsFile();
                }

                _isDataModified = false;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading folder: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                OpenFolderButton.IsEnabled = true;
            }
        }

        private bool IsImageFile(string filePath)
        {
            var extension = Path.GetExtension(filePath).ToLower();
            return extension == ".jpg" || extension == ".jpeg" || extension == ".png";
        }

        private async Task ConvertPngToJpgAsync(List<string> pngFiles)
        {
            await Task.Run(() =>
            {
                foreach (var pngFile in pngFiles)
                {
                    try
                    {
                        using var image = Cv2.ImRead(pngFile);
                        var jpgPath = Path.ChangeExtension(pngFile, ".jpg");
                        Cv2.ImWrite(jpgPath, image);
                        File.Delete(pngFile); // Delete original PNG
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"Error converting {pngFile}: {ex.Message}", "Conversion Error");
                    }
                }
            });
        }

        private bool AreFilesNumberedSequentially(List<string> files)
        {
            var pattern = @"^(\d+)\.(jpg|jpeg)$";
            var numberedFiles = files
                .Select(f => Path.GetFileName(f))
                .Where(name => Regex.IsMatch(name, pattern, RegexOptions.IgnoreCase))
                .ToList();

            if (numberedFiles.Count != files.Count)
                return false;

            var numbers = numberedFiles
                .Select(name => int.Parse(Regex.Match(name, @"^(\d+)").Groups[1].Value))
                .OrderBy(n => n)
                .ToList();

            // Check if it starts from 0 and is sequential
            return numbers.Count > 0 && numbers[0] == 0 &&
                   numbers.SequenceEqual(Enumerable.Range(0, numbers.Count));
        }

        private async Task RenameFilesToSequentialAsync(List<string> files)
        {
            await Task.Run(() =>
            {
                var sortedFiles = files.OrderBy(f => Path.GetFileName(f)).ToList();
                var tempDir = Path.Combine(Path.GetDirectoryName(sortedFiles[0]), "temp_rename");
                Directory.CreateDirectory(tempDir);

                try
                {
                    // First move to temp directory with new names
                    for (int i = 0; i < sortedFiles.Count; i++)
                    {
                        var originalFile = sortedFiles[i];
                        var tempFile = Path.Combine(tempDir, $"{i}.jpg");
                        File.Move(originalFile, tempFile);
                    }

                    // Then move back to original directory
                    var tempFiles = Directory.GetFiles(tempDir, "*.jpg").OrderBy(f =>
                        int.Parse(Path.GetFileNameWithoutExtension(f))).ToList();

                    for (int i = 0; i < tempFiles.Count; i++)
                    {
                        var tempFile = tempFiles[i];
                        var finalFile = Path.Combine(Path.GetDirectoryName(sortedFiles[0]), $"{i}.jpg");
                        File.Move(tempFile, finalFile);
                    }
                }
                finally
                {
                    if (Directory.Exists(tempDir))
                        Directory.Delete(tempDir, true);
                }
            });
        }

        private async Task LoadAnnotationsAsync(string folderPath, List<string> imageFiles)
        {
            await Task.Run(() =>
            {
                var labelsFile = Path.Combine(folderPath, "labels.txt");
                var annotations = new Dictionary<string, string>();

                // Load existing labels
                if (File.Exists(labelsFile))
                {
                    var lines = File.ReadAllLines(labelsFile);
                    foreach (var line in lines)
                    {
                        if (!string.IsNullOrWhiteSpace(line))
                        {
                            var parts = line.Split(' ', 2);
                            if (parts.Length >= 1)
                            {
                                var filename = parts[0];
                                var text = parts.Length > 1 ? parts[1] : "";
                                annotations[filename] = text;
                            }
                        }
                    }
                }

                // Create annotation objects
                var sortedImageFiles = imageFiles
                    .OrderBy(f => GetNumericOrder(Path.GetFileName(f)))
                    .ToList();

                Dispatcher.Invoke(() =>
                {
                    DataSet.Annotations.Clear();
                    foreach (var imageFile in sortedImageFiles)
                    {
                        var filename = Path.GetFileName(imageFile);
                        var annotation = new ImageAnnotation
                        {
                            Filename = filename,
                            FullPath = imageFile,
                            Text = annotations.ContainsKey(filename) ? annotations[filename] : ""
                        };

                        annotation.TextChanged += OnAnnotationTextChanged;
                        DataSet.Annotations.Add(annotation);
                    }
                });
            });
        }

        private int GetNumericOrder(string filename)
        {
            var match = Regex.Match(filename, @"^(\d+)");
            return match.Success ? int.Parse(match.Groups[1].Value) : int.MaxValue;
        }

        private void OnAnnotationTextChanged(ImageAnnotation annotation)
        {
            _isDataModified = true;
            DataModified?.Invoke(this, EventArgs.Empty);

            // Update the labeled count when text changes
            UpdateCurrentIndexText();
        }

        private async void DetectOCRButton_Click(object sender, RoutedEventArgs e)
        {
            if (DataSet.SelectedAnnotation == null)
                return;

            try
            {
                DetectOCRButton.IsEnabled = false;
                DetectOCRButton.Content = "Detecting...";

                var result = await DetectWithOCR(DataSet.SelectedAnnotation.FullPath);
                DataSet.SelectedAnnotation.Text = result ?? "";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"OCR detection failed: {ex.Message}", "OCR Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                DetectOCRButton.IsEnabled = true;
                DetectOCRButton.Content = "Detect with OCR";
            }
        }


        private async Task<string> DetectWithOCR(string imagePath)
        {
            var img = new Mat(imagePath, ImreadModes.Color);
            var result = _ocr.Run(img);
            return result.Text;
        }

        private void PreviousButton_Click(object sender, RoutedEventArgs e)
        {
            if (DataSet.SelectedAnnotation == null || DataSet.Annotations.Count == 0)
                return;

            var currentIndex = DataSet.Annotations.IndexOf(DataSet.SelectedAnnotation);
            if (currentIndex > 0)
            {
                DataSet.SelectedAnnotation = DataSet.Annotations[currentIndex - 1];
                SaveLabelsFile();
            }
        }

        private void NextButton_Click(object sender, RoutedEventArgs e)
        {
            if (DataSet.SelectedAnnotation == null || DataSet.Annotations.Count == 0)
                return;

            var currentIndex = DataSet.Annotations.IndexOf(DataSet.SelectedAnnotation);
            if (currentIndex < DataSet.Annotations.Count - 1)
            {
                DataSet.SelectedAnnotation = DataSet.Annotations[currentIndex + 1];
                SaveLabelsFile();
            }
        }

        private void ParentWindow_Closing(object sender, CancelEventArgs e)
        {
            if (_isDataModified && !string.IsNullOrEmpty(DataSet.FolderPath))
            {
                SaveLabelsFile();
            }
        }

        public void SaveLabelsFile()
        {
            if (string.IsNullOrEmpty(DataSet.FolderPath))
                return;

            try
            {
                var labelsFile = DataSet.LabelsFilePath;
                var lines = new List<string>();

                foreach (var annotation in DataSet.Annotations.OrderBy(a => GetNumericOrder(a.Filename)))
                {
                    var line = $"{annotation.Filename} {annotation.Text ?? ""}".TrimEnd();
                    lines.Add(line);
                }

                File.WriteAllLines(labelsFile, lines, Encoding.UTF8);
                _isDataModified = false;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error saving labels file: {ex.Message}", "Save Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public class HasLabelToBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            bool hasLabel = (bool)value;
            return hasLabel ? System.Windows.Media.Brushes.Green : System.Windows.Media.Brushes.Red;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class HasLabelToSymbolConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            bool hasLabel = (bool)value;
            return hasLabel ? "✓" : "!";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class HasLabelToTooltipConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            bool hasLabel = (bool)value;
            return hasLabel ? "Has annotation" : "Missing annotation";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}