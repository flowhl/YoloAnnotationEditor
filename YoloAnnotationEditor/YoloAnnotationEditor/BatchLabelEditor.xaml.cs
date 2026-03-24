using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using YoloAnnotationEditor.Models;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using MessageBox = System.Windows.MessageBox;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using OpenFileDialog = System.Windows.Forms.OpenFileDialog;
using Path = System.IO.Path;
using Point = System.Windows.Point;
using Rectangle = System.Windows.Shapes.Rectangle;
using UserControl = System.Windows.Controls.UserControl;

namespace YoloAnnotationEditor
{
    public class BatchImageItem : INotifyPropertyChanged
    {
        private bool _isSelected;

        public string FileName { get; set; }
        public string FilePath { get; set; }
        public string LabelPath { get; set; }
        public BitmapImage Thumbnail { get; set; }
        public int AnnotationCount { get; set; }

        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected != value)
                {
                    _isSelected = value;
                    OnPropertyChanged();
                }
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public partial class BatchLabelEditor : Window
    {
        private int _currentStep = 1;

        // Data
        private readonly ObservableCollection<BatchImageItem> _allBatchImages = new();
        private readonly ObservableCollection<ClassItem> _allClasses = new();
        private readonly Dictionary<int, string> _classNames;
        private readonly Dictionary<int, SolidColorBrush> _classColors;

        // Drawing state
        private bool _isDrawing;
        private Point _startPoint;
        private Rectangle _selectionRect;

        // Region in normalized coordinates (0-1)
        private double _regionLeft;
        private double _regionTop;
        private double _regionRight;
        private double _regionBottom;
        private bool _hasRegion;

        // Selected class
        private ClassItem _selectedClass;

        // Results tracking
        private bool _operationCompleted;
        private int _totalLabelsChanged;
        private int _totalFilesModified;

        // Reference to parent for reloading
        private readonly Action _onCompleteCallback;

        public BatchLabelEditor(
            IEnumerable<ImageItem> images,
            Dictionary<int, string> classNames,
            Dictionary<int, SolidColorBrush> classColors,
            Action onCompleteCallback = null)
        {
            InitializeComponent();

            _classNames = classNames;
            _classColors = classColors;
            _onCompleteCallback = onCompleteCallback;

            // Populate image list
            foreach (var img in images)
            {
                var batchItem = new BatchImageItem
                {
                    FileName = img.FileName,
                    FilePath = img.FilePath,
                    LabelPath = img.LabelPath,
                    Thumbnail = img.Thumbnail,
                    AnnotationCount = img.Annotations.Count,
                    IsSelected = false
                };
                batchItem.PropertyChanged += BatchItem_PropertyChanged;
                _allBatchImages.Add(batchItem);
            }

            LvImages.ItemsSource = _allBatchImages;

            // Populate class list
            foreach (var entry in classNames.OrderBy(c => c.Key))
            {
                _allClasses.Add(new ClassItem
                {
                    ClassId = entry.Key,
                    Name = entry.Value,
                    Color = classColors.ContainsKey(entry.Key) ? classColors[entry.Key] : System.Windows.Media.Brushes.Red
                });
            }

            LbClasses.ItemsSource = _allClasses;

            UpdateSelectionCount();
            UpdateStepIndicators();
        }

        #region Step Navigation

        private void BtnNext_Click(object sender, RoutedEventArgs e)
        {
            if (!ValidateCurrentStep()) return;
            SetStep(_currentStep + 1);
        }

        private void BtnBack_Click(object sender, RoutedEventArgs e)
        {
            if (_operationCompleted) return;
            SetStep(_currentStep - 1);
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            if (_operationCompleted)
            {
                DialogResult = true;
            }
            Close();
        }

        private bool ValidateCurrentStep()
        {
            switch (_currentStep)
            {
                case 1:
                    int selectedCount = _allBatchImages.Count(i => i.IsSelected);
                    if (selectedCount == 0)
                    {
                        MessageBox.Show("Please select at least one image.", "No Images Selected",
                            MessageBoxButton.OK, MessageBoxImage.Warning);
                        return false;
                    }
                    return true;

                case 2:
                    if (!_hasRegion)
                    {
                        MessageBox.Show("Please draw a bounding box region on the reference image.",
                            "No Region Defined", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return false;
                    }
                    return true;

                case 3:
                    if (_selectedClass == null)
                    {
                        MessageBox.Show("Please select a target class.", "No Class Selected",
                            MessageBoxButton.OK, MessageBoxImage.Warning);
                        return false;
                    }
                    return true;

                default:
                    return true;
            }
        }

        private void SetStep(int step)
        {
            _currentStep = Math.Clamp(step, 1, 4);

            // Show/hide panels
            Step1Panel.Visibility = _currentStep == 1 ? Visibility.Visible : Visibility.Collapsed;
            Step2Panel.Visibility = _currentStep == 2 ? Visibility.Visible : Visibility.Collapsed;
            Step3Panel.Visibility = _currentStep == 3 ? Visibility.Visible : Visibility.Collapsed;
            Step4Panel.Visibility = _currentStep == 4 ? Visibility.Visible : Visibility.Collapsed;

            // Update navigation buttons
            BtnBack.IsEnabled = _currentStep > 1 && !_operationCompleted;
            BtnNext.Visibility = _currentStep < 4 ? Visibility.Visible : Visibility.Collapsed;
            BtnApply.Visibility = _currentStep == 4 && !_operationCompleted ? Visibility.Visible : Visibility.Collapsed;

            // Update status text
            switch (_currentStep)
            {
                case 1:
                    TxtStatus.Text = "Step 1: Select images to modify";
                    break;
                case 2:
                    TxtStatus.Text = "Step 2: Draw a region on the reference image";
                    PopulateReferenceImageList();
                    break;
                case 3:
                    TxtStatus.Text = "Step 3: Choose the new class for labels in the region";
                    UpdatePreview();
                    break;
                case 4:
                    TxtStatus.Text = "Step 4: Review and apply changes";
                    PrepareConfirmation();
                    break;
            }

            UpdateStepIndicators();
        }

        private void UpdateStepIndicators()
        {
            var activeColor = new SolidColorBrush(Color.FromRgb(0x21, 0x96, 0xF3)); // Blue
            var completedColor = new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50)); // Green
            var inactiveColor = new SolidColorBrush(Color.FromRgb(0xD0, 0xD0, 0xD0)); // Light gray

            Step1Indicator.Background = _currentStep == 1 ? activeColor : (_currentStep > 1 ? completedColor : inactiveColor);
            Step2Indicator.Background = _currentStep == 2 ? activeColor : (_currentStep > 2 ? completedColor : inactiveColor);
            Step3Indicator.Background = _currentStep == 3 ? activeColor : (_currentStep > 3 ? completedColor : inactiveColor);
            Step4Indicator.Background = _currentStep == 4 ? activeColor : inactiveColor;

            var activeTextColor = Brushes.White;
            var inactiveTextColor = new SolidColorBrush(Color.FromRgb(0x90, 0x90, 0x90));

            Step2Text.Foreground = _currentStep >= 2 ? activeTextColor : inactiveTextColor;
            Step3Text.Foreground = _currentStep >= 3 ? activeTextColor : inactiveTextColor;
            Step4Text.Foreground = _currentStep >= 4 ? activeTextColor : inactiveTextColor;
        }

