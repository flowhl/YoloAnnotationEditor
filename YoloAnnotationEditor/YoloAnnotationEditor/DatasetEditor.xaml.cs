using LiveChartsCore.SkiaSharpView.Painting;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore;
using Microsoft.Win32;
using SkiaSharp;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;
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
using YoloDotNet;
using YoloDotNet.Models;
using YoloDotNet.Enums;
using SkiaSharp.Views.WPF;

namespace YoloAnnotationEditor
{
    /// <summary>
    /// Interaction logic for DatasetEditor.xaml
    /// </summary>
    public partial class DatasetEditor : UserControl
    {
        // Labeling state
        private bool _isDrawing = false;
        private Point _startPoint;
        private Point _currentPoint;
        private Rectangle _selectedAnnotation = null;
        private bool _isDirty = false; // Track if there are unsaved changes
        private CollectionViewSource _filteredClasses = new CollectionViewSource();
        private ImageItem _currentImage = null;
        private List<YoloLabel> _originalAnnotations = new List<YoloLabel>();
        private TextBlock _imageIndexCounter;
        private EditStateManager _editStateManager;

        //YOLO
        private Yolo _yolo = null;

        // Add this property to the DatasetEditor class:
        public bool IsEditMode => BtnEditMode.IsChecked == true;

        private string _yamlFilePath;
        private string _datasetBasePath;
        private Dictionary<int, string> _classNames = new Dictionary<int, string>();
        private Dictionary<int, SolidColorBrush> _classColors = new Dictionary<int, SolidColorBrush>();
        private ObservableCollection<ImageItem> _allImages = new ObservableCollection<ImageItem>();
        private ObservableCollection<ClassItem> _classItems = new ObservableCollection<ClassItem>();
        private CollectionViewSource _filteredImages = new CollectionViewSource();
        private Random _random = new Random(42); // Seed for consistent colors

        public DatasetEditor()
        {
            InitializeComponent();
            RegisterHotkeys();

            // Set up filtered view
            _filteredImages.Source = _allImages;
            _filteredImages.Filter += FilterImages;
            LvThumbnails.ItemsSource = _filteredImages.View;

            // Set class list
            ClassesList.ItemsSource = _classItems;

            // Add this to your DatasetEditor constructor after InitializeComponent()
            StatisticsTab.GotFocus += StatisticsTab_GotFocus;

            // Set reference to the counter
            _imageIndexCounter = ImageIndexCounter;
        }

        #region Dynamic Filters

        private void FilterImages(object sender, FilterEventArgs e)
        {
            if (e.Item is ImageItem item && !string.IsNullOrEmpty(TxtSearch.Text))
            {
                string searchText = TxtSearch.Text.ToLowerInvariant();

                // Filter by filename or class names
                bool matchesFileName = item.FileName.ToLowerInvariant().Contains(searchText);
                bool matchesClass = item.ClassIds.Any(id =>
                    _classNames.ContainsKey(id) &&
                    _classNames[id].ToLowerInvariant().Contains(searchText));

                e.Accepted = matchesFileName || matchesClass;
            }
            else
            {
                e.Accepted = true;
            }
        }
        #endregion

        #region Mouse Events

        //Lock zoom control
        public void LockZoom()
        {
            zoomControl.Enabled = false;
        }

        // Unlock zoom control
        public void UnlockZoom()
        {
            zoomControl.Enabled = true;
        }
        private void MainImage_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (!IsEditMode || MainImage.Source == null) return;

            // Get the position relative to the AnnotationCanvas
            _startPoint = e.GetPosition(AnnotationCanvas);

            // First check if we clicked on an existing annotation
            bool hitAnnotation = false;

            // Check annotations in reverse order (to select top-most first)
            for (int i = AnnotationCanvas.Children.Count - 1; i >= 0; i--)
            {
                var child = AnnotationCanvas.Children[i];
                if (child is Rectangle rect && rect.Tag is YoloLabel)
                {
                    // Get rectangle bounds in canvas coordinates
                    double rectLeft = Canvas.GetLeft(rect);
                    double rectTop = Canvas.GetTop(rect);
                    Rect rectBounds = new Rect(rectLeft, rectTop, rect.Width, rect.Height);

                    if (rectBounds.Contains(_startPoint))
                    {
                        // We found a hit! Select this annotation
                        _selectedAnnotation = rect;
                        hitAnnotation = true;

                        // Highlight it visually
                        HighlightSelectedAnnotation();

                        // Update the class dropdown selection
                        var label = (YoloLabel)rect.Tag;
                        foreach (ClassItem item in CmbClassSelect.Items)
                        {
                            if (item.ClassId == label.ClassId)
                            {
                                TxtClassSearch.Text = null;
                                CmbClassSelect.SelectedItem = item;
                                break;
                            }
                        }

                        StatusText.Text = $"Selected annotation of class {_classNames[label.ClassId]}";
                        break;
                    }
                }
            }

