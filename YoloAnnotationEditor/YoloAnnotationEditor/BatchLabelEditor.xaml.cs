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
using Point = System.Windows.Point;
using Rectangle = System.Windows.Shapes.Rectangle;

namespace YoloAnnotationEditor
{
    public enum BatchOperation { Replace, Add, Delete }
    public enum OverlapMode { AlwaysAdd, SkipIfNearby, DeleteNearbyThenAdd }

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
        private BatchOperation _operation = BatchOperation.Replace;

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

        // Reference image dimensions (used for pixel-distance calculations)
        private int _refImageWidth = 1920;
        private int _refImageHeight = 1080;

        // Selected class (Replace / Add)
        private ClassItem _selectedClass;

        // Results tracking
        private bool _operationCompleted;
        private int _totalLabelsAffected;
        private int _totalFilesModified;

        public BatchLabelEditor(
            IEnumerable<ImageItem> images,
            Dictionary<int, string> classNames,
            Dictionary<int, SolidColorBrush> classColors)
        {
            InitializeComponent();

            _classNames = classNames;
            _classColors = classColors;

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

            foreach (var entry in classNames.OrderBy(c => c.Key))
            {
                _allClasses.Add(new ClassItem
                {
                    ClassId = entry.Key,
                    Name = entry.Value,
                    Color = classColors.ContainsKey(entry.Key) ? classColors[entry.Key] : Brushes.Red
                });
            }

            LbClasses.ItemsSource = _allClasses;

            UpdateSelectionCount();
            UpdateStepIndicators();
            UpdateOperationHint();
        }

        #region Operation Selection

        private void Operation_Changed(object sender, RoutedEventArgs e)
        {
            if (RbReplace?.IsChecked == true) _operation = BatchOperation.Replace;
            else if (RbAdd?.IsChecked == true) _operation = BatchOperation.Add;
            else if (RbDelete?.IsChecked == true) _operation = BatchOperation.Delete;

            UpdateOperationHint();
        }

        private void OverlapMode_Changed(object sender, RoutedEventArgs e)
        {
            UpdatePreview();
        }

        private OverlapMode GetCurrentOverlapMode()
        {
            if (RbAlwaysAdd?.IsChecked == true) return OverlapMode.AlwaysAdd;
            if (RbDeleteNearbyThenAdd?.IsChecked == true) return OverlapMode.DeleteNearbyThenAdd;
            return OverlapMode.SkipIfNearby; // default
        }

        private double GetProximityPx()
        {
            var box = GetCurrentOverlapMode() == OverlapMode.DeleteNearbyThenAdd ? TxtDeleteDistance : TxtSkipDistance;
            if (double.TryParse(box?.Text, out double px) && px > 0) return px;
            return 50; // fallback
        }

        private string DescribeOverlapMode()
        {
            return GetCurrentOverlapMode() switch
            {
                OverlapMode.AlwaysAdd => "always add (don't delete anything)",
                OverlapMode.SkipIfNearby => $"skip if an existing label is within {GetProximityPx():F0} px",
                OverlapMode.DeleteNearbyThenAdd => $"delete labels within {GetProximityPx():F0} px, then add",
                _ => ""
            };
        }

        private void UpdateOperationHint()
        {
            if (TxtOperationHint == null) return;
            TxtOperationHint.Text = _operation switch
            {
                BatchOperation.Replace => "Finds all labels whose center falls in the drawn region and changes their class.",
                BatchOperation.Add =>
                    "For each selected image that has NO label in the region, adds a new label using the drawn region as the bounding box.",
                BatchOperation.Delete => "Removes all labels whose center falls in the drawn region.",
                _ => ""
            };
        }