        #endregion

        #region Step 1: Image Selection

        private void BtnSelectAll_Click(object sender, RoutedEventArgs e)
        {
            foreach (var item in GetFilteredImages())
                item.IsSelected = true;
            UpdateSelectionCount();
        }

        private void BtnSelectNone_Click(object sender, RoutedEventArgs e)
        {
            foreach (var item in GetFilteredImages())
                item.IsSelected = false;
            UpdateSelectionCount();
        }

        private void BtnInvertSelection_Click(object sender, RoutedEventArgs e)
        {
            foreach (var item in GetFilteredImages())
                item.IsSelected = !item.IsSelected;
            UpdateSelectionCount();
        }

        private void TxtImageFilter_TextChanged(object sender, TextChangedEventArgs e)
        {
            string filter = TxtImageFilter.Text?.ToLowerInvariant() ?? "";
            if (string.IsNullOrEmpty(filter))
            {
                LvImages.ItemsSource = _allBatchImages;
            }
            else
            {
                LvImages.ItemsSource = new ObservableCollection<BatchImageItem>(
                    _allBatchImages.Where(i => i.FileName.ToLowerInvariant().Contains(filter)));
            }
        }

        private IEnumerable<BatchImageItem> GetFilteredImages()
        {
            if (LvImages.ItemsSource is ObservableCollection<BatchImageItem> filtered)
                return filtered;
            return _allBatchImages;
        }