            if (!hitAnnotation)
            {
                // Deselect any previously selected annotation
                _selectedAnnotation = null;
                HighlightSelectedAnnotation();

                // Remove any existing selection rectangle
                for (int i = AnnotationCanvas.Children.Count - 1; i >= 0; i--)
                {
                    if (AnnotationCanvas.Children[i] == SelectionRectangle)
                    {
                        AnnotationCanvas.Children.RemoveAt(i);
                        break;
                    }
                }

                // Create a new rectangle
                SelectionRectangle = new Rectangle
                {
                    Stroke = Brushes.Yellow,
                    StrokeThickness = 2,
                    StrokeDashArray = new DoubleCollection { 4, 2 },
                    Fill = new SolidColorBrush(Color.FromArgb(50, 255, 255, 0))
                };

                // Set initial position
                Canvas.SetLeft(SelectionRectangle, _startPoint.X);
                Canvas.SetTop(SelectionRectangle, _startPoint.Y);
                SelectionRectangle.Width = 0;
                SelectionRectangle.Height = 0;

                // Add to canvas
                AnnotationCanvas.Children.Add(SelectionRectangle);

                // Start drawing
                _isDrawing = true;
                StatusText.Text = "Drawing new annotation...";
            }
        }

        private void MainImage_MouseMove(object sender, MouseEventArgs e)
        {
            if (!IsEditMode || !_isDrawing || SelectionRectangle == null) return;

            // Get the current position
            _currentPoint = e.GetPosition(AnnotationCanvas);

            // Calculate dimensions
            double x = Math.Min(_currentPoint.X, _startPoint.X);
            double y = Math.Min(_currentPoint.Y, _startPoint.Y);
            double width = Math.Max(_currentPoint.X, _startPoint.X) - x;
            double height = Math.Max(_currentPoint.Y, _startPoint.Y) - y;

            // Update rectangle
            Canvas.SetLeft(SelectionRectangle, x);
            Canvas.SetTop(SelectionRectangle, y);
            SelectionRectangle.Width = width;
            SelectionRectangle.Height = height;

            // Update status
            StatusText.Text = $"Drawing: {width:F0}×{height:F0} pixels";
        }

        private void MainImage_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (!IsEditMode || !_isDrawing || SelectionRectangle == null) return;

            _isDrawing = false;

            // Check if the rectangle is too small
            if (SelectionRectangle.Width < 5 || SelectionRectangle.Height < 5)
            {
                // Too small, remove it
                AnnotationCanvas.Children.Remove(SelectionRectangle);
                SelectionRectangle = null;
                StatusText.Text = "Edit Mode: Click and drag to create annotations";
                return;
            }

            // Keep the selection rectangle visible and prompt user to select a class
            if (CmbClassSelect.SelectedItem == null)
            {
                MessageBox.Show("Please select a class for the annotation", "Class Required", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                // We have a class selected, create the annotation
                CreateAnnotationFromSelection();

                // Remove the selection rectangle
                AnnotationCanvas.Children.Remove(SelectionRectangle);
                SelectionRectangle = null;
            }
        }

        #endregion

        #region Register Hotkeys

        private void RegisterHotkeys()
        {
            this.KeyUp += DatasetEditor_KeyUp;
        }