        #endregion

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
                DialogResult = true;
            Close();
        }

        private bool ValidateCurrentStep()
        {
            switch (_currentStep)
            {
                case 1:
                    if (_allBatchImages.Count(i => i.IsSelected) == 0)
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
                    // Delete needs no class
                    if (_operation != BatchOperation.Delete && _selectedClass == null)
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

            Step1Panel.Visibility = _currentStep == 1 ? Visibility.Visible : Visibility.Collapsed;
            Step2Panel.Visibility = _currentStep == 2 ? Visibility.Visible : Visibility.Collapsed;
            Step3Panel.Visibility = _currentStep == 3 ? Visibility.Visible : Visibility.Collapsed;
            Step4Panel.Visibility = _currentStep == 4 ? Visibility.Visible : Visibility.Collapsed;

            BtnBack.IsEnabled = _currentStep > 1 && !_operationCompleted;
            BtnNext.Visibility = _currentStep < 4 ? Visibility.Visible : Visibility.Collapsed;
            BtnApply.Visibility = _currentStep == 4 && !_operationCompleted ? Visibility.Visible : Visibility.Collapsed;

            switch (_currentStep)
            {
                case 1:
                    TxtStatus.Text = "Step 1: Choose operation and select images";
                    break;
                case 2:
                    TxtStatus.Text = "Step 2: Draw a region on the reference image";
                    PopulateReferenceImageList();
                    UpdateStep2Header();
                    break;
                case 3:
                    TxtStatus.Text = "Step 3: Configure the operation";
                    ConfigureStep3();
                    UpdatePreview();
                    break;
                case 4:
                    TxtStatus.Text = "Step 4: Review and apply changes";
                    PrepareConfirmation();
                    break;
            }

            UpdateStepIndicators();
        }

        private void UpdateStep2Header()
        {
            TxtStep2Header.Text = _operation switch
            {
                BatchOperation.Replace =>
                    "Draw a region on the reference image. Labels whose center falls inside will have their class replaced.",
                BatchOperation.Add =>
                    "Draw a region on the reference image. This region is also used as the bounding box of the new label to add.",
                BatchOperation.Delete =>
                    "Draw a region on the reference image. All labels whose center falls inside will be deleted.",
                _ => TxtStep2Header.Text
            };
        }

        private void ConfigureStep3()
        {
            bool needsClass = _operation != BatchOperation.Delete;
            ClassPickerPanel.Visibility = needsClass ? Visibility.Visible : Visibility.Collapsed;
            OverlapOptionsPanel.Visibility = _operation == BatchOperation.Add ? Visibility.Visible : Visibility.Collapsed;
            DeleteInfoPanel.Visibility = _operation == BatchOperation.Delete ? Visibility.Visible : Visibility.Collapsed;

            TxtStep3Header.Text = _operation switch
            {
                BatchOperation.Replace => "Select the new class to assign to all labels in the region:",
                BatchOperation.Add => "Select the class for the new label, and choose how to handle images that already have a label in the region:",
                BatchOperation.Delete => "Delete all labels in the region:",
                _ => "Configure the operation:"
            };

            if (_operation == BatchOperation.Delete)
            {
                int count = CountLabelsInRegion();
                int images = _allBatchImages.Count(i => i.IsSelected);
                TxtDeletePreview.Text =
                    $"Will delete {count} label(s) across {images} selected image(s).\n\n" +
                    $"Region (normalized): X=[{_regionLeft:F4}, {_regionRight:F4}], Y=[{_regionTop:F4}, {_regionBottom:F4}]";
                PreviewBorder.Visibility = Visibility.Collapsed;
            }
        }

        private void UpdateStepIndicators()
        {
            var active = new SolidColorBrush(Color.FromRgb(0x21, 0x96, 0xF3));
            var done = new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50));
            var idle = new SolidColorBrush(Color.FromRgb(0xD0, 0xD0, 0xD0));

            Step1Indicator.Background = _currentStep == 1 ? active : done;
            Step2Indicator.Background = _currentStep == 2 ? active : (_currentStep > 2 ? done : idle);
            Step3Indicator.Background = _currentStep == 3 ? active : (_currentStep > 3 ? done : idle);
            Step4Indicator.Background = _currentStep == 4 ? active : idle;

            var white = Brushes.White;
            var gray = new SolidColorBrush(Color.FromRgb(0x90, 0x90, 0x90));
            Step2Text.Foreground = _currentStep >= 2 ? white : gray;
            Step3Text.Foreground = _currentStep >= 3 ? white : gray;
            Step4Text.Foreground = _currentStep >= 4 ? white : gray;
        }

        #endregion

        #region Step 1: Image Selection

        private int _lastClickedImageIndex = -1;

        private void LvImages_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            var listViewItem = GetListViewItemFromElement(e.OriginalSource as DependencyObject);
            if (listViewItem == null) return;

            var clickedItem = listViewItem.DataContext as BatchImageItem;
            if (clickedItem == null) return;

            var currentItems = GetFilteredImages().ToList();
            int clickedIndex = currentItems.IndexOf(clickedItem);
            if (clickedIndex < 0) return;

            if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0 && _lastClickedImageIndex >= 0)
            {
                bool targetState = !clickedItem.IsSelected;
                int start = Math.Min(_lastClickedImageIndex, clickedIndex);
                int end = Math.Max(_lastClickedImageIndex, clickedIndex);
                for (int i = start; i <= end; i++)
                    currentItems[i].IsSelected = targetState;
                e.Handled = true;
            }

            _lastClickedImageIndex = clickedIndex;
        }

        private static System.Windows.Controls.ListViewItem GetListViewItemFromElement(DependencyObject element)
        {
            while (element != null && element is not System.Windows.Controls.ListViewItem)
                element = VisualTreeHelper.GetParent(element);
            return element as System.Windows.Controls.ListViewItem;
        }

        private void BtnSelectAll_Click(object sender, RoutedEventArgs e)
        {
            foreach (var item in GetFilteredImages()) item.IsSelected = true;
            UpdateSelectionCount();
        }

        private void BtnSelectNone_Click(object sender, RoutedEventArgs e)
        {
            foreach (var item in GetFilteredImages()) item.IsSelected = false;
            UpdateSelectionCount();
        }

        private void BtnInvertSelection_Click(object sender, RoutedEventArgs e)
        {
            foreach (var item in GetFilteredImages()) item.IsSelected = !item.IsSelected;
            UpdateSelectionCount();
        }

        private void TxtImageFilter_TextChanged(object sender, TextChangedEventArgs e)
        {
            _lastClickedImageIndex = -1;
            string filter = TxtImageFilter.Text?.ToLowerInvariant() ?? "";
            LvImages.ItemsSource = string.IsNullOrEmpty(filter)
                ? _allBatchImages
                : new ObservableCollection<BatchImageItem>(
                    _allBatchImages.Where(i => i.FileName.ToLowerInvariant().Contains(filter)));
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
                UpdateSelectionCount();
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
            var selected = _allBatchImages.Where(i => i.IsSelected).ToList();
            LvReferenceImages.ItemsSource = selected;
            if (selected.Count > 0)
                LvReferenceImages.SelectedIndex = 0;
        }

        private void LvReferenceImages_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (LvReferenceImages.SelectedItem is BatchImageItem item)
                LoadReferenceImage(item);
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

                _refImageWidth = image.PixelWidth;
                _refImageHeight = image.PixelHeight;

                ReferenceImage.Source = image;
                DrawingCanvas.Width = image.PixelWidth;
                DrawingCanvas.Height = image.PixelHeight;

                DrawingCanvas.Children.Clear();
                DrawExistingAnnotations(item.LabelPath, image.PixelWidth, image.PixelHeight);

                if (_hasRegion)
                    DrawRegionRectangle(image.PixelWidth, image.PixelHeight);
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"Error loading reference image: {ex.Message}");
            }
        }

        private void DrawExistingAnnotations(string labelPath, double imageWidth, double imageHeight)
        {
            if (!File.Exists(labelPath)) return;

            foreach (var line in File.ReadAllLines(labelPath))
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

                Brush brush = _classColors.ContainsKey(label.ClassId) ? _classColors[label.ClassId] : Brushes.Red;

                var annotRect = new Rectangle
                {
                    Width = w, Height = h,
                    Stroke = brush, StrokeThickness = 2,
                    Fill = new SolidColorBrush(Color.FromArgb(30,
                        ((SolidColorBrush)brush).Color.R,
                        ((SolidColorBrush)brush).Color.G,
                        ((SolidColorBrush)brush).Color.B))
                };
                Canvas.SetLeft(annotRect, left);
                Canvas.SetTop(annotRect, top);
                DrawingCanvas.Children.Add(annotRect);

                if (_classNames.ContainsKey(label.ClassId))
                {
                    var tb = new TextBlock
                    {
                        Text = _classNames[label.ClassId],
                        Foreground = Brushes.White, Background = brush,
                        Padding = new Thickness(2), FontSize = 11
                    };
                    Canvas.SetLeft(tb, left);
                    Canvas.SetTop(tb, top - 15);
                    DrawingCanvas.Children.Add(tb);
                }
            }
        }

        private void Canvas_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (ReferenceImage.Source == null) return;
            _startPoint = e.GetPosition(DrawingCanvas);
            _isDrawing = true;
            RemoveSelectionRectangle();

            _selectionRect = new Rectangle
            {
                Stroke = Brushes.Red, StrokeThickness = 3,
                StrokeDashArray = new DoubleCollection { 6, 3 },
                Fill = new SolidColorBrush(Color.FromArgb(40, 255, 0, 0)),
                Tag = "SelectionRect", Width = 0, Height = 0
            };
            Canvas.SetLeft(_selectionRect, _startPoint.X);
            Canvas.SetTop(_selectionRect, _startPoint.Y);
            DrawingCanvas.Children.Add(_selectionRect);
            e.Handled = true;
        }

        private void Canvas_MouseMove(object sender, MouseEventArgs e)
        {
            if (!_isDrawing || _selectionRect == null) return;
            var cp = e.GetPosition(DrawingCanvas);
            double x = Math.Min(cp.X, _startPoint.X);
            double y = Math.Min(cp.Y, _startPoint.Y);
            double w = Math.Abs(cp.X - _startPoint.X);
            double h = Math.Abs(cp.Y - _startPoint.Y);
            Canvas.SetLeft(_selectionRect, x);
            Canvas.SetTop(_selectionRect, y);
            _selectionRect.Width = w;
            _selectionRect.Height = h;
            TxtRegionInfo.Text = $"Region: {w:F0} × {h:F0} px";
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

            if (ReferenceImage.Source is BitmapImage image)
            {
                double imgW = image.PixelWidth, imgH = image.PixelHeight;
                double left = Canvas.GetLeft(_selectionRect);
                double top = Canvas.GetTop(_selectionRect);

                _regionLeft = Math.Max(0, left / imgW);
                _regionTop = Math.Max(0, top / imgH);
                _regionRight = Math.Min(1, (left + _selectionRect.Width) / imgW);
                _regionBottom = Math.Min(1, (top + _selectionRect.Height) / imgH);
                _hasRegion = true;

                int labelCount = CountLabelsInRegion();
                TxtRegionInfo.Text = _operation switch
                {
                    BatchOperation.Add =>
                        $"Region defined ({_selectionRect.Width:F0}×{_selectionRect.Height:F0} px) — " +
                        $"{labelCount} selected images already have a label here",
                    BatchOperation.Delete =>
                        $"Region defined ({_selectionRect.Width:F0}×{_selectionRect.Height:F0} px) — " +
                        $"{labelCount} label(s) will be deleted",
                    _ =>
                        $"Region defined ({_selectionRect.Width:F0}×{_selectionRect.Height:F0} px) — " +
                        $"{labelCount} label(s) found across selected images"
                };
            }
            e.Handled = true;
        }

        private void RemoveSelectionRectangle()
        {
            for (int i = DrawingCanvas.Children.Count - 1; i >= 0; i--)
            {
                if (DrawingCanvas.Children[i] is Rectangle r && r.Tag as string == "SelectionRect")
                    DrawingCanvas.Children.RemoveAt(i);
            }
            _selectionRect = null;
        }

        private void DrawRegionRectangle(double imageWidth, double imageHeight)
        {
            var rect = new Rectangle
            {
                Width = (_regionRight - _regionLeft) * imageWidth,
                Height = (_regionBottom - _regionTop) * imageHeight,
                Stroke = Brushes.Red, StrokeThickness = 3,
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
                foreach (var line in File.ReadAllLines(img.LabelPath))
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    var label = ParseYoloLabel(line);
                    if (label != null && IsLabelInRegion(label)) count++;
                }
            }
            return count;
        }

        private bool IsLabelInRegion(YoloLabel label) =>
            label.CenterX >= _regionLeft && label.CenterX <= _regionRight &&
            label.CenterY >= _regionTop && label.CenterY <= _regionBottom;

        #endregion

        #region Step 3: Configure

        private void TxtClassFilter_TextChanged(object sender, TextChangedEventArgs e)
        {
            string filter = TxtClassFilter.Text?.ToLowerInvariant() ?? "";
            LbClasses.ItemsSource = string.IsNullOrEmpty(filter)
                ? _allClasses
                : new ObservableCollection<ClassItem>(
                    _allClasses.Where(c => c.Name.ToLowerInvariant().Contains(filter) ||
                                           c.ClassId.ToString().Contains(filter)));
        }

        private void LbClasses_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            _selectedClass = LbClasses.SelectedItem as ClassItem;
            UpdatePreview();
        }

        private void UpdatePreview()
        {
            if (_operation == BatchOperation.Delete)
            {
                PreviewBorder.Visibility = Visibility.Collapsed;
                return;
            }
            if (_selectedClass == null || !_hasRegion)
            {
                PreviewBorder.Visibility = Visibility.Collapsed;
                return;
            }

            int selectedImageCount = _allBatchImages.Count(i => i.IsSelected);
            int labelCount = CountLabelsInRegion();

            TxtPreviewSummary.Text = _operation switch
            {
                BatchOperation.Replace =>
                    $"Will change {labelCount} label(s) across {selectedImageCount} image(s) " +
                    $"to class \"{_selectedClass.Name}\" (ID: {_selectedClass.ClassId}).\n" +
                    $"Region: ({_regionLeft:P1}, {_regionTop:P1}) → ({_regionRight:P1}, {_regionBottom:P1})",
                BatchOperation.Add =>
                    $"Will add a \"{_selectedClass.Name}\" (ID: {_selectedClass.ClassId}) label to images in the selected set.\n" +
                    $"Overlap: {DescribeOverlapMode()}.\n" +
                    $"{selectedImageCount} image(s) selected, {labelCount} already have a label in the region.\n" +
                    $"Region: ({_regionLeft:P1}, {_regionTop:P1}) → ({_regionRight:P1}, {_regionBottom:P1})",
                _ => ""
            };

            PreviewBorder.Visibility = Visibility.Visible;
        }

        #endregion

        #region Step 4: Apply

        private void PrepareConfirmation()
        {
            int selectedImageCount = _allBatchImages.Count(i => i.IsSelected);
            int labelCount = CountLabelsInRegion();

            TxtConfirmationTitle.Text = _operation switch
            {
                BatchOperation.Replace => "⚠ Confirm Batch Replace",
                BatchOperation.Add => "⚠ Confirm Batch Add",
                BatchOperation.Delete => "⚠ Confirm Batch Delete",
                _ => "⚠ Confirm Batch Operation"
            };

            TxtConfirmationDetails.Text = _operation switch
            {
                BatchOperation.Replace =>
                    $"Will change {labelCount} label(s) across {selectedImageCount} image(s) " +
                    $"to class \"{_selectedClass.Name}\" (ID: {_selectedClass.ClassId}).\n\n" +
                    $"Region (normalized): X=[{_regionLeft:F4}, {_regionRight:F4}], Y=[{_regionTop:F4}, {_regionBottom:F4}]",
                BatchOperation.Add =>
                    $"Will add a \"{_selectedClass.Name}\" (ID: {_selectedClass.ClassId}) label to images in the selected set.\n" +
                    $"Overlap: {DescribeOverlapMode()}.\n" +
                    $"Reference image dimensions: {_refImageWidth}×{_refImageHeight} px.\n\n" +
                    $"Region (normalized): X=[{_regionLeft:F4}, {_regionRight:F4}], Y=[{_regionTop:F4}, {_regionBottom:F4}]",
                BatchOperation.Delete =>
                    $"Will delete {labelCount} label(s) from {selectedImageCount} selected image(s).\n\n" +
                    $"Region (normalized): X=[{_regionLeft:F4}, {_regionRight:F4}], Y=[{_regionTop:F4}, {_regionBottom:F4}]",
                _ => ""
            };
        }

        private async void BtnApply_Click(object sender, RoutedEventArgs e)
        {
            string operationVerb = _operation switch
            {
                BatchOperation.Replace => $"replace labels in the region with class \"{_selectedClass.Name}\"",
                BatchOperation.Add => $"add \"{_selectedClass.Name}\" labels to images missing them in the region",
                BatchOperation.Delete => "delete all labels in the region",
                _ => "apply the batch operation"
            };

            var result = MessageBox.Show(
                $"Are you absolutely sure you want to {operationVerb} across " +
                $"{_allBatchImages.Count(i => i.IsSelected)} image(s)?\n\n" +
                "This writes directly to label files and cannot be undone.",
                "Final Confirmation", MessageBoxButton.YesNo, MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes) return;

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
            int processed = 0;
            _totalLabelsAffected = 0;
            _totalFilesModified = 0;

            var overlapMode = GetCurrentOverlapMode();
            double proximityPx = GetProximityPx();

            foreach (var img in selectedImages)
            {
                processed++;
                int pct = (int)((double)processed / totalImages * 100);

                await Dispatcher.InvokeAsync(() =>
                {
                    ProgressBar.Value = pct;
                    TxtProgressPercent.Text = $"{pct}%";
                    TxtProgressStatus.Text = $"Processing {processed}/{totalImages}: {img.FileName}";
                });

                try
                {
                    int changed = _operation switch
                    {
                        BatchOperation.Replace => await Task.Run(() => ProcessReplace(img)),
                        BatchOperation.Add => await Task.Run(() => ProcessAdd(img, overlapMode, proximityPx)),
                        BatchOperation.Delete => await Task.Run(() => ProcessDelete(img)),
                        _ => 0
                    };

                    string outcome = _operation switch
                    {
                        BatchOperation.Replace => changed > 0 ? $"changed {changed} label(s)" : "no labels in region",
                        BatchOperation.Add => changed switch
                        {
                            1 => "added 1 label",
                            -1 => "skipped (nearby label found)",
                            -2 => $"deleted nearby label(s) and added new one",
                            _ => "no change"
                        },
                        BatchOperation.Delete => changed > 0 ? $"deleted {changed} label(s)" : "no labels in region",
                        _ => ""
                    };

                    if (changed != 0 && changed != -1) { _totalLabelsAffected++; _totalFilesModified++; }

                    await Dispatcher.InvokeAsync(() => AppendLog($"  {img.FileName}: {outcome}"));
                }
                catch (Exception ex)
                {
                    await Dispatcher.InvokeAsync(() => AppendLog($"  ERROR {img.FileName}: {ex.Message}"));
                }
            }

            await Dispatcher.InvokeAsync(() =>
            {
                _operationCompleted = true;
                ProgressPanel.Visibility = Visibility.Collapsed;

                TxtResults.Text = _operation switch
                {
                    BatchOperation.Replace =>
                        $"Changed {_totalLabelsAffected} label(s) in {_totalFilesModified} file(s) " +
                        $"to class \"{_selectedClass?.Name}\".",
                    BatchOperation.Add =>
                        $"Added labels to {_totalFilesModified} file(s) (class \"{_selectedClass?.Name}\").",
                    BatchOperation.Delete =>
                        $"Deleted {_totalLabelsAffected} label(s) from {_totalFilesModified} file(s).",
                    _ => "Done."
                };
                TxtResults.Text += $"\nProcessed {totalImages} image(s) total.";

                ResultsBorder.Visibility = Visibility.Visible;
                TxtStatus.Text = "Batch operation complete. You can close this window.";
                BtnApply.Visibility = Visibility.Collapsed;
                BtnBack.IsEnabled = false;

                if (_totalLabelsAffected > 0)
                    Notify.sendSuccess($"Batch {_operation} complete: {_totalLabelsAffected} label(s) affected in {_totalFilesModified} file(s)");
                else
                    Notify.sendInfo($"Batch {_operation} complete: no labels were changed");
            });
        }

        // Returns number of labels changed
        private int ProcessReplace(BatchImageItem img)
        {
            if (!File.Exists(img.LabelPath)) return 0;

            var lines = File.ReadAllLines(img.LabelPath);
            var newLines = new List<string>();
            int changed = 0;

            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line)) { newLines.Add(line); continue; }
                var label = ParseYoloLabel(line);
                if (label == null) { newLines.Add(line); continue; }

                if (IsLabelInRegion(label))
                {
                    label.ClassId = _selectedClass.ClassId;
                    changed++;
                    newLines.Add(FormatYoloLabel(label));
                }
                else
                {
                    newLines.Add(line);
                }
            }

            if (changed > 0) File.WriteAllLines(img.LabelPath, newLines);
            return changed;
        }

        // Returns: 1 = added, -1 = skipped (nearby label), -2 = deleted nearby + added, 0 = no change
        private int ProcessAdd(BatchImageItem img, OverlapMode mode, double proximityPx)
        {
            var lines = File.Exists(img.LabelPath)
                ? File.ReadAllLines(img.LabelPath).ToList()
                : new List<string>();

            float newCx = (float)((_regionLeft + _regionRight) / 2.0);
            float newCy = (float)((_regionTop + _regionBottom) / 2.0);

            var newLabel = new YoloLabel
            {
                ClassId = _selectedClass.ClassId,
                CenterX = newCx,
                CenterY = newCy,
                Width = (float)(_regionRight - _regionLeft),
                Height = (float)(_regionBottom - _regionTop)
            };

            if (mode == OverlapMode.AlwaysAdd)
            {
                lines.Add(FormatYoloLabel(newLabel));
                File.WriteAllLines(img.LabelPath, lines);
                return 1;
            }

            // Build list of lines with their parsed labels (paired to avoid re-parsing)
            var parsed = lines.Select(l => (line: l, label: string.IsNullOrWhiteSpace(l) ? null : ParseYoloLabel(l))).ToList();

            bool foundNearby = parsed.Any(p => p.label != null &&
                GetPixelDistance(p.label.CenterX, p.label.CenterY, newCx, newCy) <= proximityPx);

            if (mode == OverlapMode.SkipIfNearby)
            {
                if (foundNearby) return -1;
                lines.Add(FormatYoloLabel(newLabel));
                File.WriteAllLines(img.LabelPath, lines);
                return 1;
            }

            // DeleteNearbyThenAdd
            int deletedCount = 0;
            var newLines = new List<string>();
            foreach (var (line, label) in parsed)
            {
                if (label != null && GetPixelDistance(label.CenterX, label.CenterY, newCx, newCy) <= proximityPx)
                {
                    deletedCount++;
                    continue; // remove it
                }
                newLines.Add(line);
            }
            newLines.Add(FormatYoloLabel(newLabel));
            File.WriteAllLines(img.LabelPath, newLines);
            return deletedCount > 0 ? -2 : 1;
        }

        private double GetPixelDistance(double cx1, double cy1, double cx2, double cy2)
        {
            double dx = (cx1 - cx2) * _refImageWidth;
            double dy = (cy1 - cy2) * _refImageHeight;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        // Returns number of labels deleted
        private int ProcessDelete(BatchImageItem img)
        {
            if (!File.Exists(img.LabelPath)) return 0;

            var lines = File.ReadAllLines(img.LabelPath);
            var newLines = new List<string>();
            int deleted = 0;

            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line)) { newLines.Add(line); continue; }
                var label = ParseYoloLabel(line);
                if (label == null) { newLines.Add(line); continue; }

                if (IsLabelInRegion(label))
                    deleted++;
                else
                    newLines.Add(line);
            }

            if (deleted > 0) File.WriteAllLines(img.LabelPath, newLines);
            return deleted;
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
                    if (parts[i].Contains(',') && !parts[i].Contains('.'))
                        parts[i] = parts[i].Replace(',', '.');

                var fmt = CultureInfo.InvariantCulture;
                if (int.TryParse(parts[0], out int classId) &&
                    float.TryParse(parts[1], fmt, out float cx) &&
                    float.TryParse(parts[2], fmt, out float cy) &&
                    float.TryParse(parts[3], fmt, out float w) &&
                    float.TryParse(parts[4], fmt, out float h))
                {
                    return new YoloLabel { ClassId = classId, CenterX = cx, CenterY = cy, Width = w, Height = h };
                }

                return null;
            }
            catch { return null; }
        }

        private string FormatYoloLabel(YoloLabel label) =>
            string.Format(CultureInfo.InvariantCulture,
                "{0} {1:0.######} {2:0.######} {3:0.######} {4:0.######}",
                label.ClassId, label.CenterX, label.CenterY, label.Width, label.Height);

        private void AppendLog(string message)
        {
            TxtLog.AppendText(message + "\n");
            TxtLog.ScrollToEnd();
        }

        #endregion
    }
}
