using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms;
using YoloAnnotationEditor.Models;
using Application = System.Windows.Application;
using System.Windows.Threading;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace YoloAnnotationEditor
{
    public partial class YoloDatasetToolsControl : System.Windows.Controls.UserControl
    {
        private List<string> mergeDatasetPaths = new List<string>();
        private Dictionary<int, string> datasetClasses = new Dictionary<int, string>();

        public YoloDatasetToolsControl()
        {
            InitializeComponent();
        }

        #region Merge Datasets

        private void BrowseMergeOutput_Click(object sender, RoutedEventArgs e)
        {
            using (var dialog = new FolderBrowserDialog())
            {
                dialog.Description = "Select output directory for merged dataset";
                if (dialog.ShowDialog() == DialogResult.OK)
                {
                    txtMergeOutput.Text = dialog.SelectedPath;
                }
            }
        }

        private void AddMergeDataset_Click(object sender, RoutedEventArgs e)
        {
            using (var dialog = new FolderBrowserDialog())
            {
                dialog.Description = "Select YOLO dataset folder (containing images and labels folders)";
                if (dialog.ShowDialog() == DialogResult.OK)
                {
                    string path = dialog.SelectedPath;
                    if (ValidateYoloDataset(path))
                    {
                        if (!mergeDatasetPaths.Contains(path))
                        {
                            mergeDatasetPaths.Add(path);
                            UpdateMergeDatasetsList();
                            LogMergeMessage($"Added dataset: {path}");
                        }
                        else
                        {
                            System.Windows.MessageBox.Show("This dataset is already in the list.", "Duplicate Dataset", 
                                MessageBoxButton.OK, MessageBoxImage.Information);
                        }
                    }
                    else
                    {
                        System.Windows.MessageBox.Show("Invalid YOLO dataset structure. Expected 'images' and 'labels' folders.", 
                            "Invalid Dataset", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
            }
        }

        private void RemoveMergeDataset_Click(object sender, RoutedEventArgs e)
        {
            if (lstMergeDatasets.SelectedIndex >= 0)
            {
                int index = lstMergeDatasets.SelectedIndex;
                string removed = mergeDatasetPaths[index];
                mergeDatasetPaths.RemoveAt(index);
                UpdateMergeDatasetsList();
                LogMergeMessage($"Removed dataset: {removed}");
            }
        }

        private void ClearMergeDatasets_Click(object sender, RoutedEventArgs e)
        {
            mergeDatasetPaths.Clear();
            UpdateMergeDatasetsList();
            LogMergeMessage("Cleared all datasets");
        }

        private void UpdateMergeDatasetsList()
        {
            lstMergeDatasets.Items.Clear();
            foreach (var path in mergeDatasetPaths)
            {
                lstMergeDatasets.Items.Add(Path.GetFileName(path) + " - " + path);
            }
        }

        private async void MergeDatasets_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(txtMergeOutput.Text))
            {
                System.Windows.MessageBox.Show("Please select an output directory.", "Missing Output", 
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (mergeDatasetPaths.Count < 2)
            {
                System.Windows.MessageBox.Show("Please add at least 2 datasets to merge.", "Insufficient Datasets", 
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            btnMergeDatasets.IsEnabled = false;
            txtMergeLog.Clear();

            try
            {
                await Task.Run(() => MergeDatasets());
                LogMergeMessage("Merge completed successfully!");
                System.Windows.MessageBox.Show("Datasets merged successfully!", "Success", 
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                LogMergeMessage($"Error: {ex.Message}");
                System.Windows.MessageBox.Show($"Error merging datasets: {ex.Message}", "Error", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                btnMergeDatasets.IsEnabled = true;
            }
        }

        private void MergeDatasets()
        {
            string outputPath = "";
            bool includeTrain = false, includeVal = false, includeTest = false;

            Dispatcher.Invoke(() =>
            {
                outputPath = txtMergeOutput.Text;
                includeTrain = chkMergeTrain.IsChecked ?? false;
                includeVal = chkMergeVal.IsChecked ?? false;
                includeTest = chkMergeTest.IsChecked ?? false;
            });

            // Create output directory structure
            Directory.CreateDirectory(outputPath);
            string imagesPath = Path.Combine(outputPath, "images");
            string labelsPath = Path.Combine(outputPath, "labels");

            List<string> splits = new List<string>();
            if (includeTrain) splits.Add("train");
            if (includeVal) splits.Add("val");
            if (includeTest) splits.Add("test");

            foreach (var split in splits)
            {
                Directory.CreateDirectory(Path.Combine(imagesPath, split));
                Directory.CreateDirectory(Path.Combine(labelsPath, split));
            }

            LogMergeMessage($"Created output directory structure at: {outputPath}");

            // Merge class names from all datasets
            Dictionary<int, string> mergedClasses = new Dictionary<int, string>();
            foreach (var datasetPath in mergeDatasetPaths)
            {
                var classes = LoadClassesFromYaml(datasetPath);
                foreach (var kvp in classes)
                {
                    if (!mergedClasses.ContainsKey(kvp.Key))
                    {
                        mergedClasses[kvp.Key] = kvp.Value;
                    }
                    else if (mergedClasses[kvp.Key] != kvp.Value)
                    {
                        LogMergeMessage($"Warning: Class ID {kvp.Key} has different names: '{mergedClasses[kvp.Key]}' vs '{kvp.Value}'");
                    }
                }
            }

            int totalCopied = 0;

            // Copy files from each dataset
            foreach (var datasetPath in mergeDatasetPaths)
            {
                LogMergeMessage($"Processing dataset: {Path.GetFileName(datasetPath)}");

                foreach (var split in splits)
                {
                    string srcImagesPath = Path.Combine(datasetPath, "images", split);
                    string srcLabelsPath = Path.Combine(datasetPath, "labels", split);

                    if (!Directory.Exists(srcImagesPath))
                    {
                        LogMergeMessage($"  Skipping {split} split (not found)");
                        continue;
                    }

                    var imageFiles = Directory.GetFiles(srcImagesPath, "*.*")
                        .Where(f => f.EndsWith(".png", StringComparison.OrdinalIgnoreCase) || 
                                    f.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ||
                                    f.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase))
                        .ToList();

                    LogMergeMessage($"  Copying {imageFiles.Count} images from {split} split...");

                    foreach (var imageFile in imageFiles)
                    {
                        string fileName = Path.GetFileName(imageFile);
                        string fileNameWithoutExt = Path.GetFileNameWithoutExtension(imageFile);
                        string labelFile = Path.Combine(srcLabelsPath, fileNameWithoutExt + ".txt");

                        // Generate unique filename if conflict
                        string destImageFile = Path.Combine(imagesPath, split, fileName);
                        string destLabelFile = Path.Combine(labelsPath, split, fileNameWithoutExt + ".txt");

                        int counter = 1;
                        while (File.Exists(destImageFile))
                        {
                            string newFileName = $"{fileNameWithoutExt}_{counter}{Path.GetExtension(imageFile)}";
                            destImageFile = Path.Combine(imagesPath, split, newFileName);
                            destLabelFile = Path.Combine(labelsPath, split, $"{fileNameWithoutExt}_{counter}.txt");
                            counter++;
                        }

                        File.Copy(imageFile, destImageFile);
                        if (File.Exists(labelFile))
                        {
                            File.Copy(labelFile, destLabelFile);
                        }

                        totalCopied++;
                    }
                }
            }

            // Create dataset.yaml
            CreateDatasetYaml(outputPath, mergedClasses, splits);

            LogMergeMessage($"Total images copied: {totalCopied}");
            LogMergeMessage($"Created dataset.yaml with {mergedClasses.Count} classes");
        }

        #endregion

        #region Split Datasets

        private void BrowseSplitInput_Click(object sender, RoutedEventArgs e)
        {
            using (var dialog = new FolderBrowserDialog())
            {
                dialog.Description = "Select YOLO dataset folder to split";
                if (dialog.ShowDialog() == DialogResult.OK)
                {
                    if (ValidateYoloDataset(dialog.SelectedPath))
                    {
                        txtSplitInput.Text = dialog.SelectedPath;
                    }
                    else
                    {
                        System.Windows.MessageBox.Show("Invalid YOLO dataset structure.", "Invalid Dataset", 
                            MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
            }
        }

        private void BrowseSplitOutput_Click(object sender, RoutedEventArgs e)
        {
            using (var dialog = new FolderBrowserDialog())
            {
                dialog.Description = "Select output directory for split datasets";
                if (dialog.ShowDialog() == DialogResult.OK)
                {
                    txtSplitOutput.Text = dialog.SelectedPath;
                }
            }
        }

        private async void SplitDataset_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(txtSplitInput.Text) || string.IsNullOrEmpty(txtSplitOutput.Text))
            {
                System.Windows.MessageBox.Show("Please select input and output directories.", "Missing Paths", 
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            btnSplitDataset.IsEnabled = false;
            txtSplitLog.Clear();

            try
            {
                await Task.Run(() => SplitDataset());
                LogSplitMessage("Split completed successfully!");
                System.Windows.MessageBox.Show("Dataset split successfully!", "Success", 
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                LogSplitMessage($"Error: {ex.Message}");
                System.Windows.MessageBox.Show($"Error splitting dataset: {ex.Message}", "Error", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                btnSplitDataset.IsEnabled = true;
            }
        }

        private void SplitDataset()
        {
            string inputPath = "";
            string outputPath = "";
            bool splitIntoParts = false, splitByCount = false, extractSubset = false;
            int parts = 0, count = 0, subsetCount = 0;
            bool includeTrain = false, includeVal = false, includeTest = false;
            string seedText = "";

            Dispatcher.Invoke(() =>
            {
                inputPath = txtSplitInput.Text;
                outputPath = txtSplitOutput.Text;
                splitIntoParts = rbSplitIntoParts.IsChecked ?? false;
                splitByCount = rbSplitByCount.IsChecked ?? false;
                extractSubset = rbExtractSubset.IsChecked ?? false;
                int.TryParse(txtSplitParts.Text, out parts);
                int.TryParse(txtSplitCount.Text, out count);
                int.TryParse(txtSubsetCount.Text, out subsetCount);
                includeTrain = chkSplitTrain.IsChecked ?? false;
                includeVal = chkSplitVal.IsChecked ?? false;
                includeTest = chkSplitTest.IsChecked ?? false;
                seedText = txtSplitSeed.Text;
            });

            Random random = string.IsNullOrEmpty(seedText) ? new Random() : new Random(int.Parse(seedText));

            List<string> splits = new List<string>();
            if (includeTrain) splits.Add("train");
            if (includeVal) splits.Add("val");
            if (includeTest) splits.Add("test");

            var classes = LoadClassesFromYaml(inputPath);

            if (extractSubset)
            {
                // Extract random subset
                LogSplitMessage($"Extracting random subset of {subsetCount} images...");
                string subsetPath = Path.Combine(outputPath, "subset");
                Directory.CreateDirectory(subsetPath);

                CreateDirectoryStructure(subsetPath, splits);

                int totalExtracted = 0;

                foreach (var split in splits)
                {
                    var images = GetImagesFromSplit(inputPath, split);
                    
                    // Shuffle and take subset
                    var shuffled = images.OrderBy(x => random.Next()).Take(subsetCount).ToList();

                    LogSplitMessage($"  Extracting {shuffled.Count} images from {split} split...");

                    foreach (var image in shuffled)
                    {
                        CopyImageAndLabel(inputPath, subsetPath, split, image);
                        totalExtracted++;
                    }
                }

                CreateDatasetYaml(subsetPath, classes, splits);
                LogSplitMessage($"Total images extracted: {totalExtracted}");
            }
            else if (splitIntoParts)
            {
                // Split into N equal parts
                LogSplitMessage($"Splitting dataset into {parts} equal parts...");

                List<List<string>>[] splitGroups = new List<List<string>>[parts];
                for (int i = 0; i < parts; i++)
                {
                    splitGroups[i] = new List<List<string>>();
                }

                foreach (var split in splits)
                {
                    var images = GetImagesFromSplit(inputPath, split);
                    var shuffled = images.OrderBy(x => random.Next()).ToList();

                    int imagesPerPart = (int)Math.Ceiling((double)shuffled.Count / parts);

                    for (int i = 0; i < parts; i++)
                    {
                        var partImages = shuffled.Skip(i * imagesPerPart).Take(imagesPerPart).ToList();
                        splitGroups[i].Add(partImages);
                    }
                }

                // Create output datasets
                for (int i = 0; i < parts; i++)
                {
                    string partPath = Path.Combine(outputPath, $"part_{i + 1}");
                    Directory.CreateDirectory(partPath);
                    CreateDirectoryStructure(partPath, splits);

                    int partTotal = 0;
                    for (int j = 0; j < splits.Count; j++)
                    {
                        var images = splitGroups[i][j];
                        foreach (var image in images)
                        {
                            CopyImageAndLabel(inputPath, partPath, splits[j], image);
                            partTotal++;
                        }
                    }

                    CreateDatasetYaml(partPath, classes, splits);
                    LogSplitMessage($"Part {i + 1}: {partTotal} images");
                }
            }
            else if (splitByCount)
            {
                // Split by image count
                LogSplitMessage($"Splitting dataset into parts of {count} images each...");

                List<string> allImages = new List<string>();
                Dictionary<string, string> imageSplitMap = new Dictionary<string, string>();

                foreach (var split in splits)
                {
                    var images = GetImagesFromSplit(inputPath, split);
                    foreach (var img in images)
                    {
                        allImages.Add(img);
                        imageSplitMap[img] = split;
                    }
                }

                var shuffled = allImages.OrderBy(x => random.Next()).ToList();
                int numParts = (int)Math.Ceiling((double)shuffled.Count / count);

                for (int i = 0; i < numParts; i++)
                {
                    string partPath = Path.Combine(outputPath, $"part_{i + 1}");
                    Directory.CreateDirectory(partPath);
                    CreateDirectoryStructure(partPath, splits);

                    var partImages = shuffled.Skip(i * count).Take(count).ToList();

                    foreach (var image in partImages)
                    {
                        string originalSplit = imageSplitMap[image];
                        CopyImageAndLabel(inputPath, partPath, originalSplit, image);
                    }

                    CreateDatasetYaml(partPath, classes, splits);
                    LogSplitMessage($"Part {i + 1}: {partImages.Count} images");
                }
            }
        }

        #endregion

        #region Filter Datasets

        private void BrowseFilterInput_Click(object sender, RoutedEventArgs e)
        {
            using (var dialog = new FolderBrowserDialog())
            {
                dialog.Description = "Select YOLO dataset folder to filter";
                if (dialog.ShowDialog() == DialogResult.OK)
                {
                    if (ValidateYoloDataset(dialog.SelectedPath))
                    {
                        txtFilterInput.Text = dialog.SelectedPath;
                    }
                    else
                    {
                        System.Windows.MessageBox.Show("Invalid YOLO dataset structure.", "Invalid Dataset", 
                            MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
            }
        }

        private void BrowseFilterOutput_Click(object sender, RoutedEventArgs e)
        {
            using (var dialog = new FolderBrowserDialog())
            {
                dialog.Description = "Select output directory for filtered dataset";
                if (dialog.ShowDialog() == DialogResult.OK)
                {
                    txtFilterOutput.Text = dialog.SelectedPath;
                }
            }
        }

        private void LoadClasses_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(txtFilterInput.Text))
            {
                System.Windows.MessageBox.Show("Please select an input dataset first.", "No Dataset", 
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                datasetClasses = LoadClassesFromYaml(txtFilterInput.Text);
                lstFilterClasses.Items.Clear();

                foreach (var kvp in datasetClasses.OrderBy(x => x.Key))
                {
                    lstFilterClasses.Items.Add($"{kvp.Key}: {kvp.Value}");
                }

                LogFilterMessage($"Loaded {datasetClasses.Count} classes from dataset");
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Error loading classes: {ex.Message}", "Error", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void FilterDataset_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(txtFilterInput.Text) || string.IsNullOrEmpty(txtFilterOutput.Text))
            {
                System.Windows.MessageBox.Show("Please select input and output directories.", "Missing Paths", 
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            btnFilterDataset.IsEnabled = false;
            txtFilterLog.Clear();

            try
            {
                await Task.Run(() => FilterDataset());
                LogFilterMessage("Filter completed successfully!");
                System.Windows.MessageBox.Show("Dataset filtered successfully!", "Success", 
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                LogFilterMessage($"Error: {ex.Message}");
                System.Windows.MessageBox.Show($"Error filtering dataset: {ex.Message}", "Error", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                btnFilterDataset.IsEnabled = true;
            }
        }

        private void FilterDataset()
        {
            string inputPath = "";
            string outputPath = "";
            bool filterByClass = false, filterFirstN = false, filterRandomN = false;
            bool classContains = false, classAnd = false;
            int firstN = 0, randomN = 0;
            bool includeTrain = false, includeVal = false, includeTest = false;
            string seedText = "";
            List<int> selectedClasses = new List<int>();

            Dispatcher.Invoke(() =>
            {
                inputPath = txtFilterInput.Text;
                outputPath = txtFilterOutput.Text;
                filterByClass = rbFilterByClass.IsChecked ?? false;
                filterFirstN = rbFilterFirstN.IsChecked ?? false;
                filterRandomN = rbFilterRandomN.IsChecked ?? false;
                classContains = rbClassContains.IsChecked ?? false;
                classAnd = rbClassAnd.IsChecked ?? false;
                int.TryParse(txtFilterFirstN.Text, out firstN);
                int.TryParse(txtFilterRandomN.Text, out randomN);
                includeTrain = chkFilterTrain.IsChecked ?? false;
                includeVal = chkFilterVal.IsChecked ?? false;
                includeTest = chkFilterTest.IsChecked ?? false;
                seedText = txtFilterSeed.Text;

                // Get selected classes
                foreach (var item in lstFilterClasses.SelectedItems)
                {
                    string itemStr = item.ToString();
                    int classId = int.Parse(itemStr.Split(':')[0].Trim());
                    selectedClasses.Add(classId);
                }
            });

            Random random = string.IsNullOrEmpty(seedText) ? new Random() : new Random(int.Parse(seedText));

            List<string> splits = new List<string>();
            if (includeTrain) splits.Add("train");
            if (includeVal) splits.Add("val");
            if (includeTest) splits.Add("test");

            var classes = LoadClassesFromYaml(inputPath);

            Directory.CreateDirectory(outputPath);
            CreateDirectoryStructure(outputPath, splits);

            int totalFiltered = 0;

            foreach (var split in splits)
            {
                var images = GetImagesFromSplit(inputPath, split);
                List<string> filteredImages = new List<string>();

                if (filterByClass)
                {
                    LogFilterMessage($"Filtering {split} split by class...");

                    foreach (var image in images)
                    {
                        var imageClasses = GetImageClasses(inputPath, split, image);

                        bool matches = false;

                        if (classAnd)
                        {
                            // AND operation: image must contain ALL selected classes
                            if (classContains)
                            {
                                matches = selectedClasses.All(c => imageClasses.Contains(c));
                            }
                            else
                            {
                                // Does not contain: image must not contain ANY selected classes
                                matches = !selectedClasses.Any(c => imageClasses.Contains(c));
                            }
                        }
                        else
                        {
                            // OR operation: image must contain AT LEAST ONE selected class
                            if (classContains)
                            {
                                matches = selectedClasses.Any(c => imageClasses.Contains(c));
                            }
                            else
                            {
                                // Does not contain: image must not contain at least one selected class
                                matches = selectedClasses.Any(c => !imageClasses.Contains(c));
                            }
                        }

                        if (matches)
                        {
                            filteredImages.Add(image);
                        }
                    }
                }
                else if (filterFirstN)
                {
                    LogFilterMessage($"Taking first {firstN} images from {split} split...");
                    filteredImages = images.Take(firstN).ToList();
                }
                else if (filterRandomN)
                {
                    LogFilterMessage($"Taking random {randomN} images from {split} split...");
                    filteredImages = images.OrderBy(x => random.Next()).Take(randomN).ToList();
                }

                LogFilterMessage($"  Copying {filteredImages.Count} images from {split} split...");

                foreach (var image in filteredImages)
                {
                    CopyImageAndLabel(inputPath, outputPath, split, image);
                    totalFiltered++;
                }
            }

            CreateDatasetYaml(outputPath, classes, splits);
            LogFilterMessage($"Total images filtered: {totalFiltered}");
        }

        #endregion

        #region Helper Methods

        private bool ValidateYoloDataset(string path)
        {
            string imagesPath = Path.Combine(path, "images");
            string labelsPath = Path.Combine(path, "labels");

            return Directory.Exists(imagesPath) && Directory.Exists(labelsPath);
        }

        private Dictionary<int, string> LoadClassesFromYaml(string datasetPath)
        {
            Dictionary<int, string> classes = new Dictionary<int, string>();

            // Look for .yaml or .yml files
            var yamlFiles = Directory.GetFiles(datasetPath, "*.yaml")
                .Concat(Directory.GetFiles(datasetPath, "*.yml"))
                .ToList();

            if (yamlFiles.Count == 0)
            {
                return classes;
            }

            try
            {
                string yamlContent = File.ReadAllText(yamlFiles[0]);
                var deserializer = new DeserializerBuilder()
                    .WithNamingConvention(UnderscoredNamingConvention.Instance)
                    .Build();

                var yamlData = deserializer.Deserialize<Dictionary<string, object>>(yamlContent);

                if (yamlData.ContainsKey("names"))
                {
                    var names = yamlData["names"] as Dictionary<object, object>;
                    if (names != null)
                    {
                        foreach (var kvp in names)
                        {
                            int classId = Convert.ToInt32(kvp.Key);
                            string className = kvp.Value.ToString();
                            classes[classId] = className;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LogMergeMessage($"Warning: Could not parse YAML file: {ex.Message}");
            }

            return classes;
        }

        private void CreateDatasetYaml(string outputPath, Dictionary<int, string> classes, List<string> splits)
        {
            StringBuilder yaml = new StringBuilder();
            yaml.AppendLine($"path: {outputPath}");

            if (splits.Contains("train"))
                yaml.AppendLine("train: images/train");
            if (splits.Contains("val"))
                yaml.AppendLine("val: images/val");
            if (splits.Contains("test"))
                yaml.AppendLine("test: images/test");

            yaml.AppendLine($"nc: {classes.Count}");
            yaml.AppendLine("names:");

            foreach (var kvp in classes.OrderBy(x => x.Key))
            {
                yaml.AppendLine($"  {kvp.Key}: {kvp.Value}");
            }

            File.WriteAllText(Path.Combine(outputPath, "dataset.yaml"), yaml.ToString());
        }

        private void CreateDirectoryStructure(string basePath, List<string> splits)
        {
            foreach (var split in splits)
            {
                Directory.CreateDirectory(Path.Combine(basePath, "images", split));
                Directory.CreateDirectory(Path.Combine(basePath, "labels", split));
            }
        }

        private List<string> GetImagesFromSplit(string datasetPath, string split)
        {
            string imagesPath = Path.Combine(datasetPath, "images", split);

            if (!Directory.Exists(imagesPath))
                return new List<string>();

            return Directory.GetFiles(imagesPath, "*.*")
                .Where(f => f.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
                            f.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ||
                            f.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase))
                .Select(f => Path.GetFileNameWithoutExtension(f))
                .ToList();
        }

        private void CopyImageAndLabel(string srcDatasetPath, string destDatasetPath, string split, string imageNameWithoutExt)
        {
            string srcImagesPath = Path.Combine(srcDatasetPath, "images", split);
            string srcLabelsPath = Path.Combine(srcDatasetPath, "labels", split);
            string destImagesPath = Path.Combine(destDatasetPath, "images", split);
            string destLabelsPath = Path.Combine(destDatasetPath, "labels", split);

            // Find the actual image file (could be .png, .jpg, or .jpeg)
            string[] possibleExtensions = { ".png", ".jpg", ".jpeg", ".PNG", ".JPG", ".JPEG" };
            string srcImageFile = null;

            foreach (var ext in possibleExtensions)
            {
                string testPath = Path.Combine(srcImagesPath, imageNameWithoutExt + ext);
                if (File.Exists(testPath))
                {
                    srcImageFile = testPath;
                    break;
                }
            }

            if (srcImageFile == null)
                return;

            string destImageFile = Path.Combine(destImagesPath, Path.GetFileName(srcImageFile));
            string srcLabelFile = Path.Combine(srcLabelsPath, imageNameWithoutExt + ".txt");
            string destLabelFile = Path.Combine(destLabelsPath, imageNameWithoutExt + ".txt");

            File.Copy(srcImageFile, destImageFile, true);

            if (File.Exists(srcLabelFile))
            {
                File.Copy(srcLabelFile, destLabelFile, true);
            }
        }

        private HashSet<int> GetImageClasses(string datasetPath, string split, string imageNameWithoutExt)
        {
            HashSet<int> classes = new HashSet<int>();
            string labelFile = Path.Combine(datasetPath, "labels", split, imageNameWithoutExt + ".txt");

            if (!File.Exists(labelFile))
                return classes;

            try
            {
                var lines = File.ReadAllLines(labelFile);
                foreach (var line in lines)
                {
                    if (string.IsNullOrWhiteSpace(line))
                        continue;

                    var parts = line.Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length > 0)
                    {
                        if (int.TryParse(parts[0], out int classId))
                        {
                            classes.Add(classId);
                        }
                    }
                }
            }
            catch { }

            return classes;
        }

        private void LogMergeMessage(string message)
        {
            Dispatcher.Invoke(() =>
            {
                txtMergeLog.AppendText(message + Environment.NewLine);
                txtMergeLog.ScrollToEnd();
            });
        }

        private void LogSplitMessage(string message)
        {
            Dispatcher.Invoke(() =>
            {
                txtSplitLog.AppendText(message + Environment.NewLine);
                txtSplitLog.ScrollToEnd();
            });
        }

        private void LogFilterMessage(string message)
        {
            Dispatcher.Invoke(() =>
            {
                txtFilterLog.AppendText(message + Environment.NewLine);
                txtFilterLog.ScrollToEnd();
            });
        }

        #endregion
    }
}