        private void DatasetEditor_KeyUp(object sender, System.Windows.Input.KeyEventArgs e)
        {
            bool isCtrlPressed = Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl);
            if (e.Key == Key.E && isCtrlPressed)
            {
                BtnEditMode.IsChecked = !IsEditMode;
                ToggleEditMode();
            }
            else if (e.Key == Key.Delete)
            {
                DeleteSelectedAnnotation();
            }
            else if (e.Key == Key.S && isCtrlPressed)
            {
                SaveAnnotations();
            }
        }

        #endregion

        #region Button Controls

        private void BtnBrowseYaml_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog openFileDialog = new OpenFileDialog
            {
                Filter = "YAML files (*.yaml, *.yml)|*.yaml;*.yml|All files (*.*)|*.*",
                Title = "Select YOLO Dataset YAML File"
            };

            openFileDialog.ShowDialog();

            if (openFileDialog.FileName != null)
            {
                _yamlFilePath = openFileDialog.FileName;
                TxtYamlPath.Text = _yamlFilePath;

                // Load dataset
                LoadDataset();
            }
        }

        private void BtnEditMode_Click(object sender, RoutedEventArgs e)
        {
            ToggleEditMode();
        }

        private void BtnDeleteSelected_Click(object sender, RoutedEventArgs e)
        {
            DeleteSelectedAnnotation();
        }

        private void BtnSaveAnnotations_Click(object sender, RoutedEventArgs e)
        {
            SaveAnnotations();
        }

        public void ToggleEditMode()
        {
            if (IsEditMode)
            {
                LockZoom();
                // Entering edit mode
                StatusText.Text = "Edit Mode: Click and drag to create annotations";

                // Initialize the class dropdown
                PopulateClassDropdown();
            }
            else
            {
                UnlockZoom();
                // Exiting edit mode
                StatusText.Text = "View Mode";

                // Clear any selection rectangle
                if (SelectionRectangle != null)
                    SelectionRectangle.Visibility = Visibility.Collapsed;
                _selectedAnnotation = null;

                // Prompt to save changes if dirty
                if (_isDirty)
                {
                    var result = MessageBox.Show("You have unsaved annotation changes. Would you like to save them now?",
                        "Unsaved Changes", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);

                    if (result == MessageBoxResult.Yes)
                    {
                        SaveAnnotations();
                    }
                    else if (result == MessageBoxResult.Cancel)
                    {
                        // Return to edit mode
                        BtnEditMode.IsChecked = true;
                        return;
                    }
                }
            }

            // Refresh the annotation display
            if (_currentImage != null)
            {
                DisplayMainImage(_currentImage);
            }
        }

        public void DeleteSelectedAnnotation()
        {
            if (!IsEditMode || _selectedAnnotation == null || _currentImage == null) return;

            // Get the label to delete
            var labelToDelete = (YoloLabel)_selectedAnnotation.Tag;

            // Remove from annotations list
            _currentImage.Annotations.Remove(labelToDelete);

            // Check if we need to update ClassIds
            bool stillExists = _currentImage.Annotations.Any(a => a.ClassId == labelToDelete.ClassId);
            if (!stillExists)
            {
                _currentImage.ClassIds.Remove(labelToDelete.ClassId);
            }

            // Mark as dirty
            _isDirty = true;

            // Redraw annotations
            if (MainImage.Source is BitmapImage image)
            {
                // Clear and redraw all annotations
                AnnotationCanvas.Children.Clear();
                foreach (var annotation in _currentImage.Annotations)
                {
                    DrawAnnotation(annotation, image.PixelWidth, image.PixelHeight);
                }
            }

            _selectedAnnotation = null;
            TxtClassSearch.Text = null;
            CmbClassSelect.SelectedItem = null;
            StatusText.Text = $"Deleted annotation";
        }

        // Add these methods to the DatasetEditor class
        private void BtnToggleEditState_Click(object sender, RoutedEventArgs e)
        {
            if (LvThumbnails.SelectedItem is ImageItem selectedImage && _editStateManager != null)
            {
                _editStateManager.ToggleEditState(selectedImage.FileName);
                selectedImage.IsEdited = _editStateManager.IsEdited(selectedImage.FileName);
                UpdateStatistics();
                StatusText.Text = selectedImage.IsEdited
                    ? $"Marked {selectedImage.FileName} as edited"
                    : $"Marked {selectedImage.FileName} as not edited";
            }
        }

        private void BtnMarkAllEditedUntilHere_Click(object sender, RoutedEventArgs e)
        {
            if (LvThumbnails.SelectedItem is ImageItem selectedImage && _editStateManager != null)
            {
                // Get all file names in the current order
                var allFileNames = _filteredImages.View.Cast<ImageItem>()
                    .TakeWhile(img => img != selectedImage)
                    .Select(img => img.FileName)
                    .ToList();

                // Add the selected image
                allFileNames.Add(selectedImage.FileName);

                // Mark all as edited
                _editStateManager.MarkAllEditedUntil(selectedImage.FileName, allFileNames);

                // Update UI state for all images
                foreach (var image in _allImages)
                {
                    image.IsEdited = _editStateManager.IsEdited(image.FileName);
                }

                UpdateStatistics();
                StatusText.Text = $"Marked all images up to {selectedImage.FileName} as edited";
            }
        }


        #endregion

        #region Manage UI Annotation Preview

        private void HighlightSelectedAnnotation()
        {
            // Reset all annotation styles
            foreach (var child in AnnotationCanvas.Children)
            {
                if (child is Rectangle rect && rect.Tag is YoloLabel)
                {
                    var label = (YoloLabel)rect.Tag;
                    rect.StrokeThickness = 2;
                    rect.Stroke = _classColors.ContainsKey(label.ClassId)
                        ? _classColors[label.ClassId]
                        : Brushes.Red;
                }
            }

            // Highlight the selected annotation much more visibly
            if (_selectedAnnotation != null)
            {
                // Make it very obvious which annotation is selected
                _selectedAnnotation.StrokeThickness = 4;
                _selectedAnnotation.Stroke = Brushes.Yellow;

                // Add a second stroke effect
                var dashedStroke = new Rectangle
                {
                    Width = _selectedAnnotation.Width + 4, // Slightly larger
                    Height = _selectedAnnotation.Height + 4,
                    Stroke = Brushes.Black,
                    StrokeThickness = 1,
                    StrokeDashArray = new DoubleCollection { 4, 2 },
                    Fill = Brushes.Transparent
                };

                // Position it around the selected annotation
                double left = Canvas.GetLeft(_selectedAnnotation);
                double top = Canvas.GetTop(_selectedAnnotation);
                Canvas.SetLeft(dashedStroke, left - 2);
                Canvas.SetTop(dashedStroke, top - 2);

                // Remove any existing selection indicator
                for (int i = AnnotationCanvas.Children.Count - 1; i >= 0; i--)
                {
                    if (AnnotationCanvas.Children[i] is Rectangle r && r.StrokeDashArray?.Count > 0)
                    {
                        AnnotationCanvas.Children.RemoveAt(i);
                    }
                }

                // Add the selection indicator
                AnnotationCanvas.Children.Add(dashedStroke);

                // Ensure it's in front
                Canvas.SetZIndex(dashedStroke, 1000);
            }
            else
            {
                // Remove any existing selection indicators
                for (int i = AnnotationCanvas.Children.Count - 1; i >= 0; i--)
                {
                    if (AnnotationCanvas.Children[i] is Rectangle r && r.StrokeDashArray?.Count > 0)
                    {
                        AnnotationCanvas.Children.RemoveAt(i);
                    }
                }
            }
        }

        private void CreateAnnotationFromSelection()
        {
            if (CmbClassSelect.SelectedItem is not ClassItem selectedClass)
            {
                MessageBox.Show("Please select a class for the annotation", "Class Required", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (MainImage.Source is not BitmapImage image)
            {
                return;
            }

            // Get the rectangle dimensions
            double left = Math.Min(_startPoint.X, _currentPoint.X);
            double top = Math.Min(_startPoint.Y, _currentPoint.Y);
            double width = Math.Abs(_currentPoint.X - _startPoint.X);
            double height = Math.Abs(_currentPoint.Y - _startPoint.Y);

            // Convert pixel coordinates to normalized YOLO coordinates
            float centerX = (float)((left + width / 2) / image.PixelWidth);
            float centerY = (float)((top + height / 2) / image.PixelHeight);
            float normalizedWidth = (float)(width / image.PixelWidth);
            float normalizedHeight = (float)(height / image.PixelHeight);

            // Create new YOLO label
            YoloLabel newLabel = new YoloLabel
            {
                ClassId = selectedClass.ClassId,
                CenterX = centerX,
                CenterY = centerY,
                Width = normalizedWidth,
                Height = normalizedHeight
            };

            // Add to the current image's annotations
            if (_currentImage != null)
            {
                _currentImage.Annotations.Add(newLabel);

                // Mark as dirty
                _isDirty = true;

                // Add class to current image if not already present
                if (!_currentImage.ClassIds.Contains(selectedClass.ClassId))
                {
                    _currentImage.ClassIds.Add(selectedClass.ClassId);
                }

                // Redraw all annotations
                DrawAnnotation(newLabel, image.PixelWidth, image.PixelHeight);
                StatusText.Text = $"Added annotation for class {selectedClass.Name}";
            }
        }
        #endregion

        #region Smaller UI Methods

        private void PopulateClassDropdown()
        {
            var classItems = new ObservableCollection<ClassItem>();

            foreach (var classId in _classNames.Keys.OrderBy(id => id))
            {
                if (_classColors.ContainsKey(classId))
                {
                    classItems.Add(new ClassItem
                    {
                        ClassId = classId,
                        Name = _classNames[classId],
                        Color = _classColors[classId]
                    });
                }
            }

            _filteredClasses.Source = classItems;
            _filteredClasses.Filter += FilterClasses;
            CmbClassSelect.ItemsSource = _filteredClasses.View;

            // Select the first class by default if available
            if (classItems.Count > 0)
            {
                CmbClassSelect.SelectedIndex = 0;
            }
        }

        private void FilterClasses(object sender, FilterEventArgs e)
        {
            if (e.Item is ClassItem item && !string.IsNullOrEmpty(TxtClassSearch.Text))
            {
                string searchText = TxtClassSearch.Text.ToLowerInvariant();

                // Filter by class name or class ID
                bool matchesName = item.Name.ToLowerInvariant().Contains(searchText);
                bool matchesId = item.ClassId.ToString().Contains(searchText);

                e.Accepted = matchesName || matchesId;
            }
            else
            {
                e.Accepted = true;
            }
        }

        private void TxtClassSearch_TextChanged(object sender, TextChangedEventArgs e)
        {
            _filteredClasses.View.Refresh();
        }

        private void TxtSearch_TextChanged(object sender, TextChangedEventArgs e)
        {
            // Refresh filtering
            _filteredImages.View.Refresh();
            UpdateImageIndexCounter();
        }

        private void LvThumbnails_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (LvThumbnails.SelectedItem is ImageItem selectedImage)
            {
                DisplayMainImage(selectedImage);
                UpdateImageIndexCounter();
            }
        }

        private void CmbClassSelect_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Only update if there's a selected annotation and we're in edit mode
            if (_selectedAnnotation != null && IsEditMode && CmbClassSelect.SelectedItem is ClassItem selectedClass)
            {
                // Get the current label
                var label = (YoloLabel)_selectedAnnotation.Tag;

                // Update the class ID
                label.ClassId = selectedClass.ClassId;

                // Mark as dirty
                _isDirty = true;

                // Update the class list for the current image if needed
                if (!_currentImage.ClassIds.Contains(selectedClass.ClassId))
                {
                    _currentImage.ClassIds.Add(selectedClass.ClassId);
                }

                // Redraw the annotation with the new class color
                if (MainImage.Source is BitmapImage image)
                {
                    // Clear and redraw all annotations
                    AnnotationCanvas.Children.Clear();
                    foreach (var annotation in _currentImage.Annotations)
                    {
                        DrawAnnotation(annotation, image.PixelWidth, image.PixelHeight);
                    }

                    // Re-select the annotation to highlight it
                    HighlightSelectedAnnotation();
                }

                StatusText.Text = $"Changed annotation class to {selectedClass.Name}";
            }
        }

        #endregion

        #region Save

        // Add these methods to the DatasetEditor class:

        private void SaveAnnotations()
        {
            if (_currentImage == null) return;

            try
            {
                // Format each annotation as a YOLO label line
                var lines = _currentImage.Annotations.Select(label =>
                    $"{label.ClassId} {label.CenterX.ToString("0.######")} {label.CenterY.ToString("0.######")} " +
                    $"{label.Width.ToString("0.######")} {label.Height.ToString("0.######")}");

                // Write to the label file
                File.WriteAllLines(_currentImage.LabelPath, lines);

                // Update the original annotations with the current (now saved) state
                _originalAnnotations = DeepCopyAnnotations(_currentImage.Annotations);

                // Mark as edited
                if (_editStateManager != null)
                {
                    _editStateManager.MarkAsEdited(_currentImage.FileName);
                    _currentImage.IsEdited = true;
                }

                // Update status
                _isDirty = false;

                // Update statistics
                UpdateStatistics();

                string msg = $"Saved annotations to {Path.GetFileName(_currentImage.LabelPath)}";
                StatusText.Text = msg;
                Trace.WriteLine(msg);
                Notify.sendSuccess("Saved annotations successfully");

            }
            catch (Exception ex)
            {
                Notify.sendError($"Error saving annotations: {ex.Message}");
                StatusText.Text = "Error saving annotations";
                Trace.WriteLine($"Error saving annotations: {ex}");
            }
        }

        private void DisplayMainImage(ImageItem imageItem)
        {
            try
            {
                // Check for unsaved changes before switching images
                if (_isDirty && _currentImage != null && _currentImage != imageItem)
                {
                    var result = MessageBox.Show("You have unsaved annotation changes. Would you like to save them now?",
                        "Unsaved Changes", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);

                    if (result == MessageBoxResult.Yes)
                    {
                        SaveAnnotations();
                    }
                    else if (result == MessageBoxResult.No)
                    {
                        // Revert changes to the current image
                        RevertChanges();
                    }
                    else if (result == MessageBoxResult.Cancel)
                    {
                        // Prevent the selection change by resetting the selection to the current image
                        LvThumbnails.SelectionChanged -= LvThumbnails_SelectionChanged;
                        LvThumbnails.SelectedItem = _currentImage;
                        LvThumbnails.SelectionChanged += LvThumbnails_SelectionChanged;
                        return;
                    }
                }

                // Store current image reference
                _currentImage = imageItem;

                // Reset the dirty flag
                _isDirty = false;

                // Store a deep copy of the original annotations
                _originalAnnotations = DeepCopyAnnotations(imageItem.Annotations);

                // Load full image
                BitmapImage image = new BitmapImage();
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.UriSource = new Uri(imageItem.FilePath);
                image.EndInit();

                // Set image
                MainImage.Source = image;

                // Clear previous annotations
                AnnotationCanvas.Children.Clear();

                // Set canvas size to match image
                AnnotationCanvas.Width = image.PixelWidth;
                AnnotationCanvas.Height = image.PixelHeight;

                // Clear previous class items
                _classItems.Clear();

                // Create a set of class IDs in this image
                var classesInImage = new HashSet<int>();

                // Draw annotations
                foreach (var annotation in imageItem.Annotations)
                {
                    DrawAnnotation(annotation, image.PixelWidth, image.PixelHeight);
                    classesInImage.Add(annotation.ClassId);
                }

                // Update class list to show only classes in this image
                foreach (var classId in classesInImage.OrderBy(id => id))
                {
                    if (_classNames.ContainsKey(classId) && _classColors.ContainsKey(classId))
                    {
                        _classItems.Add(new ClassItem
                        {
                            ClassId = classId,
                            Name = _classNames[classId],
                            Color = _classColors[classId]
                        });
                    }
                }

                // Update status
                StatusText.Text = $"Displaying {imageItem.FileName} ({imageItem.Annotations.Count} annotations)";

                // Clear selection
                _selectedAnnotation = null;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error displaying image: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                StatusText.Text = "Error displaying image";
            }
        }

        private void RevertChanges()
        {
            if (_currentImage == null) return;

            // Replace the current annotations with the deep copy of the original annotations
            _currentImage.Annotations.Clear();
            foreach (var annotation in _originalAnnotations)
            {
                _currentImage.Annotations.Add(annotation);
            }

            // Update class IDs
            _currentImage.ClassIds = _originalAnnotations
                .Select(a => a.ClassId)
                .Distinct()
                .ToList();

            // Reset dirty flag
            _isDirty = false;

            StatusText.Text = "Reverted changes";
            Notify.sendInfo("Reverted changes");
        }

        private List<YoloLabel> DeepCopyAnnotations(List<YoloLabel> annotations)
        {
            List<YoloLabel> copy = new List<YoloLabel>();

            foreach (var annotation in annotations)
            {
                copy.Add(new YoloLabel
                {
                    ClassId = annotation.ClassId,
                    CenterX = annotation.CenterX,
                    CenterY = annotation.CenterY,
                    Width = annotation.Width,
                    Height = annotation.Height
                });
            }

            return copy;
        }

        // Modify the DrawAnnotation method to store the annotation reference in the rectangle's Tag:
        private void DrawAnnotation(YoloLabel label, double imageWidth, double imageHeight)
        {
            try
            {
                // Convert normalized YOLO coordinates to pixel coordinates
                double x = label.CenterX * imageWidth;
                double y = label.CenterY * imageHeight;
                double width = label.Width * imageWidth;
                double height = label.Height * imageHeight;

                // Calculate top-left corner
                double left = x - (width / 2);
                double top = y - (height / 2);

                // Get color for class
                Brush brush = _classColors.ContainsKey(label.ClassId)
                    ? _classColors[label.ClassId]
                    : Brushes.Red;

                // Create rectangle
                Rectangle rect = new Rectangle
                {
                    Width = width,
                    Height = height,
                    Stroke = brush,
                    StrokeThickness = 2,
                    Fill = new SolidColorBrush(Color.FromArgb(50,
                        ((SolidColorBrush)brush).Color.R,
                        ((SolidColorBrush)brush).Color.G,
                        ((SolidColorBrush)brush).Color.B)),
                    Tag = label // Store reference to the label
                };

                // Position rectangle
                Canvas.SetLeft(rect, left);
                Canvas.SetTop(rect, top);

                // Add to canvas
                AnnotationCanvas.Children.Add(rect);

                // Add class label if it exists
                if (_classNames.ContainsKey(label.ClassId))
                {
                    TextBlock textBlock = new TextBlock
                    {
                        Text = _classNames[label.ClassId],
                        Foreground = Brushes.White,
                        Background = brush,
                        Padding = new Thickness(2),
                        FontSize = 12
                    };

                    // Position text at top of bounding box
                    Canvas.SetLeft(textBlock, left);
                    Canvas.SetTop(textBlock, top - textBlock.FontSize - 4);

                    // Add to canvas
                    AnnotationCanvas.Children.Add(textBlock);
                }
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"Error drawing annotation: {ex.Message}");
            }
        }

        private void EditStateManager_EditStateChanged(object sender, EventArgs e)
        {
            // Update the UI when edit states change
            if (_currentImage != null)
            {
                _currentImage.IsEdited = _editStateManager.IsEdited(_currentImage.FileName);
            }
        }

        #endregion

        #region Parse Dataset

        private async void LoadDataset()
        {
            try
            {
                // Clear existing data
                _allImages.Clear();
                _classNames.Clear();
                _classColors.Clear();
                _classItems.Clear();
                AnnotationCanvas.Children.Clear();
                MainImage.Source = null;

                // Update UI
                StatusText.Text = "Loading dataset...";
                Mouse.OverrideCursor = System.Windows.Input.Cursors.Wait;

                // Determine base path (directory containing the YAML file)
                _datasetBasePath = System.IO.Path.GetDirectoryName(_yamlFilePath);

                // Initialize the EditStateManager
                _editStateManager = new EditStateManager(_yamlFilePath);
                _editStateManager.EditStateChanged += EditStateManager_EditStateChanged;

                // Parse YAML file
                await Task.Run(() => ParseYamlFile());

                // Generate colors for classes
                GenerateClassColors();

                // Update class list
                UpdateClassList();

                // Load images
                await LoadImages();

                // Update statistics tab
                UpdateStatistics();

                // Update status
                string msg = $"Loaded {_allImages.Count} images from dataset";
                StatusText.Text = msg;
                Notify.sendSuccess(msg);
            }
            catch (Exception ex)
            {
                Notify.sendError($"Error loading dataset: {ex.Message}");
                StatusText.Text = "Error loading dataset";
                Trace.WriteLine($"Error loading dataset: {ex}");
            }
            finally
            {
                Mouse.OverrideCursor = null;
            }
        }

        private void ParseYamlFile()
        {
            try
            {
                // Read YAML content
                string yamlContent = File.ReadAllText(_yamlFilePath);

                // Create deserializer
                var deserializer = new DeserializerBuilder()
                    .WithNamingConvention(CamelCaseNamingConvention.Instance)
                    .Build();

                // Parse YAML
                var yamlData = deserializer.Deserialize<Dictionary<string, object>>(yamlContent);

                // Extract class names
                if (yamlData.TryGetValue("names", out object namesObj))
                {
                    if (namesObj is Dictionary<object, object> namesDict)
                    {
                        foreach (var entry in namesDict)
                        {
                            if (int.TryParse(entry.Key.ToString(), out int classId))
                            {
                                _classNames[classId] = entry.Value.ToString();
                            }
                        }
                    }
                    else if (namesObj is List<object> namesList)
                    {
                        for (int i = 0; i < namesList.Count; i++)
                        {
                            _classNames[i] = namesList[i].ToString();
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"Error parsing YAML: {ex}");
                throw new Exception($"Failed to parse YAML file: {ex.Message}");
            }
        }

        private void GenerateClassColors()
        {
            foreach (var classId in _classNames.Keys)
            {
                // Generate a consistent color for each class ID
                byte r = (byte)(_random.Next(100, 250));
                byte g = (byte)(_random.Next(100, 250));
                byte b = (byte)(_random.Next(100, 250));

                _classColors[classId] = new SolidColorBrush(Color.FromRgb(r, g, b));
            }
        }

        private void UpdateClassList()
        {
            foreach (var entry in _classNames.OrderBy(c => c.Key))
            {
                _classItems.Add(new ClassItem
                {
                    ClassId = entry.Key,
                    Name = entry.Value,
                    Color = _classColors[entry.Key]
                });
            }
        }
        private async Task LoadImages()
        {
            try
            {
                // Get directories for train and val images
                string trainImagesPath = Path.Combine(_datasetBasePath, "images", "train");
                string valImagesPath = Path.Combine(_datasetBasePath, "images", "val");

                // Get corresponding label directories
                string trainLabelsPath = Path.Combine(_datasetBasePath, "labels", "train");
                string valLabelsPath = Path.Combine(_datasetBasePath, "labels", "val");

                // Define paths to process
                var imageDirectories = new Dictionary<string, string>
        {
            { trainImagesPath, trainLabelsPath },
            { valImagesPath, valLabelsPath }
        };

                // Process each directory
                foreach (var dirPair in imageDirectories)
                {
                    string imagesDir = dirPair.Key;
                    string labelsDir = dirPair.Value;

                    if (!Directory.Exists(imagesDir) || !Directory.Exists(labelsDir))
                    {
                        continue;
                    }

                    // Get image files and sort them alphabetically
                    var imageFiles = Directory.GetFiles(imagesDir, "*.*")
                        .Where(f => f.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ||
                                   f.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase) ||
                                   f.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                        .OrderBy(f => Path.GetFileName(f))
                        .ToList();

                    //Check if we have images without labels
                    bool missingLabelFiles = false;
                    foreach (var imageFile in imageFiles)
                    {
                        string fileNameWithoutExt = Path.GetFileNameWithoutExtension(imageFile);
                        string labelFile = Path.Combine(labelsDir, fileNameWithoutExt + ".txt");
                        if (!File.Exists(labelFile))
                        {
                            missingLabelFiles = true;
                            break;
                        }
                    }
                    //Ask user if we should generate empty label files
                    if (missingLabelFiles)
                    {
                        var result = MessageBox.Show("Some images do not have corresponding label files. Would you like to generate empty label files for them?",
                            "Missing Label Files", MessageBoxButton.YesNo, MessageBoxImage.Question);
                        if (result == MessageBoxResult.Yes)
                        {
                            foreach (var imageFile in imageFiles)
                            {
                                string fileNameWithoutExt = Path.GetFileNameWithoutExtension(imageFile);
                                string labelFile = Path.Combine(labelsDir, fileNameWithoutExt + ".txt");
                                if (!File.Exists(labelFile))
                                {
                                    File.Create(labelFile).Close();
                                    Trace.WriteLine($"Created empty label file: {labelFile}");
                                }
                            }
                        }
                    }

                    // Process each image
                    foreach (var imageFile in imageFiles)
                    {
                        string fileName = Path.GetFileName(imageFile);
                        string fileNameWithoutExt = Path.GetFileNameWithoutExtension(imageFile);
                        string labelFile = Path.Combine(labelsDir, fileNameWithoutExt + ".txt");

                        // Skip if no label file exists
                        if (!File.Exists(labelFile))
                        {
                            continue;
                        }

                        // Parse label file to get bounding boxes
                        var labels = File.ReadAllLines(labelFile)
                            .Where(line => !string.IsNullOrWhiteSpace(line))
                            .Select(line => ParseYoloLabel(line))
                            .Where(label => label != null)
                            .ToList();

                        // Create thumbnail
                        BitmapImage thumbnail = await Task.Run(() => CreateThumbnail(imageFile, 200, 150));

                        // Check if the image is marked as edited
                        bool isEdited = _editStateManager.IsEdited(fileName);

                        // Add to collection
                        await Dispatcher.InvokeAsync(() =>
                        {
                            _allImages.Add(new ImageItem
                            {
                                FileName = fileName,
                                FilePath = imageFile,
                                LabelPath = labelFile,
                                Thumbnail = thumbnail,
                                Annotations = labels,
                                ClassIds = labels.Select(l => l.ClassId).Distinct().ToList(),
                                IsEdited = isEdited
                            });
                        });
                    }
                }

                // Update image index counter
                UpdateImageIndexCounter();
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"Error loading images: {ex}");
                throw;
            }
        }

        private YoloLabel ParseYoloLabel(string line)
        {
            try
            {
                string[] parts = line.Trim().Split(' ');
                if (parts.Length < 5)
                    return null;

                if (int.TryParse(parts[0], out int classId) &&
                    float.TryParse(parts[1], out float centerX) &&
                    float.TryParse(parts[2], out float centerY) &&
                    float.TryParse(parts[3], out float width) &&
                    float.TryParse(parts[4], out float height))
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

        private BitmapImage CreateThumbnail(string imagePath, int maxWidth, int maxHeight)
        {
            try
            {
                BitmapImage image = new BitmapImage();
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.UriSource = new Uri(imagePath);
                image.DecodePixelWidth = maxWidth;
                image.EndInit();
                image.Freeze(); // Important for cross-thread usage

                return image;
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"Error creating thumbnail: {ex.Message}");
                return null;
            }
        }

        #endregion

        #region Statistics

        private void StatisticsTab_GotFocus(object sender, RoutedEventArgs e)
        {
            if (_allImages.Count > 0)
            {
                UpdateStatistics();
            }
        }

        private void UpdateStatistics()
        {
            try
            {
                // Update basic stats
                TxtTotalImages.Text = _allImages.Count.ToString();
                int totalAnnotations = _allImages.Sum(img => img.Annotations.Count);
                TxtTotalAnnotations.Text = totalAnnotations.ToString();
                TxtUniqueClasses.Text = _classNames.Count.ToString();

                // Add count of edited images
                int editedCount = _allImages.Count(img => img.IsEdited);
                TxtEditedImages.Text = $"{editedCount} ({(editedCount * 100.0 / _allImages.Count):F1}%)";

                // Generate class distribution chart data
                var classDistribution = new Dictionary<int, int>();

                // Count annotations per class
                foreach (var image in _allImages)
                {
                    foreach (var annotation in image.Annotations)
                    {
                        if (!classDistribution.ContainsKey(annotation.ClassId))
                            classDistribution[annotation.ClassId] = 0;

                        classDistribution[annotation.ClassId]++;
                    }
                }

                // Sort by class ID
                var sortedDistribution = classDistribution.OrderBy(pair => pair.Key).ToList();

                // Create chart series
                var series = new ISeries[]
                {
            new ColumnSeries<int>
            {
                Values = sortedDistribution.Select(pair => pair.Value).ToArray(),
                Stroke = null,
                Fill = new SolidColorPaint(SKColors.DodgerBlue),
                Name = "Count"
            }
                };

                // Create X-axis labels
                var xLabels = sortedDistribution.Select(pair =>
                    _classNames.ContainsKey(pair.Key) ? $"{pair.Key}: {_classNames[pair.Key]}" : $"Class {pair.Key}").ToArray();

                // Set chart properties
                ClassDistributionChart.Series = series;
                ClassDistributionChart.XAxes = new Axis[]
                {
            new Axis
            {
                Labels = xLabels,
                LabelsRotation = 45,
                LabelsPaint = new SolidColorPaint(SKColors.Black)
            }
                };

                ClassDistributionChart.YAxes = new Axis[]
                {
            new Axis
            {
                Name = "Number of Annotations",
                LabelsPaint = new SolidColorPaint(SKColors.Black)
            }
                };

                StatusText.Text = "Statistics updated";
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"Error updating statistics: {ex}");
                StatusText.Text = "Error updating statistics";
            }
        }

        #endregion

        #region Image Index
        private void UpdateImageIndexCounter()
        {
            if (_imageIndexCounter != null && LvThumbnails.SelectedItem != null)
            {
                int currentIndex = LvThumbnails.SelectedIndex + 1;
                int totalCount = _filteredImages.View.Cast<object>().Count();
                _imageIndexCounter.Text = $"Image {currentIndex}/{totalCount}";
            }
            else if (_imageIndexCounter != null)
            {
                _imageIndexCounter.Text = $"Image 0/{_filteredImages.View.Cast<object>().Count()}";
            }
        }
        #endregion

        #region AI Annotations

        private void BrowseOnnxButton_Click(object sender, RoutedEventArgs e)
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

        private void BtnDetectUsingYolo_Click(object sender, RoutedEventArgs e)
        {
            GenerateAnnotationsFromYolo(false);
        }

        private void BtnRedetectUsingYolo_Click(object sender, RoutedEventArgs e)
        {
            GenerateAnnotationsFromYolo(true);
        }

        private void GenerateAnnotationsFromYolo(bool clearAnnoations)
        {
            if (_yolo == null)
            {
                Notify.sendError("YOLO model is not loaded");
                return;
            }

            if (MainImage.Source is not BitmapImage image)
            {
                return;
            }

            var skImg = image.ToSKImage();

            var labels = _yolo.RunObjectDetection(skImg).Where(x => x.Confidence > 0.4 && x.BoundingBox.Width > 5 && x.BoundingBox.Height > 5);
            
            if(labels.Any())
            {
                if (clearAnnoations && _currentImage != null)
                {
                    // Clear and redraw all annotations
                    AnnotationCanvas.Children.Clear();
                    _currentImage.Annotations.Clear();
                    foreach (var annotation in _currentImage.Annotations)
                    {
                        DrawAnnotation(annotation, image.PixelWidth, image.PixelHeight);
                    }
                    // Mark as dirty
                    _isDirty = true;
                }
            }
            //At least 40% confidence and bounding box size > 5 pixels
            foreach (var label in labels)
            {
                GenerateAnnotationFromYolo(label, clearAnnoations);
            }
        }

        private void GenerateAnnotationFromYolo(ObjectDetection label, bool clearAnnoations)
        {
            if (label == null || label.Label == null)
            {
                Notify.sendWarn("Invalid label data");
                return;
            }
            if (!_classNames.ContainsKey(label.Label.Index))
            {
                Notify.sendError($"Class ID {label.Label.Index} not found in class names");
                return;
            }
            if (_classNames[label.Label.Index]?.ToLower().Trim() != label.Label.Name?.ToLower().Trim())
            {
                Notify.sendError($"Class name mismatch for ID {label.Label.Index} | {_classNames[label.Label.Index]?.ToLower().Trim()} does not match {label.Label.Name?.ToLower().Trim()}");
                return;
            }

            if (MainImage.Source is not BitmapImage image)
            {
                return;
            }

            var bb = label.BoundingBox;

            double x = bb.MidX / image.Width;
            double y = bb.MidY / image.Height;
            double width = bb.Width / image.Width;
            double height = bb.Height / image.Height;

            // Create new YOLO label
            YoloLabel newLabel = new YoloLabel
            {
                ClassId = label.Label.Index,
                CenterX = (float)x,
                CenterY = (float)y,
                Width = (float)width,
                Height = (float)height
            };



            // Add to the current image's annotations
            if (_currentImage != null)
            {
                _currentImage.Annotations.Add(newLabel);

                // Mark as dirty
                _isDirty = true;

                // Add class to current image if not already present
                if (!_currentImage.ClassIds.Contains(label.Label.Index))
                {
                    _currentImage.ClassIds.Add(label.Label.Index);
                }

                // Redraw all annotations
                DrawAnnotation(newLabel, image.PixelWidth, image.PixelHeight);
                StatusText.Text = $"Added annotation for class {label.Label.Name}";
            }
        }


        #endregion

    }
}