        private void BatchItem_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(BatchImageItem.IsSelected))
            {
                UpdateSelectionCount();
            }
        }

        private void UpdateSelectionCount()
        {
            int count = _allBatchImages.Count(i => i.IsSelected);
            TxtSelectionCount.Text = $"{count} of {_allBatchImages.Count} images selected";
        }

        #endregion

        #region Step 2: Draw Region

        private void PopulateReferenceImageList()
        {
            var selectedImages = _allBatchImages.Where(i => i.IsSelected).ToList();
            LvReferenceImages.ItemsSource = selectedImages;
            if (selectedImages.Count > 0)
            {
                LvReferenceImages.SelectedIndex = 0;
            }
        }

        private void LvReferenceImages_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (LvReferenceImages.SelectedItem is BatchImageItem item)
            {
                LoadReferenceImage(item);
            }
        }

        private void LoadReferenceImage(BatchImageItem item)
        {
            try
            {
                var image = new BitmapImage();
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.UriSource = new Uri(item.FilePath);
                image.EndInit();

                ReferenceImage.Source = image;
                DrawingCanvas.Width = image.PixelWidth;
                DrawingCanvas.Height = image.PixelHeight;

                // Draw existing annotations
                DrawingCanvas.Children.Clear();
                DrawExistingAnnotations(item.LabelPath, image.PixelWidth, image.PixelHeight);

                // Redraw the region if we have one
                if (_hasRegion)
                {
                    DrawRegionRectangle(image.PixelWidth, image.PixelHeight);
                }
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"Error loading reference image: {ex.Message}");
            }
        }

        private void DrawExistingAnnotations(string labelPath, double imageWidth, double imageHeight)
        {
            if (!File.Exists(labelPath)) return;

            var lines = File.ReadAllLines(labelPath);
            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;

                var label = ParseYoloLabel(line);
                if (label == null) continue;

                double x = label.CenterX * imageWidth;
                double y = label.CenterY * imageHeight;
                double w = label.Width * imageWidth;
                double h = label.Height * imageHeight;
                double left = x - w / 2;
                double top = y - h / 2;

                Brush brush = _classColors.ContainsKey(label.ClassId)
                    ? _classColors[label.ClassId]
                    : Brushes.Red;

                var rect = new Rectangle
                {
                    Width = w,
                    Height = h,
                    Stroke = brush,
                    StrokeThickness = 2,
                    Fill = new SolidColorBrush(Color.FromArgb(30,
                        ((SolidColorBrush)brush).Color.R,
                        ((SolidColorBrush)brush).Color.G,
                        ((SolidColorBrush)brush).Color.B))
                };

                Canvas.SetLeft(rect, left);
                Canvas.SetTop(rect, top);
                DrawingCanvas.Children.Add(rect);

                if (_classNames.ContainsKey(label.ClassId))
                {
                    var text = new TextBlock
                    {
                        Text = _classNames[label.ClassId],
                        Foreground = Brushes.White,
                        Background = brush,
                        Padding = new Thickness(2),
                        FontSize = 11
                    };
                    Canvas.SetLeft(text, left);
                    Canvas.SetTop(text, top - 15);
                    DrawingCanvas.Children.Add(text);
                }
            }
        }

        private void Canvas_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (ReferenceImage.Source == null) return;

            _startPoint = e.GetPosition(DrawingCanvas);
            _isDrawing = true;

            // Remove previous selection rectangle
            RemoveSelectionRectangle();

            _selectionRect = new Rectangle
            {
                Stroke = Brushes.Red,
                StrokeThickness = 3,
                StrokeDashArray = new DoubleCollection { 6, 3 },
                Fill = new SolidColorBrush(Color.FromArgb(40, 255, 0, 0))
            };

            Canvas.SetLeft(_selectionRect, _startPoint.X);
            Canvas.SetTop(_selectionRect, _startPoint.Y);
            _selectionRect.Width = 0;
            _selectionRect.Height = 0;
            _selectionRect.Tag = "SelectionRect";

            DrawingCanvas.Children.Add(_selectionRect);
            e.Handled = true;
        }

        private void Canvas_MouseMove(object sender, MouseEventArgs e)
        {
            if (!_isDrawing || _selectionRect == null) return;

            var currentPoint = e.GetPosition(DrawingCanvas);

            double x = Math.Min(currentPoint.X, _startPoint.X);
            double y = Math.Min(currentPoint.Y, _startPoint.Y);
            double width = Math.Abs(currentPoint.X - _startPoint.X);
            double height = Math.Abs(currentPoint.Y - _startPoint.Y);

            Canvas.SetLeft(_selectionRect, x);
            Canvas.SetTop(_selectionRect, y);
            _selectionRect.Width = width;
            _selectionRect.Height = height;

            TxtRegionInfo.Text = $"Region: {width:F0} × {height:F0} px";
            e.Handled = true;
        }

        private void Canvas_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (!_isDrawing || _selectionRect == null) return;
            _isDrawing = false;

            if (_selectionRect.Width < 5 || _selectionRect.Height < 5)
            {
                RemoveSelectionRectangle();
                _hasRegion = false;
                TxtRegionInfo.Text = "Region too small, try again";
                return;
            }

            // Convert to normalized coordinates
            if (ReferenceImage.Source is BitmapImage image)
            {
                double imgW = image.PixelWidth;
                double imgH = image.PixelHeight;

                double left = Canvas.GetLeft(_selectionRect);
                double top = Canvas.GetTop(_selectionRect);

                _regionLeft = Math.Max(0, left / imgW);
                _regionTop = Math.Max(0, top / imgH);
                _regionRight = Math.Min(1, (left + _selectionRect.Width) / imgW);
                _regionBottom = Math.Min(1, (top + _selectionRect.Height) / imgH);

                _hasRegion = true;

                // Count how many labels fall in this region across selected images
                int labelCount = CountLabelsInRegion();
                TxtRegionInfo.Text = $"Region defined ({_selectionRect.Width:F0}×{_selectionRect.Height:F0} px) — {labelCount} labels found in region across selected images";
            }

            e.Handled = true;
        }

        private void RemoveSelectionRectangle()
        {
            for (int i = DrawingCanvas.Children.Count - 1; i >= 0; i--)
            {
                if (DrawingCanvas.Children[i] is Rectangle r && r.Tag as string == "SelectionRect")
                {
                    DrawingCanvas.Children.RemoveAt(i);
                }
            }
            _selectionRect = null;
        }

        private void DrawRegionRectangle(double imageWidth, double imageHeight)
        {
            var rect = new Rectangle
            {
                Width = (_regionRight - _regionLeft) * imageWidth,
                Height = (_regionBottom - _regionTop) * imageHeight,
                Stroke = Brushes.Red,
                StrokeThickness = 3,
                StrokeDashArray = new DoubleCollection { 6, 3 },
                Fill = new SolidColorBrush(Color.FromArgb(40, 255, 0, 0)),
                Tag = "SelectionRect"
            };

            Canvas.SetLeft(rect, _regionLeft * imageWidth);
            Canvas.SetTop(rect, _regionTop * imageHeight);
            DrawingCanvas.Children.Add(rect);
            _selectionRect = rect;
        }

        private int CountLabelsInRegion()
        {
            int count = 0;
            foreach (var img in _allBatchImages.Where(i => i.IsSelected))
            {
                if (!File.Exists(img.LabelPath)) continue;
                var lines = File.ReadAllLines(img.LabelPath);
                foreach (var line in lines)
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    var label = ParseYoloLabel(line);
                    if (label != null && IsLabelInRegion(label))
                        count++;
                }
            }
            return count;
        }

        private bool IsLabelInRegion(YoloLabel label)
        {
            // Check if the center of the label falls within the region
            return label.CenterX >= _regionLeft && label.CenterX <= _regionRight &&
                   label.CenterY >= _regionTop && label.CenterY <= _regionBottom;
        }

        #endregion

        #region Step 3: Class Selection

        private void TxtClassFilter_TextChanged(object sender, TextChangedEventArgs e)
        {
            string filter = TxtClassFilter.Text?.ToLowerInvariant() ?? "";
            if (string.IsNullOrEmpty(filter))
            {
                LbClasses.ItemsSource = _allClasses;
            }
            else
            {
                LbClasses.ItemsSource = new ObservableCollection<ClassItem>(
                    _allClasses.Where(c => c.Name.ToLowerInvariant().Contains(filter) ||
                                           c.ClassId.ToString().Contains(filter)));
            }
        }

        private void LbClasses_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            _selectedClass = LbClasses.SelectedItem as ClassItem;
            UpdatePreview();
        }

        private void UpdatePreview()
        {
            if (_selectedClass == null || !_hasRegion)
            {
                PreviewBorder.Visibility = Visibility.Collapsed;
                return;
            }

            int selectedImageCount = _allBatchImages.Count(i => i.IsSelected);
            int labelCount = CountLabelsInRegion();

            TxtPreviewSummary.Text =
                $"Will change {labelCount} label(s) across {selectedImageCount} image(s) " +
                $"to class \"{_selectedClass.Name}\" (ID: {_selectedClass.ClassId}).\n\n" +
                $"Region: ({_regionLeft:P1}, {_regionTop:P1}) to ({_regionRight:P1}, {_regionBottom:P1})";

            PreviewBorder.Visibility = Visibility.Visible;
        }

        #endregion

        #region Step 4: Apply

        private void PrepareConfirmation()
        {
            int selectedImageCount = _allBatchImages.Count(i => i.IsSelected);
            int labelCount = CountLabelsInRegion();

            TxtConfirmationDetails.Text =
                $"You are about to change {labelCount} label(s) across {selectedImageCount} image(s).\n\n" +
                $"All labels whose center falls within the defined region will be reassigned " +
                $"to class \"{_selectedClass.Name}\" (ID: {_selectedClass.ClassId}).\n\n" +
                $"Region (normalized): X=[{_regionLeft:F4}, {_regionRight:F4}], Y=[{_regionTop:F4}, {_regionBottom:F4}]";
        }

        private async void BtnApply_Click(object sender, RoutedEventArgs e)
        {
            // Final confirmation
            var result = MessageBox.Show(
                $"Are you absolutely sure you want to change all labels in the defined region " +
                $"to \"{_selectedClass.Name}\" across {_allBatchImages.Count(i => i.IsSelected)} images?\n\n" +
                $"This operation writes directly to your label files and cannot be undone.",
                "Final Confirmation",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes) return;

            // Disable UI during operation
            BtnApply.IsEnabled = false;
            BtnBack.IsEnabled = false;
            BtnNext.IsEnabled = false;
            ConfirmationBorder.Visibility = Visibility.Collapsed;
            ProgressPanel.Visibility = Visibility.Visible;
            LogLabel.Visibility = Visibility.Visible;
            TxtLog.Visibility = Visibility.Visible;

            await ExecuteBatchOperation();
        }

        private async Task ExecuteBatchOperation()
        {
            var selectedImages = _allBatchImages.Where(i => i.IsSelected).ToList();
            int totalImages = selectedImages.Count;
            int processedImages = 0;
            _totalLabelsChanged = 0;
            _totalFilesModified = 0;

            foreach (var img in selectedImages)
            {
                processedImages++;
                int percentComplete = (int)((double)processedImages / totalImages * 100);

                await Dispatcher.InvokeAsync(() =>
                {
                    ProgressBar.Value = percentComplete;
                    TxtProgressPercent.Text = $"{percentComplete}%";
                    TxtProgressStatus.Text = $"Processing {processedImages}/{totalImages}: {img.FileName}";
                });

                try
                {
                    int changed = await Task.Run(() => ProcessImageLabels(img));
                    if (changed > 0)
                    {
                        _totalLabelsChanged += changed;
                        _totalFilesModified++;
                        await Dispatcher.InvokeAsync(() =>
                            AppendLog($"  {img.FileName}: changed {changed} label(s)"));
                    }
                    else
                    {
                        await Dispatcher.InvokeAsync(() =>
                            AppendLog($"  {img.FileName}: no labels in region"));
                    }
                }
                catch (Exception ex)
                {
                    await Dispatcher.InvokeAsync(() =>
                        AppendLog($"  ERROR {img.FileName}: {ex.Message}"));
                }
            }

            // Show results
            await Dispatcher.InvokeAsync(() =>
            {
                _operationCompleted = true;
                ProgressPanel.Visibility = Visibility.Collapsed;

                TxtResults.Text =
                    $"Changed {_totalLabelsChanged} label(s) across {_totalFilesModified} file(s) " +
                    $"to class \"{_selectedClass.Name}\" (ID: {_selectedClass.ClassId}).\n" +
                    $"Processed {totalImages} image(s) total.";

                ResultsBorder.Visibility = Visibility.Visible;
                TxtStatus.Text = "Batch operation complete. You can close this window.";

                BtnApply.Visibility = Visibility.Collapsed;
                BtnBack.IsEnabled = false;

                if (_totalLabelsChanged > 0)
                {
                    Notify.sendSuccess($"Batch edit complete: {_totalLabelsChanged} labels changed in {_totalFilesModified} files");
                }
                else
                {
                    Notify.sendInfo("Batch edit complete: no labels were changed");
                }
            });
        }

        private int ProcessImageLabels(BatchImageItem img)
        {
            if (!File.Exists(img.LabelPath)) return 0;

            var lines = File.ReadAllLines(img.LabelPath);
            var newLines = new List<string>();
            int changedCount = 0;

            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    newLines.Add(line);
                    continue;
                }

                var label = ParseYoloLabel(line);
                if (label == null)
                {
                    newLines.Add(line);
                    continue;
                }

                if (IsLabelInRegion(label))
                {
                    // Replace class ID
                    label.ClassId = _selectedClass.ClassId;
                    changedCount++;

                    // Write updated line
                    newLines.Add(FormatYoloLabel(label));
                }
                else
                {
                    newLines.Add(line);
                }
            }

            if (changedCount > 0)
            {
                File.WriteAllLines(img.LabelPath, newLines);
            }

            return changedCount;
        }

        private string FormatYoloLabel(YoloLabel label)
        {
            return string.Format(CultureInfo.InvariantCulture,
                "{0} {1:0.######} {2:0.######} {3:0.######} {4:0.######}",
                label.ClassId, label.CenterX, label.CenterY, label.Width, label.Height);
        }

        #endregion

        #region Helpers

        private YoloLabel ParseYoloLabel(string line)
        {
            try
            {
                string[] parts = line.Trim().Split(' ');
                if (parts.Length < 5) return null;

                for (int i = 1; i < parts.Length; i++)
                {
                    if (parts[i].Contains(',') && !parts[i].Contains('.'))
                        parts[i] = parts[i].Replace(',', '.');
                }

                var formatProvider = CultureInfo.InvariantCulture;

                if (int.TryParse(parts[0], out int classId) &&
                    float.TryParse(parts[1], formatProvider, out float centerX) &&
                    float.TryParse(parts[2], formatProvider, out float centerY) &&
                    float.TryParse(parts[3], formatProvider, out float width) &&
                    float.TryParse(parts[4], formatProvider, out float height))
                {
                    return new YoloLabel
                    {
                        ClassId = classId,
                        CenterX = centerX,
                        CenterY = centerY,
                        Width = width,
                        Height = height
                    };
                }

                return null;
            }
            catch
            {
                return null;
            }
        }

        private void AppendLog(string message)
        {
            TxtLog.AppendText(message + "\n");
            TxtLog.ScrollToEnd();
        }

        #endregion
    }
}
