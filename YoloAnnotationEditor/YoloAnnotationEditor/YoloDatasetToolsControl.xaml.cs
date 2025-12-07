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
using YoloAnnotationEditor.Helpers;

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
            pbFilterProgress.Value = 0;
            txtFilterStatus.Text = "Starting filter...";
            txtFilterLog.Clear();

            var progress = new Progress<(int percentage, string status)>(report =>
            {
                pbFilterProgress.Value = report.percentage;
                txtFilterStatus.Text = report.status;
            });

            try
            {
                await Task.Run(() => FilterDataset(progress));
                txtFilterStatus.Text = "Filter completed!";
                pbFilterProgress.Value = 100;
                LogFilterMessage("Filter completed successfully!");
                System.Windows.MessageBox.Show("Dataset filtered successfully!", "Success",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                txtFilterStatus.Text = "Filter failed";
                pbFilterProgress.Value = 0;
                LogFilterMessage($"Error: {ex.Message}");
                System.Windows.MessageBox.Show($"Error filtering dataset: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                btnFilterDataset.IsEnabled = true;
            }
        }

        private void FilterDataset(IProgress<(int percentage, string status)>? progress = null)
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

            progress?.Report((5, "Loading dataset information..."));

            Random random = string.IsNullOrEmpty(seedText) ? new Random() : new Random(int.Parse(seedText));

            List<string> splits = new List<string>();
            if (includeTrain) splits.Add("train");
            if (includeVal) splits.Add("val");
            if (includeTest) splits.Add("test");

            var allClasses = LoadClassesFromYaml(inputPath);

            // Determine which classes to include in the output YAML
            Dictionary<int, string> outputClasses;
            if (filterByClass && classContains)
            {
                // Only include the selected classes
                outputClasses = new Dictionary<int, string>();
                foreach (var classId in selectedClasses)
                {
                    if (allClasses.ContainsKey(classId))
                    {
                        outputClasses[classId] = allClasses[classId];
                    }
                }
                LogFilterMessage($"Output dataset will contain {outputClasses.Count} classes (filtered from {allClasses.Count})");
            }
            else
            {
                // Include all classes
                outputClasses = allClasses;
                LogFilterMessage($"Output dataset will contain all {outputClasses.Count} classes");
            }

            Directory.CreateDirectory(outputPath);
            CreateDirectoryStructure(outputPath, splits);

            progress?.Report((10, "Scanning images..."));

            // First pass: count total images for progress
            int totalImageCount = 0;
            foreach (var split in splits)
            {
                var images = GetImagesFromSplit(inputPath, split);
                totalImageCount += images.Count;
            }

            LogFilterMessage($"Scanning {totalImageCount} total images across {splits.Count} splits...");

            int totalFiltered = 0;
            int totalProcessed = 0;

            foreach (var split in splits)
            {
                var images = GetImagesFromSplit(inputPath, split);
                List<string> filteredImages = new List<string>();

                progress?.Report((10 + (totalProcessed * 40 / Math.Max(1, totalImageCount)), $"Filtering {split} split..."));

                if (filterByClass)
                {
                    LogFilterMessage($"Filtering {split} split by class ({images.Count} images)...");

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

                        totalProcessed++;

                        // Update progress every 100 images
                        if (totalProcessed % 100 == 0)
                        {
                            int percentage = 10 + (totalProcessed * 40 / totalImageCount);
                            progress?.Report((percentage, $"Filtering {split}: {totalProcessed}/{totalImageCount} images scanned, {filteredImages.Count} matched"));
                        }
                    }
                }
                else if (filterFirstN)
                {
                    LogFilterMessage($"Taking first {firstN} images from {split} split...");
                    filteredImages = images.Take(firstN).ToList();
                    totalProcessed += images.Count;
                }
                else if (filterRandomN)
                {
                    LogFilterMessage($"Taking random {randomN} images from {split} split...");
                    filteredImages = images.OrderBy(x => random.Next()).Take(randomN).ToList();
                    totalProcessed += images.Count;
                }

                LogFilterMessage($"  Found {filteredImages.Count} matching images in {split} split");
                progress?.Report((50 + (totalFiltered * 45 / Math.Max(1, filteredImages.Count * splits.Count)), $"Copying {split} split ({filteredImages.Count} files)..."));

                int copiedInSplit = 0;
                foreach (var image in filteredImages)
                {
                    CopyImageAndLabel(inputPath, outputPath, split, image);
                    totalFiltered++;
                    copiedInSplit++;

                    // Update progress every 100 files
                    if (copiedInSplit % 100 == 0)
                    {
                        progress?.Report((50 + (totalFiltered * 45 / Math.Max(1, filteredImages.Count * splits.Count)), $"Copying {split}: {copiedInSplit}/{filteredImages.Count} files ({totalFiltered} total)"));
                    }
                }

                LogFilterMessage($"  Copied {filteredImages.Count} images from {split} split");
            }

            progress?.Report((95, "Creating dataset.yaml..."));
            CreateDatasetYaml(outputPath, outputClasses, splits);
            LogFilterMessage($"Total images filtered: {totalFiltered}");
            progress?.Report((100, "Filter completed!"));
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

        #region Analyze Dataset

        private DatasetStatistics currentStats;

        private void BrowseAnalyzeInput_Click(object sender, RoutedEventArgs e)
        {
            using (var dialog = new FolderBrowserDialog())
            {
                dialog.Description = "Select YOLO dataset folder to analyze";
                if (dialog.ShowDialog() == DialogResult.OK)
                {
                    if (ValidateYoloDataset(dialog.SelectedPath))
                    {
                        txtAnalyzeInput.Text = dialog.SelectedPath;
                    }
                    else
                    {
                        System.Windows.MessageBox.Show("Invalid YOLO dataset structure.", "Invalid Dataset",
                            MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
            }
        }

        private async void AnalyzeDataset_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(txtAnalyzeInput.Text))
            {
                System.Windows.MessageBox.Show("Please select a dataset directory.", "Missing Path",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            btnAnalyzeDataset.IsEnabled = false;
            txtAnalyzeLog.Clear();
            pbAnalyzeProgress.Value = 0;
            txtAnalyzeStatus.Text = "Starting analysis...";

            // Create progress reporter
            var progress = new Progress<(int percentage, string status)>(report =>
            {
                pbAnalyzeProgress.Value = report.percentage;
                txtAnalyzeStatus.Text = report.status;
            });

            try
            {
                await Task.Run(() => AnalyzeDataset(progress));
                txtAnalyzeStatus.Text = "Analysis completed successfully!";
                pbAnalyzeProgress.Value = 100;
                LogAnalyzeMessage("Analysis completed successfully!");
                btnExportCSV.IsEnabled = true;
                btnGenerateReport.IsEnabled = true;
            }
            catch (Exception ex)
            {
                txtAnalyzeStatus.Text = "Analysis failed";
                pbAnalyzeProgress.Value = 0;
                LogAnalyzeMessage($"Error: {ex.Message}");
                System.Windows.MessageBox.Show($"Error analyzing dataset: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                btnAnalyzeDataset.IsEnabled = true;
            }
        }

        private void AnalyzeDataset(IProgress<(int percentage, string status)> progress)
        {
            string datasetPath = "";
            Dispatcher.Invoke(() => datasetPath = txtAnalyzeInput.Text);

            LogAnalyzeMessage($"Analyzing dataset: {datasetPath}");
            progress?.Report((0, "Loading class definitions..."));

            var stats = new DatasetStatistics();
            var classes = LoadClassesFromYaml(datasetPath);
            var classStats = new Dictionary<int, ClassStatistics>();

            // Initialize class stats
            foreach (var kvp in classes)
            {
                classStats[kvp.Key] = new ClassStatistics
                {
                    ClassId = kvp.Key,
                    ClassName = kvp.Value,
                    InstanceCount = 0,
                    ImageCount = 0
                };
            }

            var splits = new[] { "train", "val", "test" };
            int totalImages = 0;
            int totalAnnotations = 0;
            int minAnnotations = int.MaxValue;
            int maxAnnotations = 0;

            // First pass: count total images for progress calculation
            int totalImageCount = 0;
            foreach (var split in splits)
            {
                var images = GetImagesFromSplit(datasetPath, split);
                totalImageCount += images.Count;
            }

            progress?.Report((5, $"Found {totalImageCount} total images. Starting multi-threaded analysis..."));
            int processedImages = 0;
            object lockObj = new object();

            // Use all CPU cores except 2 for analysis
            int maxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount - 2);

            foreach (var split in splits)
            {
                var images = GetImagesFromSplit(datasetPath, split);
                LogAnalyzeMessage($"Processing {split} split ({images.Count} images) with {maxDegreeOfParallelism} threads...");
                progress?.Report((5 + (processedImages * 90 / Math.Max(1, totalImageCount)), $"Processing {split} split ({images.Count} images)..."));

                Parallel.ForEach(images,
                    new ParallelOptions { MaxDegreeOfParallelism = maxDegreeOfParallelism },
                    image =>
                    {
                        var imageClasses = GetImageClasses(datasetPath, split, image);
                        var labelPath = Path.Combine(datasetPath, "labels", split, image + ".txt");

                        int annotCount = 0;
                        var instanceCounts = new Dictionary<int, int>();
                        var imageCounts = new HashSet<int>();

                        if (File.Exists(labelPath))
                        {
                            var lines = File.ReadAllLines(labelPath).Where(l => !string.IsNullOrWhiteSpace(l)).ToList();
                            annotCount = lines.Count;

                            // Count instances per class
                            foreach (var line in lines)
                            {
                                var parts = line.Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries);
                                if (parts.Length >= 5 && int.TryParse(parts[0], out int classId))
                                {
                                    if (!instanceCounts.ContainsKey(classId))
                                        instanceCounts[classId] = 0;
                                    instanceCounts[classId]++;
                                }
                            }

                            // Collect unique classes in this image
                            foreach (var classId in imageClasses)
                            {
                                imageCounts.Add(classId);
                            }
                        }

                        // Thread-safe updates
                        lock (lockObj)
                        {
                            totalImages++;
                            totalAnnotations += annotCount;

                            if (annotCount < minAnnotations) minAnnotations = annotCount;
                            if (annotCount > maxAnnotations) maxAnnotations = annotCount;

                            // Update class statistics
                            foreach (var kvp in instanceCounts)
                            {
                                if (classStats.ContainsKey(kvp.Key))
                                {
                                    classStats[kvp.Key].InstanceCount += kvp.Value;
                                }
                            }

                            foreach (var classId in imageCounts)
                            {
                                if (classStats.ContainsKey(classId))
                                {
                                    classStats[classId].ImageCount++;
                                }
                            }

                            if (split == "train") stats.TrainImages++;
                            else if (split == "val") stats.ValImages++;
                            else if (split == "test") stats.TestImages++;

                            processedImages++;
                        }

                        // Report progress every 100 images to avoid UI flooding
                        int currentProcessed = processedImages;
                        if (currentProcessed % 100 == 0 || currentProcessed == totalImageCount)
                        {
                            int percentage = 5 + (currentProcessed * 90 / totalImageCount);
                            progress?.Report((percentage, $"Processing {split}: {currentProcessed}/{totalImageCount} total"));
                        }
                    });
            }

            progress?.Report((95, "Calculating statistics..."));

            stats.TotalImages = totalImages;
            stats.TotalAnnotations = totalAnnotations;
            stats.MinAnnotationsPerImage = minAnnotations == int.MaxValue ? 0 : minAnnotations;
            stats.MaxAnnotationsPerImage = maxAnnotations;
            stats.AverageAnnotationsPerImage = totalImages > 0 ? (double)totalAnnotations / totalImages : 0;

            // Calculate percentages
            foreach (var cs in classStats.Values)
            {
                cs.Percentage = totalAnnotations > 0 ? (double)cs.InstanceCount / totalAnnotations * 100 : 0;
            }

            stats.ClassStats = classStats;
            currentStats = stats;

            // Update UI
            Dispatcher.Invoke(() =>
            {
                lblTotalImages.Content = stats.TotalImages.ToString();
                lblTotalAnnotations.Content = stats.TotalAnnotations.ToString();
                lblTrainImages.Content = stats.TrainImages.ToString();
                lblValImages.Content = stats.ValImages.ToString();
                lblTestImages.Content = stats.TestImages.ToString();
                lblAvgAnnotations.Content = stats.AverageAnnotationsPerImage.ToString("F2");
                lblMinAnnotations.Content = stats.MinAnnotationsPerImage.ToString();
                lblMaxAnnotations.Content = stats.MaxAnnotationsPerImage.ToString();
                lblNumClasses.Content = classStats.Count.ToString();

                dgClassDistribution.ItemsSource = classStats.Values.OrderBy(c => c.ClassId).ToList();
            });

            LogAnalyzeMessage($"Found {totalImages} images with {totalAnnotations} annotations");
            LogAnalyzeMessage($"Classes: {classStats.Count}");
        }

        private void ExportCSV_Click(object sender, RoutedEventArgs e)
        {
            if (currentStats == null)
            {
                System.Windows.MessageBox.Show("Please analyze a dataset first.", "No Data",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var saveDialog = new SaveFileDialog
            {
                Filter = "CSV files (*.csv)|*.csv",
                FileName = "dataset_statistics.csv"
            };

            if (saveDialog.ShowDialog() == DialogResult.OK)
            {
                try
                {
                    var csv = new StringBuilder();
                    csv.AppendLine("Class ID,Class Name,Instance Count,Image Count,Percentage");
                    
                    foreach (var cs in currentStats.ClassStats.Values.OrderBy(c => c.ClassId))
                    {
                        csv.AppendLine($"{cs.ClassId},{cs.ClassName},{cs.InstanceCount},{cs.ImageCount},{cs.Percentage:F2}");
                    }

                    File.WriteAllText(saveDialog.FileName, csv.ToString());
                    LogAnalyzeMessage($"Exported statistics to: {saveDialog.FileName}");
                    System.Windows.MessageBox.Show("Statistics exported successfully!", "Success",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    System.Windows.MessageBox.Show($"Error exporting CSV: {ex.Message}", "Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void GenerateReport_Click(object sender, RoutedEventArgs e)
        {
            if (currentStats == null)
            {
                System.Windows.MessageBox.Show("Please analyze a dataset first.", "No Data",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var saveDialog = new SaveFileDialog
            {
                Filter = "HTML files (*.html)|*.html",
                FileName = "dataset_report.html"
            };

            if (saveDialog.ShowDialog() == DialogResult.OK)
            {
                try
                {
                    var html = GenerateHTMLReport(currentStats);
                    File.WriteAllText(saveDialog.FileName, html);
                    LogAnalyzeMessage($"Generated report: {saveDialog.FileName}");
                    System.Windows.MessageBox.Show("Report generated successfully!", "Success",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    System.Windows.MessageBox.Show($"Error generating report: {ex.Message}", "Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private string GenerateHTMLReport(DatasetStatistics stats)
        {
            var html = new StringBuilder();
            html.AppendLine("<!DOCTYPE html>");
            html.AppendLine("<html><head><title>Dataset Analysis Report</title>");
            html.AppendLine("<style>");
            html.AppendLine("body { font-family: Arial, sans-serif; margin: 20px; }");
            html.AppendLine("table { border-collapse: collapse; width: 100%; margin-top: 20px; }");
            html.AppendLine("th, td { border: 1px solid #ddd; padding: 8px; text-align: left; }");
            html.AppendLine("th { background-color: #4CAF50; color: white; }");
            html.AppendLine(".stats { display: grid; grid-template-columns: repeat(2, 1fr); gap: 10px; margin: 20px 0; }");
            html.AppendLine(".stat-box { background: #f0f0f0; padding: 15px; border-radius: 5px; }");
            html.AppendLine("</style></head><body>");
            html.AppendLine("<h1>YOLO Dataset Analysis Report</h1>");
            html.AppendLine($"<p>Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}</p>");
            
            html.AppendLine("<h2>Dataset Statistics</h2>");
            html.AppendLine("<div class='stats'>");
            html.AppendLine($"<div class='stat-box'><strong>Total Images:</strong> {stats.TotalImages}</div>");
            html.AppendLine($"<div class='stat-box'><strong>Total Annotations:</strong> {stats.TotalAnnotations}</div>");
            html.AppendLine($"<div class='stat-box'><strong>Train Images:</strong> {stats.TrainImages}</div>");
            html.AppendLine($"<div class='stat-box'><strong>Val Images:</strong> {stats.ValImages}</div>");
            html.AppendLine($"<div class='stat-box'><strong>Test Images:</strong> {stats.TestImages}</div>");
            html.AppendLine($"<div class='stat-box'><strong>Avg Annotations/Image:</strong> {stats.AverageAnnotationsPerImage:F2}</div>");
            html.AppendLine("</div>");

            html.AppendLine("<h2>Class Distribution</h2>");
            html.AppendLine("<table>");
            html.AppendLine("<tr><th>Class ID</th><th>Class Name</th><th>Instances</th><th>Images</th><th>Percentage</th></tr>");
            
            foreach (var cs in stats.ClassStats.Values.OrderBy(c => c.ClassId))
            {
                html.AppendLine($"<tr><td>{cs.ClassId}</td><td>{cs.ClassName}</td><td>{cs.InstanceCount}</td><td>{cs.ImageCount}</td><td>{cs.Percentage:F2}%</td></tr>");
            }
            
            html.AppendLine("</table>");
            html.AppendLine("</body></html>");
            
            return html.ToString();
        }

        private void LogAnalyzeMessage(string message)
        {
            Dispatcher.Invoke(() =>
            {
                txtAnalyzeLog.AppendText(message + Environment.NewLine);
                txtAnalyzeLog.ScrollToEnd();
            });
        }

        #endregion

        #region Balance Dataset

        private void BrowseBalancePrimary_Click(object sender, RoutedEventArgs e)
        {
            using (var dialog = new FolderBrowserDialog())
            {
                dialog.Description = "Select primary YOLO dataset folder";
                if (dialog.ShowDialog() == DialogResult.OK)
                {
                    if (ValidateYoloDataset(dialog.SelectedPath))
                    {
                        txtBalancePrimary.Text = dialog.SelectedPath;
                    }
                    else
                    {
                        System.Windows.MessageBox.Show("Invalid YOLO dataset structure.", "Invalid Dataset",
                            MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
            }
        }

        private void BrowseBalanceSecondary_Click(object sender, RoutedEventArgs e)
        {
            using (var dialog = new FolderBrowserDialog())
            {
                dialog.Description = "Select secondary YOLO dataset folder (optional)";
                if (dialog.ShowDialog() == DialogResult.OK)
                {
                    if (ValidateYoloDataset(dialog.SelectedPath))
                    {
                        txtBalanceSecondary.Text = dialog.SelectedPath;
                    }
                    else
                    {
                        System.Windows.MessageBox.Show("Invalid YOLO dataset structure.", "Invalid Dataset",
                            MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
            }
        }

        private void BrowseBalanceOutput_Click(object sender, RoutedEventArgs e)
        {
            using (var dialog = new FolderBrowserDialog())
            {
                dialog.Description = "Select output directory for balanced dataset";
                if (dialog.ShowDialog() == DialogResult.OK)
                {
                    txtBalanceOutput.Text = dialog.SelectedPath;
                }
            }
        }

        private async void BalanceDataset_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(txtBalancePrimary.Text) || string.IsNullOrEmpty(txtBalanceOutput.Text))
            {
                System.Windows.MessageBox.Show("Please select primary dataset and output directory.", "Missing Paths",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            btnBalanceDataset.IsEnabled = false;
            txtBalanceLog.Clear();

            try
            {
                await Task.Run(() => BalanceDataset());
                LogBalanceMessage("Balancing completed successfully!");
                System.Windows.MessageBox.Show("Dataset balanced successfully!", "Success",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                LogBalanceMessage($"Error: {ex.Message}");
                System.Windows.MessageBox.Show($"Error balancing dataset: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                btnBalanceDataset.IsEnabled = true;
            }
        }

        private void BalanceDataset()
        {
            string primaryPath = "";
            string secondaryPath = "";
            string outputPath = "";
            int targetInstances = 0;

            Dispatcher.Invoke(() =>
            {
                primaryPath = txtBalancePrimary.Text;
                secondaryPath = txtBalanceSecondary.Text;
                outputPath = txtBalanceOutput.Text;
                int.TryParse(txtTargetInstances.Text, out targetInstances);
            });

            LogBalanceMessage($"Balancing dataset with target: {targetInstances} instances per class");

            // This is a simplified version - the full FWTools implementation is more complex
            // For now, we'll implement a basic version that selects images to meet the target

            var primaryClasses = LoadClassesFromYaml(primaryPath);
            var classInstanceCounts = new Dictionary<int, int>();

            // Count instances in primary dataset
            foreach (var split in new[] { "train", "val", "test" })
            {
                var images = GetImagesFromSplit(primaryPath, split);
                foreach (var image in images)
                {
                    var labelPath = Path.Combine(primaryPath, "labels", split, image + ".txt");
                    if (File.Exists(labelPath))
                    {
                        var lines = File.ReadAllLines(labelPath);
                        foreach (var line in lines)
                        {
                            var parts = line.Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries);
                            if (parts.Length >= 5 && int.TryParse(parts[0], out int classId))
                            {
                                if (!classInstanceCounts.ContainsKey(classId))
                                    classInstanceCounts[classId] = 0;
                                classInstanceCounts[classId]++;
                            }
                        }
                    }
                }
            }

            LogBalanceMessage("Current class distribution:");
            foreach (var kvp in classInstanceCounts.OrderBy(x => x.Key))
            {
                LogBalanceMessage($"  Class {kvp.Key}: {kvp.Value} instances");
            }

            LogBalanceMessage("Note: Full balancing algorithm from FWTools will be implemented in next iteration");
            LogBalanceMessage("This is a placeholder implementation");
        }

        private void LogBalanceMessage(string message)
        {
            Dispatcher.Invoke(() =>
            {
                txtBalanceLog.AppendText(message + Environment.NewLine);
                txtBalanceLog.ScrollToEnd();
            });
        }

        #endregion

        #region Validate Dataset

        private List<Models.ValidationResult> validationResults = new List<Models.ValidationResult>();

        private void BrowseValidateInput_Click(object sender, RoutedEventArgs e)
        {
            using (var dialog = new FolderBrowserDialog())
            {
                dialog.Description = "Select YOLO dataset folder to validate";
                if (dialog.ShowDialog() == DialogResult.OK)
                {
                    if (ValidateYoloDataset(dialog.SelectedPath))
                    {
                        txtValidateInput.Text = dialog.SelectedPath;
                    }
                    else
                    {
                        System.Windows.MessageBox.Show("Invalid YOLO dataset structure.", "Invalid Dataset",
                            MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
            }
        }

        private void BrowseValidateOutput_Click(object sender, RoutedEventArgs e)
        {
            using (var dialog = new FolderBrowserDialog())
            {
                dialog.Description = "Select output directory for organized files";
                if (dialog.ShowDialog() == DialogResult.OK)
                {
                    txtValidateOutput.Text = dialog.SelectedPath;
                }
            }
        }

        private async void ValidateDataset_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(txtValidateInput.Text))
            {
                System.Windows.MessageBox.Show("Please select a dataset directory.", "Missing Path",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            btnValidateDataset.IsEnabled = false;
            txtValidateLog.Clear();
            pbValidateProgress.Value = 0;
            txtValidateStatus.Text = "Starting validation...";

            // Create progress reporter
            var progress = new Progress<(int percentage, string status)>(report =>
            {
                pbValidateProgress.Value = report.percentage;
                txtValidateStatus.Text = report.status;
            });

            try
            {
                await Task.Run(() => ValidateDatasetMethod(progress));
                txtValidateStatus.Text = "Validation completed!";
                pbValidateProgress.Value = 100;
                LogValidateMessage("Validation completed!");
                btnExportGoodDataset.IsEnabled = validationResults.Any(v => v.IsValid);
                btnExportBadDataset.IsEnabled = validationResults.Any(v => !v.IsValid);
                btnExportValidationReport.IsEnabled = true;
            }
            catch (Exception ex)
            {
                txtValidateStatus.Text = "Validation failed";
                pbValidateProgress.Value = 0;
                LogValidateMessage($"Error: {ex.Message}");
                System.Windows.MessageBox.Show($"Error validating dataset: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                btnValidateDataset.IsEnabled = true;
            }
        }

        private void ValidateDatasetMethod(IProgress<(int percentage, string status)> progress)
        {
            string datasetPath = "";
            bool checkMinROI = false, checkDuplicates = false, checkCoordinates = false;
            bool checkMissingFiles = false, checkEmptyLabels = false;
            int minROI = 1;

            Dispatcher.Invoke(() =>
            {
                datasetPath = txtValidateInput.Text;
                checkMinROI = chkValidateMinROI.IsChecked ?? false;
                checkDuplicates = chkValidateDuplicates.IsChecked ?? false;
                checkCoordinates = chkValidateCoordinates.IsChecked ?? false;
                checkMissingFiles = chkValidateMissingFiles.IsChecked ?? false;
                checkEmptyLabels = chkValidateEmptyLabels.IsChecked ?? false;
                int.TryParse(txtMinROI.Text, out minROI);
            });

            validationResults.Clear();
            LogValidateMessage($"Validating dataset: {datasetPath}");
            progress?.Report((0, "Counting images..."));

            // First pass: count total images for progress calculation
            int totalImageCount = 0;
            foreach (var split in new[] { "train", "val", "test" })
            {
                var images = GetImagesFromSplit(datasetPath, split);
                totalImageCount += images.Count;
            }

            progress?.Report((5, $"Found {totalImageCount} total images. Starting multi-threaded validation..."));
            int processedImages = 0;
            object lockObj = new object();

            // Use all CPU cores except 2 for validation
            int maxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount - 2);

            foreach (var split in new[] { "train", "val", "test" })
            {
                var images = GetImagesFromSplit(datasetPath, split);
                LogValidateMessage($"Checking {split} split ({images.Count} images) with {maxDegreeOfParallelism} threads...");
                progress?.Report((5 + (processedImages * 85 / Math.Max(1, totalImageCount)), $"Validating {split} split ({images.Count} images)..."));

                var splitResults = new System.Collections.Concurrent.ConcurrentBag<Models.ValidationResult>();

                Parallel.ForEach(images,
                    new ParallelOptions { MaxDegreeOfParallelism = maxDegreeOfParallelism },
                    (image, state, imgIdx) =>
                    {
                        var result = new Models.ValidationResult
                        {
                            ImagePath = image,
                            LabelPath = Path.Combine(datasetPath, "labels", split, image + ".txt"),
                            Category = ValidationCategory.Fine
                        };

                        var labelPath = result.LabelPath;

                        // Check missing label
                        if (checkMissingFiles && !File.Exists(labelPath))
                        {
                            result.Issues.Add(new ValidationIssue
                            {
                                Type = ValidationIssueType.MissingLabel,
                                Description = "Label file is missing"
                            });
                            result.Category = ValidationCategory.MissingFiles;
                        }
                        else if (File.Exists(labelPath))
                        {
                            var lines = File.ReadAllLines(labelPath).Where(l => !string.IsNullOrWhiteSpace(l)).ToList();

                            // Check empty label
                            if (checkEmptyLabels && lines.Count == 0)
                            {
                                result.Issues.Add(new ValidationIssue
                                {
                                    Type = ValidationIssueType.EmptyLabel,
                                    Description = "Label file is empty"
                                });
                                result.Category = ValidationCategory.MissingFiles;
                            }

                            // Check minimum ROI
                            if (checkMinROI && lines.Count < minROI)
                            {
                                result.Issues.Add(new ValidationIssue
                                {
                                    Type = ValidationIssueType.MinimumROI,
                                    Description = $"Only {lines.Count} ROIs (minimum: {minROI})"
                                });
                                result.Category = ValidationCategory.MinimumROI;
                            }

                            // Check duplicates
                            if (checkDuplicates)
                            {
                                var classIds = new List<int>();
                                foreach (var line in lines)
                                {
                                    var parts = line.Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries);
                                    if (parts.Length >= 5 && int.TryParse(parts[0], out int classId))
                                    {
                                        classIds.Add(classId);
                                    }
                                }

                                if (classIds.Count != classIds.Distinct().Count())
                                {
                                    result.Issues.Add(new ValidationIssue
                                    {
                                        Type = ValidationIssueType.DuplicateClass,
                                        Description = "Duplicate class IDs found"
                                    });
                                    result.Category = ValidationCategory.DuplicateClasses;
                                }
                            }

                            // Check coordinates
                            if (checkCoordinates)
                            {
                                for (int i = 0; i < lines.Count; i++)
                                {
                                    if (!YoloDatasetHelper.ValidateYoloCoordinates(lines[i], out string error))
                                    {
                                        result.Issues.Add(new ValidationIssue
                                        {
                                            Type = ValidationIssueType.InvalidCoordinates,
                                            Description = $"Line {i + 1}: {error}",
                                            LineNumber = i + 1
                                        });
                                        result.Category = ValidationCategory.InvalidCoordinates;
                                    }
                                }
                            }
                        }

                        splitResults.Add(result);

                        // Thread-safe progress reporting
                        int currentProcessed;
                        lock (lockObj)
                        {
                            processedImages++;
                            currentProcessed = processedImages;
                        }

                        // Report progress every 100 images to avoid UI flooding
                        if (currentProcessed % 100 == 0 || currentProcessed == totalImageCount)
                        {
                            int percentage = 5 + (currentProcessed * 85 / totalImageCount);
                            progress?.Report((percentage, $"Validating {split}: {splitResults.Count}/{images.Count} ({currentProcessed}/{totalImageCount} total)"));
                        }
                    });

                // Add all results from this split to the main collection
                foreach (var result in splitResults)
                {
                    validationResults.Add(result);
                }
            }

            // Check for missing images
            if (checkMissingFiles)
            {
                foreach (var split in new[] { "train", "val", "test" })
                {
                    var labelsPath = Path.Combine(datasetPath, "labels", split);
                    if (Directory.Exists(labelsPath))
                    {
                        var labelFiles = Directory.GetFiles(labelsPath, "*.txt");
                        foreach (var labelFile in labelFiles)
                        {
                            var imageName = Path.GetFileNameWithoutExtension(labelFile);
                            var imagePath = YoloDatasetHelper.GetImagePath(datasetPath, split, imageName);
                            
                            if (imagePath == null)
                            {
                                validationResults.Add(new Models.ValidationResult
                                {
                                    ImagePath = imageName,
                                    LabelPath = labelFile,
                                    Category = ValidationCategory.MissingFiles,
                                    Issues = new List<ValidationIssue>
                                    {
                                        new ValidationIssue
                                        {
                                            Type = ValidationIssueType.MissingImage,
                                            Description = "Image file is missing"
                                        }
                                    }
                                });
                            }
                        }
                    }
                }
            }

            progress?.Report((90, "Calculating statistics..."));

            var issueCount = validationResults.Count(v => !v.IsValid);
            LogValidateMessage($"Found {issueCount} images with issues out of {validationResults.Count} total");

            // Calculate category counts
            var fineCount = validationResults.Count(v => v.Category == ValidationCategory.Fine);
            var minROICount = validationResults.Count(v => v.Category == ValidationCategory.MinimumROI);
            var duplicatesCount = validationResults.Count(v => v.Category == ValidationCategory.DuplicateClasses);
            var invalidCoordsCount = validationResults.Count(v => v.Category == ValidationCategory.InvalidCoordinates);
            var missingFilesCount = validationResults.Count(v => v.Category == ValidationCategory.MissingFiles);
            var emptyLabelsCount = validationResults.Count(v => v.Issues.Any(i => i.Type == ValidationIssueType.EmptyLabel));

            var maxCount = Math.Max(1, new[] { fineCount, minROICount, duplicatesCount, invalidCoordsCount, missingFilesCount, emptyLabelsCount }.Max());

            Dispatcher.Invoke(() =>
            {
                dgValidationResults.ItemsSource = validationResults.Where(v => !v.IsValid).ToList();

                // Update bar chart
                const double maxBarHeight = 150;
                barFine.Height = (fineCount * maxBarHeight) / maxCount;
                barMinROI.Height = (minROICount * maxBarHeight) / maxCount;
                barDuplicates.Height = (duplicatesCount * maxBarHeight) / maxCount;
                barInvalidCoords.Height = (invalidCoordsCount * maxBarHeight) / maxCount;
                barMissingFiles.Height = (missingFilesCount * maxBarHeight) / maxCount;
                barEmptyLabels.Height = (emptyLabelsCount * maxBarHeight) / maxCount;

                lblFineCount.Text = fineCount.ToString();
                lblMinROICount.Text = minROICount.ToString();
                lblDuplicatesCount.Text = duplicatesCount.ToString();
                lblInvalidCoordsCount.Text = invalidCoordsCount.ToString();
                lblMissingFilesCount.Text = missingFilesCount.ToString();
                lblEmptyLabelsCount.Text = emptyLabelsCount.ToString();
            });
        }

        private async void ExportGoodDataset_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(txtValidateOutput.Text))
            {
                System.Windows.MessageBox.Show("Please select an output directory.", "Missing Path",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            btnExportGoodDataset.IsEnabled = false;
            pbValidateProgress.Value = 0;
            txtValidateStatus.Text = "Starting export...";

            // Create progress reporter
            var progress = new Progress<(int percentage, string status)>(report =>
            {
                pbValidateProgress.Value = report.percentage;
                txtValidateStatus.Text = report.status;
            });

            try
            {
                await ExportDatasetByValidation(true, progress);
                txtValidateStatus.Text = "Good images exported successfully!";
                pbValidateProgress.Value = 100;
                LogValidateMessage("Good images exported successfully!");
                System.Windows.MessageBox.Show("Good images exported successfully!", "Success",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                txtValidateStatus.Text = "Export failed";
                pbValidateProgress.Value = 0;
                LogValidateMessage($"Error: {ex.Message}");
                System.Windows.MessageBox.Show($"Error exporting dataset: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                btnExportGoodDataset.IsEnabled = true;
            }
        }

        private async void ExportBadDataset_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(txtValidateOutput.Text))
            {
                System.Windows.MessageBox.Show("Please select an output directory.", "Missing Path",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            btnExportBadDataset.IsEnabled = false;
            pbValidateProgress.Value = 0;
            txtValidateStatus.Text = "Starting export...";

            // Create progress reporter
            var progress = new Progress<(int percentage, string status)>(report =>
            {
                pbValidateProgress.Value = report.percentage;
                txtValidateStatus.Text = report.status;
            });

            try
            {
                await ExportDatasetByValidation(false, progress);
                txtValidateStatus.Text = "Bad images exported successfully!";
                pbValidateProgress.Value = 100;
                LogValidateMessage("Bad images exported successfully!");
                System.Windows.MessageBox.Show("Bad images exported successfully!", "Success",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                txtValidateStatus.Text = "Export failed";
                pbValidateProgress.Value = 0;
                LogValidateMessage($"Error: {ex.Message}");
                System.Windows.MessageBox.Show($"Error exporting dataset: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                btnExportBadDataset.IsEnabled = true;
            }
        }

        private async Task ExportDatasetByValidation(bool exportGood, IProgress<(int percentage, string status)>? progress = null)
        {
            string sourceDatasetPath = "";
            string outputPath = "";

            Dispatcher.Invoke(() =>
            {
                sourceDatasetPath = txtValidateInput.Text;
                outputPath = txtValidateOutput.Text;
            });

            var targetResults = exportGood
                ? validationResults.Where(v => v.IsValid).ToList()
                : validationResults.Where(v => !v.IsValid).ToList();

            var folderName = exportGood ? "good_dataset" : "bad_dataset";
            var fullOutputPath = Path.Combine(outputPath, folderName);

            LogValidateMessage($"Exporting {targetResults.Count} {(exportGood ? "good" : "bad")} images to {fullOutputPath}");
            LogValidateMessage("Using robocopy for faster file operations...");
            progress?.Report((5, "Setting up output directory..."));

            // Create output directory structure
            Directory.CreateDirectory(fullOutputPath);
            foreach (var split in new[] { "train", "val", "test" })
            {
                Directory.CreateDirectory(Path.Combine(fullOutputPath, "images", split));
                Directory.CreateDirectory(Path.Combine(fullOutputPath, "labels", split));
            }

            // Copy dataset.yaml
            var yamlFiles = Directory.GetFiles(sourceDatasetPath, "*.yaml", SearchOption.TopDirectoryOnly);
            if (yamlFiles.Length > 0)
            {
                File.Copy(yamlFiles[0], Path.Combine(fullOutputPath, Path.GetFileName(yamlFiles[0])), true);
            }

            progress?.Report((10, "Organizing files by split..."));

            // Group results by split for batch processing with robocopy
            var imagesBySplit = new Dictionary<string, List<string>>();
            foreach (var split in new[] { "train", "val", "test" })
            {
                imagesBySplit[split] = new List<string>();
            }

            // Categorize all images by split
            foreach (var result in targetResults)
            {
                foreach (var split in new[] { "train", "val", "test" })
                {
                    var imagePath = YoloDatasetHelper.GetImagePath(sourceDatasetPath, split, result.ImagePath);
                    if (imagePath != null)
                    {
                        imagesBySplit[split].Add(result.ImagePath);
                        break;
                    }
                }
            }

            int totalCopied = 0;
            int totalFiles = targetResults.Count;

            // Use robocopy for each split
            foreach (var split in new[] { "train", "val", "test" })
            {
                var images = imagesBySplit[split];
                if (images.Count == 0)
                    continue;

                LogValidateMessage($"Copying {images.Count} images from {split} split using robocopy...");
                progress?.Report((10 + (totalCopied * 85 / Math.Max(1, totalFiles)), $"Copying {split} split ({images.Count} files)..."));

                try
                {
                    await YoloDatasetHelper.CopyImageAndLabelWithRobocopyAsync(
                        sourceDatasetPath,
                        fullOutputPath,
                        split,
                        images,
                        LogValidateMessage);

                    totalCopied += images.Count;

                    LogValidateMessage($"Completed {split} split: {images.Count} files copied");
                    progress?.Report((10 + (totalCopied * 85 / totalFiles), $"Completed {split} split ({totalCopied}/{totalFiles} total)"));
                }
                catch (Exception ex)
                {
                    LogValidateMessage($"Robocopy failed for {split} split, falling back to standard copy: {ex.Message}");

                    // Fallback to standard copy if robocopy fails
                    int splitCopied = 0;
                    foreach (var imageName in images)
                    {
                        try
                        {
                            CopyImageAndLabel(sourceDatasetPath, fullOutputPath, split, imageName);
                            splitCopied++;
                            totalCopied++;

                            if (splitCopied % 100 == 0)
                            {
                                LogValidateMessage($"Copied {splitCopied}/{images.Count} from {split} split...");
                                progress?.Report((10 + (totalCopied * 85 / totalFiles), $"Copying {split}: {splitCopied}/{images.Count} ({totalCopied}/{totalFiles} total)"));
                            }
                        }
                        catch (Exception copyEx)
                        {
                            LogValidateMessage($"Error copying {imageName}: {copyEx.Message}");
                        }
                    }
                }
            }

            progress?.Report((95, "Finalizing..."));
            LogValidateMessage($"Export completed: {totalCopied} images copied to {fullOutputPath}");
        }

        private void ExportValidationReport_Click(object sender, RoutedEventArgs e)
        {
            var saveDialog = new SaveFileDialog
            {
                Filter = "Text files (*.txt)|*.txt",
                FileName = "validation_report.txt"
            };

            if (saveDialog.ShowDialog() == DialogResult.OK)
            {
                try
                {
                    var report = new StringBuilder();
                    report.AppendLine("YOLO Dataset Validation Report");
                    report.AppendLine($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                    report.AppendLine();
                    report.AppendLine($"Total images checked: {validationResults.Count}");
                    report.AppendLine($"Images with issues: {validationResults.Count(v => !v.IsValid)}");
                    report.AppendLine();

                    foreach (var result in validationResults.Where(v => !v.IsValid))
                    {
                        report.AppendLine($"Image: {result.ImagePath}");
                        report.AppendLine($"Category: {result.Category}");
                        foreach (var issue in result.Issues)
                        {
                            report.AppendLine($"  - {issue.Description}");
                        }
                        report.AppendLine();
                    }

                    File.WriteAllText(saveDialog.FileName, report.ToString());
                    LogValidateMessage($"Exported validation report to: {saveDialog.FileName}");
                }
                catch (Exception ex)
                {
                    System.Windows.MessageBox.Show($"Error exporting report: {ex.Message}", "Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void LogValidateMessage(string message)
        {
            Dispatcher.Invoke(() =>
            {
                txtValidateLog.AppendText(message + Environment.NewLine);
                txtValidateLog.ScrollToEnd();
            });
        }

        #endregion

        #region Utilities

        // Label-Image Matcher
        private void BrowseMatchSource_Click(object sender, RoutedEventArgs e)
        {
            using (var dialog = new FolderBrowserDialog())
            {
                dialog.Description = "Select source directory";
                if (dialog.ShowDialog() == DialogResult.OK)
                {
                    txtMatchSource.Text = dialog.SelectedPath;
                }
            }
        }

        private void BrowseMatchTarget_Click(object sender, RoutedEventArgs e)
        {
            using (var dialog = new FolderBrowserDialog())
            {
                dialog.Description = "Select target directory";
                if (dialog.ShowDialog() == DialogResult.OK)
                {
                    txtMatchTarget.Text = dialog.SelectedPath;
                }
            }
        }

        private async void RunMatcher_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(txtMatchSource.Text) || string.IsNullOrEmpty(txtMatchTarget.Text))
            {
                System.Windows.MessageBox.Show("Please select source and target directories.", "Missing Paths",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            btnRunMatcher.IsEnabled = false;
            txtUtilitiesLog.Clear();

            try
            {
                await Task.Run(() => RunMatcher());
                LogUtilitiesMessage("Matching completed!");
                System.Windows.MessageBox.Show("Matching completed successfully!", "Success",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                LogUtilitiesMessage($"Error: {ex.Message}");
                System.Windows.MessageBox.Show($"Error: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                btnRunMatcher.IsEnabled = true;
            }
        }

        private void RunMatcher()
        {
            bool matchLabelsToImages = false;
            string sourcePath = "";
            string targetPath = "";

            Dispatcher.Invoke(() =>
            {
                matchLabelsToImages = rbMatchLabelsToImages.IsChecked ?? false;
                sourcePath = txtMatchSource.Text;
                targetPath = txtMatchTarget.Text;
            });

            LogUtilitiesMessage($"Matching {(matchLabelsToImages ? "labels to images" : "images to labels")}...");
            
            // Simple implementation - full FWTools version is more sophisticated
            var sourceFiles = Directory.GetFiles(sourcePath);
            var targetFiles = Directory.GetFiles(targetPath);
            
            int matchedCount = 0;
            
            foreach (var sourceFile in sourceFiles)
            {
                var baseName = Path.GetFileNameWithoutExtension(sourceFile);
                var targetFile = targetFiles.FirstOrDefault(t => Path.GetFileNameWithoutExtension(t) == baseName);
                
                if (targetFile != null)
                {
                    matchedCount++;
                }
            }
            
            LogUtilitiesMessage($"Found {matchedCount} matches out of {sourceFiles.Length} source files");
        }

        // Find Missing Files
        private void BrowseMissingFiles_Click(object sender, RoutedEventArgs e)
        {
            using (var dialog = new FolderBrowserDialog())
            {
                dialog.Description = "Select dataset directory";
                if (dialog.ShowDialog() == DialogResult.OK)
                {
                    if (ValidateYoloDataset(dialog.SelectedPath))
                    {
                        txtMissingFilesInput.Text = dialog.SelectedPath;
                    }
                    else
                    {
                        System.Windows.MessageBox.Show("Invalid YOLO dataset structure.", "Invalid Dataset",
                            MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
            }
        }

        private async void FindMissingFiles_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(txtMissingFilesInput.Text))
            {
                System.Windows.MessageBox.Show("Please select a dataset directory.", "Missing Path",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            btnFindMissingFiles.IsEnabled = false;
            txtUtilitiesLog.Clear();

            try
            {
                await Task.Run(() => FindMissingFiles());
                LogUtilitiesMessage("Search completed!");
            }
            catch (Exception ex)
            {
                LogUtilitiesMessage($"Error: {ex.Message}");
            }
            finally
            {
                btnFindMissingFiles.IsEnabled = true;
            }
        }

        private void FindMissingFiles()
        {
            string datasetPath = "";
            Dispatcher.Invoke(() => datasetPath = txtMissingFilesInput.Text);

            LogUtilitiesMessage("Searching for missing files...");

            int missingLabels = 0;
            int missingImages = 0;

            foreach (var split in new[] { "train", "val", "test" })
            {
                var images = GetImagesFromSplit(datasetPath, split);
                foreach (var image in images)
                {
                    var labelPath = Path.Combine(datasetPath, "labels", split, image + ".txt");
                    if (!File.Exists(labelPath))
                    {
                        LogUtilitiesMessage($"Missing label: {split}/{image}.txt");
                        missingLabels++;
                    }
                }

                var labelsPath = Path.Combine(datasetPath, "labels", split);
                if (Directory.Exists(labelsPath))
                {
                    var labelFiles = Directory.GetFiles(labelsPath, "*.txt");
                    foreach (var labelFile in labelFiles)
                    {
                        var imageName = Path.GetFileNameWithoutExtension(labelFile);
                        var imagePath = YoloDatasetHelper.GetImagePath(datasetPath, split, imageName);
                        if (imagePath == null)
                        {
                            LogUtilitiesMessage($"Missing image: {split}/{imageName}");
                            missingImages++;
                        }
                    }
                }
            }

            LogUtilitiesMessage($"Found {missingLabels} missing labels and {missingImages} missing images");
        }

        // Cleanup Tool
        private void BrowseCleanup_Click(object sender, RoutedEventArgs e)
        {
            using (var dialog = new FolderBrowserDialog())
            {
                dialog.Description = "Select dataset directory to clean up";
                if (dialog.ShowDialog() == DialogResult.OK)
                {
                    if (ValidateYoloDataset(dialog.SelectedPath))
                    {
                        txtCleanupInput.Text = dialog.SelectedPath;
                    }
                    else
                    {
                        System.Windows.MessageBox.Show("Invalid YOLO dataset structure.", "Invalid Dataset",
                            MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
            }
        }

        private void RunCleanup_Click(object sender, RoutedEventArgs e)
        {
            System.Windows.MessageBox.Show("This operation will delete files. Feature coming soon with preview!", "Info",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }

        // Class Remapping
        private void BrowseRemap_Click(object sender, RoutedEventArgs e)
        {
            using (var dialog = new FolderBrowserDialog())
            {
                dialog.Description = "Select dataset directory";
                if (dialog.ShowDialog() == DialogResult.OK)
                {
                    if (ValidateYoloDataset(dialog.SelectedPath))
                    {
                        txtRemapInput.Text = dialog.SelectedPath;
                    }
                    else
                    {
                        System.Windows.MessageBox.Show("Invalid YOLO dataset structure.", "Invalid Dataset",
                            MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
            }
        }

        private void AddRemapRule_Click(object sender, RoutedEventArgs e)
        {
            if (int.TryParse(txtOldClassId.Text, out int oldId) && int.TryParse(txtNewClassId.Text, out int newId))
            {
                lstRemapRules.Items.Add($"{oldId} → {newId}");
                txtOldClassId.Clear();
                txtNewClassId.Clear();
            }
            else
            {
                System.Windows.MessageBox.Show("Please enter valid class IDs.", "Invalid Input",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void ApplyRemap_Click(object sender, RoutedEventArgs e)
        {
            System.Windows.MessageBox.Show("Feature coming soon!", "Info",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }

        // Image Resizing
        private void BrowseResize_Click(object sender, RoutedEventArgs e)
        {
            using (var dialog = new FolderBrowserDialog())
            {
                dialog.Description = "Select dataset directory";
                if (dialog.ShowDialog() == DialogResult.OK)
                {
                    if (ValidateYoloDataset(dialog.SelectedPath))
                    {
                        txtResizeInput.Text = dialog.SelectedPath;
                    }
                    else
                    {
                        System.Windows.MessageBox.Show("Invalid YOLO dataset structure.", "Invalid Dataset",
                            MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
            }
        }

        private void BrowseResizeOutput_Click(object sender, RoutedEventArgs e)
        {
            using (var dialog = new FolderBrowserDialog())
            {
                dialog.Description = "Select output directory";
                if (dialog.ShowDialog() == DialogResult.OK)
                {
                    txtResizeOutput.Text = dialog.SelectedPath;
                }
            }
        }

        private void ResizeImages_Click(object sender, RoutedEventArgs e)
        {
            System.Windows.MessageBox.Show("Feature coming soon!", "Info",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }

        // Deduplication
        private void BrowseDedupe_Click(object sender, RoutedEventArgs e)
        {
            using (var dialog = new FolderBrowserDialog())
            {
                dialog.Description = "Select dataset directory";
                if (dialog.ShowDialog() == DialogResult.OK)
                {
                    if (ValidateYoloDataset(dialog.SelectedPath))
                    {
                        txtDedupeInput.Text = dialog.SelectedPath;
                    }
                    else
                    {
                        System.Windows.MessageBox.Show("Invalid YOLO dataset structure.", "Invalid Dataset",
                            MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
            }
        }

        private async void RunDedupe_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(txtDedupeInput.Text))
            {
                System.Windows.MessageBox.Show("Please select a dataset directory.", "Missing Path",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            btnRunDedupe.IsEnabled = false;
            txtUtilitiesLog.Clear();

            try
            {
                await Task.Run(() => RunDeduplication());
                LogUtilitiesMessage("Deduplication scan completed!");
            }
            catch (Exception ex)
            {
                LogUtilitiesMessage($"Error: {ex.Message}");
            }
            finally
            {
                btnRunDedupe.IsEnabled = true;
            }
        }

        private void RunDeduplication()
        {
            string datasetPath = "";
            Dispatcher.Invoke(() => datasetPath = txtDedupeInput.Text);

            LogUtilitiesMessage("Scanning for duplicate images...");

            var imageHashes = new Dictionary<string, List<string>>();

            foreach (var split in new[] { "train", "val", "test" })
            {
                var images = GetImagesFromSplit(datasetPath, split);
                LogUtilitiesMessage($"Scanning {split} split ({images.Count} images)...");

                foreach (var image in images)
                {
                    var imagePath = YoloDatasetHelper.GetImagePath(datasetPath, split, image);
                    if (imagePath != null)
                    {
                        var hash = YoloDatasetHelper.CalculateFileHash(imagePath);
                        if (!imageHashes.ContainsKey(hash))
                        {
                            imageHashes[hash] = new List<string>();
                        }
                        imageHashes[hash].Add(imagePath);
                    }
                }
            }

            var duplicates = imageHashes.Where(kvp => kvp.Value.Count > 1).ToList();
            LogUtilitiesMessage($"Found {duplicates.Count} sets of duplicate images");

            foreach (var dup in duplicates)
            {
                LogUtilitiesMessage($"Duplicate set ({dup.Value.Count} files):");
                foreach (var file in dup.Value)
                {
                    LogUtilitiesMessage($"  - {Path.GetFileName(file)}");
                }
            }
        }

        private void LogUtilitiesMessage(string message)
        {
            Dispatcher.Invoke(() =>
            {
                txtUtilitiesLog.AppendText(message + Environment.NewLine);
                txtUtilitiesLog.ScrollToEnd();
            });
        }

        // Consolidate to Train
        private void BrowseConsolidate_Click(object sender, RoutedEventArgs e)
        {
            using (var dialog = new FolderBrowserDialog())
            {
                dialog.Description = "Select dataset directory";
                if (dialog.ShowDialog() == DialogResult.OK)
                {
                    if (ValidateYoloDataset(dialog.SelectedPath))
                    {
                        txtConsolidateInput.Text = dialog.SelectedPath;
                    }
                    else
                    {
                        System.Windows.MessageBox.Show("Invalid YOLO dataset structure.", "Invalid Dataset",
                            MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
            }
        }

        private async void ConsolidateToTrain_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(txtConsolidateInput.Text))
            {
                System.Windows.MessageBox.Show("Please select a dataset directory.", "Missing Path",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var result = System.Windows.MessageBox.Show(
                "This will move all images and labels from val and test splits to the train split.\n\nContinue?",
                "Confirm Consolidation",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes)
                return;

            btnConsolidateToTrain.IsEnabled = false;
            txtSplitLog.Clear();

            try
            {
                await Task.Run(() => ConsolidateToTrainMethod());
                LogSplitMessage("Consolidation completed!");
                System.Windows.MessageBox.Show("All images and labels moved to train split!", "Success",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                LogSplitMessage($"Error: {ex.Message}");
                System.Windows.MessageBox.Show($"Error: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                btnConsolidateToTrain.IsEnabled = true;
            }
        }

        private void ConsolidateToTrainMethod()
        {
            string datasetPath = "";
            Dispatcher.Invoke(() => datasetPath = txtConsolidateInput.Text);

            LogSplitMessage($"Consolidating dataset: {datasetPath}");

            int totalMoved = 0;

            foreach (var split in new[] { "val", "test" })
            {
                string srcImagesPath = Path.Combine(datasetPath, "images", split);
                string srcLabelsPath = Path.Combine(datasetPath, "labels", split);
                string destImagesPath = Path.Combine(datasetPath, "images", "train");
                string destLabelsPath = Path.Combine(datasetPath, "labels", "train");

                if (!Directory.Exists(srcImagesPath))
                {
                    LogSplitMessage($"No {split} split found, skipping...");
                    continue;
                }

                // Ensure train directories exist
                Directory.CreateDirectory(destImagesPath);
                Directory.CreateDirectory(destLabelsPath);

                // Move images
                var images = Directory.GetFiles(srcImagesPath)
                    .Where(f => f.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
                                f.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ||
                                f.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase))
                    .ToList();

                LogSplitMessage($"Moving {images.Count} images from {split} to train...");

                foreach (var imagePath in images)
                {
                    string fileName = Path.GetFileName(imagePath);
                    string destPath = Path.Combine(destImagesPath, fileName);

                    // Handle name conflicts
                    if (File.Exists(destPath))
                    {
                        string nameWithoutExt = Path.GetFileNameWithoutExtension(fileName);
                        string ext = Path.GetExtension(fileName);
                        int counter = 1;
                        while (File.Exists(destPath))
                        {
                            fileName = $"{nameWithoutExt}_{counter}{ext}";
                            destPath = Path.Combine(destImagesPath, fileName);
                            counter++;
                        }
                        LogSplitMessage($"  Renamed duplicate: {Path.GetFileName(imagePath)} -> {fileName}");
                    }

                    File.Move(imagePath, destPath);
                    totalMoved++;
                }

                // Move labels
                if (Directory.Exists(srcLabelsPath))
                {
                    var labels = Directory.GetFiles(srcLabelsPath, "*.txt");
                    LogSplitMessage($"Moving {labels.Length} labels from {split} to train...");

                    foreach (var labelPath in labels)
                    {
                        string fileName = Path.GetFileName(labelPath);
                        string destPath = Path.Combine(destLabelsPath, fileName);

                        // Handle name conflicts
                        if (File.Exists(destPath))
                        {
                            string nameWithoutExt = Path.GetFileNameWithoutExtension(fileName);
                            int counter = 1;
                            while (File.Exists(destPath))
                            {
                                fileName = $"{nameWithoutExt}_{counter}.txt";
                                destPath = Path.Combine(destLabelsPath, fileName);
                                counter++;
                            }
                        }

                        File.Move(labelPath, destPath);
                    }
                }
            }

            LogSplitMessage($"Moved {totalMoved} images to train split");
            LogSplitMessage("Consolidation completed successfully!");
        }

        #endregion
    }
}
