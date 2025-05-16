using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
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
using YamlDotNet.Serialization.NamingConventions;
using YamlDotNet.Serialization;
using MessageBox = System.Windows.MessageBox;
using System.IO;

namespace YoloAnnotationEditor
{
    public partial class YamlEditorControl : System.Windows.Controls.UserControl
    {
        private string _currentFilePath;
        private bool _hasUnsavedChanges;
        private ObservableCollection<ClassItem> _allClasses;
        private ObservableCollection<ClassItem> _filteredClasses;

        public event EventHandler<string> YamlFileLoaded;
        public event EventHandler<string> YamlFileSaved;

        public YamlEditorControl()
        {
            InitializeComponent();

            _allClasses = new ObservableCollection<ClassItem>();
            _filteredClasses = new ObservableCollection<ClassItem>();
            dgClasses.ItemsSource = _filteredClasses;

            UpdateNumClasses();
            UpdateUIState();
        }

        #region File Operations

        private void btnNew_Click(object sender, RoutedEventArgs e)
        {
            if (_hasUnsavedChanges)
            {
                var result = MessageBox.Show("You have unsaved changes. Do you want to save them before creating a new file?",
                    "Unsaved Changes", MessageBoxButton.YesNoCancel, MessageBoxImage.Warning);

                if (result == MessageBoxResult.Yes)
                {
                    SaveCurrentFile();
                }
                else if (result == MessageBoxResult.Cancel)
                {
                    return;
                }
            }

            ClearForm();
            SetDefaultValues();
            _currentFilePath = null;
            _hasUnsavedChanges = false;
            UpdateUIState();
        }

        private void btnOpen_Click(object sender, RoutedEventArgs e)
        {
            if (_hasUnsavedChanges)
            {
                var result = MessageBox.Show("You have unsaved changes. Do you want to save them before opening a different file?",
                    "Unsaved Changes", MessageBoxButton.YesNoCancel, MessageBoxImage.Warning);

                if (result == MessageBoxResult.Yes)
                {
                    SaveCurrentFile();
                }
                else if (result == MessageBoxResult.Cancel)
                {
                    return;
                }
            }

            var openFileDialog = new OpenFileDialog
            {
                Filter = "YAML Files (*.yaml;*.yml)|*.yaml;*.yml|All Files (*.*)|*.*",
                Title = "Open YAML File"
            };

            if (openFileDialog.ShowDialog() == DialogResult.OK)
            {
                OpenYamlFile(openFileDialog.FileName);
            }
        }

        private void btnSave_Click(object sender, RoutedEventArgs e)
        {
            SaveCurrentFile();
        }

        private void btnSaveAs_Click(object sender, RoutedEventArgs e)
        {
            SaveFileAs();
        }

        private void OpenYamlFile(string filePath)
        {
            try
            {
                var yamlText = File.ReadAllText(filePath);
                var deserializer = new DeserializerBuilder()
                    .WithNamingConvention(CamelCaseNamingConvention.Instance)
                    .Build();

                var yamlData = deserializer.Deserialize<YoloConfig>(yamlText);

                ClearForm();

                // Load data into UI
                txtBasePath.Text = yamlData.Path;
                txtTrainPath.Text = yamlData.Train;
                txtValPath.Text = yamlData.Val;
                txtTestPath.Text = yamlData.Test;

                // Load class names
                _allClasses.Clear();
                if (yamlData.Names != null)
                {
                    foreach (var pair in yamlData.Names)
                    {
                        _allClasses.Add(new ClassItem { Id = pair.Key, Name = pair.Value });
                    }
                }

                UpdateFilteredClasses();
                UpdateNumClasses();

                _currentFilePath = filePath;
                _hasUnsavedChanges = false;
                UpdateUIState();

                YamlFileLoaded?.Invoke(this, filePath);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error opening YAML file: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private bool SaveCurrentFile()
        {
            if (string.IsNullOrEmpty(_currentFilePath))
            {
                return SaveFileAs();
            }
            else
            {
                return SaveYamlFile(_currentFilePath);
            }
        }

        private bool SaveFileAs()
        {
            var saveFileDialog = new SaveFileDialog
            {
                Filter = "YAML Files (*.yaml)|*.yaml|YAML Files (*.yml)|*.yml|All Files (*.*)|*.*",
                Title = "Save YAML File",
                DefaultExt = ".yaml"
            };
            saveFileDialog.ShowDialog();

            if (!string.IsNullOrEmpty(saveFileDialog.FileName))
                return SaveYamlFile(saveFileDialog.FileName);

            return false;
        }

        private bool SaveYamlFile(string filePath)
        {
            try
            {
                var yamlData = new YoloConfig
                {
                    Path = txtBasePath.Text,
                    Train = txtTrainPath.Text,
                    Val = txtValPath.Text,
                    Test = txtTestPath.Text,
                    Nc = _allClasses.Count,
                    Names = _allClasses.ToDictionary(c => c.Id, c => c.Name)
                };

                var serializer = new SerializerBuilder()
                    .WithNamingConvention(CamelCaseNamingConvention.Instance)
                    .Build();

                var yamlText = serializer.Serialize(yamlData);
                File.WriteAllText(filePath, yamlText);

                _currentFilePath = filePath;
                _hasUnsavedChanges = false;
                UpdateUIState();

                YamlFileSaved?.Invoke(this, filePath);

                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error saving YAML file: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
        }

        #endregion

        #region Class Management

        private void btnAddClass_Click(object sender, RoutedEventArgs e)
        {
            // Find the next available ID
            int nextId = 0;
            if (_allClasses.Count > 0)
            {
                nextId = _allClasses.Max(c => c.Id) + 1;
            }

            var newClass = new ClassItem { Id = nextId, Name = "new_class" };
            _allClasses.Add(newClass);

            UpdateFilteredClasses();
            UpdateNumClasses();
            _hasUnsavedChanges = true;
            UpdateUIState();

            // Select the newly added class
            dgClasses.SelectedItem = _filteredClasses.FirstOrDefault(c => c.Id == nextId);
            dgClasses.ScrollIntoView(dgClasses.SelectedItem);
        }

        private void btnRemoveClass_Click(object sender, RoutedEventArgs e)
        {
            if (dgClasses.SelectedItem is ClassItem selectedClass)
            {
                var result = MessageBox.Show($"Are you sure you want to remove the class '{selectedClass.Name}'?",
                    "Confirm Removal", MessageBoxButton.YesNo, MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes)
                {
                    _allClasses.Remove(_allClasses.First(c => c.Id == selectedClass.Id));
                    UpdateFilteredClasses();
                    UpdateNumClasses();
                    _hasUnsavedChanges = true;
                    UpdateUIState();
                }
            }
            else
            {
                MessageBox.Show("Please select a class to remove.", "No Selection", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void dgClasses_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
        {
            if (e.EditAction == DataGridEditAction.Commit)
            {
                // Set unsaved changes flag
                _hasUnsavedChanges = true;
                UpdateUIState();

                // If we're changing the name, make sure to update the actual classes collection
                if (e.Column.Header.ToString() == "Class Name" && e.Row.Item is ClassItem item)
                {
                    var textBox = e.EditingElement as System.Windows.Controls.TextBox;
                    if (textBox != null)
                    {
                        string newName = textBox.Text;
                        var actualItem = _allClasses.First(c => c.Id == item.Id);
                        actualItem.Name = newName;
                    }
                }
            }
        }

        #endregion

        #region UI Management

        private void btnBrowseBasePath_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new System.Windows.Forms.FolderBrowserDialog();
            System.Windows.Forms.DialogResult result = dialog.ShowDialog();

            if (result == System.Windows.Forms.DialogResult.OK)
            {
                txtBasePath.Text = dialog.SelectedPath;
                _hasUnsavedChanges = true;
                UpdateUIState();
            }
        }

        private void txtSearch_TextChanged(object sender, TextChangedEventArgs e)
        {
            UpdateFilteredClasses();
        }

        private void btnClearSearch_Click(object sender, RoutedEventArgs e)
        {
            txtSearch.Clear();
        }

        private void rbSortById_Checked(object sender, RoutedEventArgs e)
        {
            UpdateFilteredClasses();
        }

        private void rbSortByName_Checked(object sender, RoutedEventArgs e)
        {
            UpdateFilteredClasses();
        }

        private void UpdateFilteredClasses()
        {
            var searchText = txtSearch.Text?.ToLower() ?? string.Empty;

            if (_allClasses == null) return;

            IEnumerable<ClassItem> filtered = _allClasses
                .Where(c => string.IsNullOrEmpty(searchText) ||
                           c.Name.ToLower().Contains(searchText) ||
                           c.Id.ToString().Contains(searchText));

            // Apply sorting
            if (rbSortById.IsChecked == true)
            {
                filtered = filtered.OrderBy(c => c.Id);
            }
            else
            {
                filtered = filtered.OrderBy(c => c.Name);
            }

            _filteredClasses.Clear();
            foreach (var item in filtered)
            {
                _filteredClasses.Add(item);
            }
        }

        private void UpdateNumClasses()
        {
            txtNumClasses.Text = _allClasses.Count.ToString();
        }

        private void UpdateUIState()
        {
            // Update file display
            runCurrentFile.Text = string.IsNullOrEmpty(_currentFilePath)
                ? "[No file loaded]"
                : System.IO.Path.GetFileName(_currentFilePath);

            // Update window title to indicate unsaved changes
            var parentWindow = Window.GetWindow(this);
            if (parentWindow != null)
            {
                string baseTitle = "YOLO YAML Editor";
                if (!string.IsNullOrEmpty(_currentFilePath))
                {
                    baseTitle += $" - {System.IO.Path.GetFileName(_currentFilePath)}";
                }

                if (_hasUnsavedChanges)
                {
                    baseTitle += " *";
                }

                parentWindow.Title = baseTitle;
            }

            // Enable/disable save button based on whether there's a file path
            btnSave.IsEnabled = !string.IsNullOrEmpty(_currentFilePath);
        }

        private void ClearForm()
        {
            txtBasePath.Clear();
            txtTrainPath.Clear();
            txtValPath.Clear();
            txtTestPath.Clear();
            _allClasses.Clear();
            UpdateFilteredClasses();
            UpdateNumClasses();
        }

        private void SetDefaultValues()
        {
            txtTrainPath.Text = "images/train";
            txtValPath.Text = "images/val";
            txtTestPath.Text = "images/test";
            UpdateFilteredClasses();
            UpdateNumClasses();
        }

        #endregion

        #region Data Binding

        // Class to represent a class item in the data grid
        public class ClassItem : INotifyPropertyChanged
        {
            private int _id;
            private string _name;

            public int Id
            {
                get => _id;
                set
                {
                    if (_id != value)
                    {
                        _id = value;
                        OnPropertyChanged(nameof(Id));
                    }
                }
            }

            public string Name
            {
                get => _name;
                set
                {
                    if (_name != value)
                    {
                        _name = value;
                        OnPropertyChanged(nameof(Name));
                    }
                }
            }

            public event PropertyChangedEventHandler PropertyChanged;

            protected void OnPropertyChanged(string propertyName)
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            }
        }

        // Class for YAML serialization/deserialization
        public class YoloConfig
        {
            public string Path { get; set; }
            public string Train { get; set; }
            public string Val { get; set; }
            public string Test { get; set; }
            public int Nc { get; set; }
            public Dictionary<int, string> Names { get; set; } = new Dictionary<int, string>();
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Open a YAML file programmatically
        /// </summary>
        public void OpenFile(string filePath)
        {
            if (File.Exists(filePath))
            {
                OpenYamlFile(filePath);
            }
            else
            {
                MessageBox.Show($"File not found: {filePath}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// Save current YAML file
        /// </summary>
        public bool Save()
        {
            return SaveCurrentFile();
        }

        /// <summary>
        /// Get the current YAML file path
        /// </summary>
        public string GetCurrentFilePath()
        {
            return _currentFilePath;
        }

        /// <summary>
        /// Check if there are unsaved changes
        /// </summary>
        public bool HasUnsavedChanges()
        {
            return _hasUnsavedChanges;
        }

        /// <summary>
        /// Create a new YAML file with default values
        /// </summary>
        public void CreateNewFile()
        {
            btnNew_Click(null, null);
        }

        #endregion
    }

}
