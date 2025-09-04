using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms; // For FolderBrowserDialog and SaveFileDialog
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using YoloAnnotationEditor.Models;

namespace YoloAnnotationEditor
{
    public partial class DatasetToolsControl : System.Windows.Controls.UserControl
    {
        private List<string> selectedDatasets = new List<string>();
        private Random random = new Random();

        public DatasetToolsControl()
        {
            InitializeComponent();
        }

        #region Convert TRDG to PaddleOCR

        private void BrowseTrdgDir_Click(object sender, RoutedEventArgs e)
        {
            using (var dialog = new FolderBrowserDialog())
            {
                dialog.Description = "Select TRDG Dataset Directory";
                if (dialog.ShowDialog() == DialogResult.OK)
                {
                    txtTrdgDir.Text = dialog.SelectedPath;
                }
            }
        }

        private void BrowseConvertOutput_Click(object sender, RoutedEventArgs e)
        {
            using (var dialog = new FolderBrowserDialog())
            {
                dialog.Description = "Select Output Directory for PaddleOCR Format";
                if (dialog.ShowDialog() == DialogResult.OK)
                {
                    txtConvertOutput.Text = dialog.SelectedPath;
                }
            }
        }

        private async void ConvertDataset_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(txtTrdgDir.Text) || string.IsNullOrEmpty(txtConvertOutput.Text))
            {
                System.Windows.MessageBox.Show("Please select both input and output directories.");
                return;
            }

            btnConvert.IsEnabled = false;
            txtConvertLog.Clear();

            try
            {
                await Task.Run(() => ConvertTrdgToPaddleOcr(txtTrdgDir.Text, txtConvertOutput.Text, sliderSplitRatio.Value));
                LogMessage(txtConvertLog, "Conversion completed successfully!");
            }
            catch (Exception ex)
            {
                LogMessage(txtConvertLog, $"Error: {ex.Message}");
            }
            finally
            {
                btnConvert.IsEnabled = true;
            }
        }

        private void ConvertTrdgToPaddleOcr(string trdgDir, string outputDir, double splitRatio)
        {
            var labelsFile = Path.Combine(trdgDir, "labels.txt");
            if (!File.Exists(labelsFile))
            {
                throw new Exception($"labels.txt not found in {trdgDir}");
            }

            // Create output directories
            var trainImgDir = Path.Combine(outputDir, "train_images");
            var valImgDir = Path.Combine(outputDir, "val_images");
            Directory.CreateDirectory(trainImgDir);
            Directory.CreateDirectory(valImgDir);

            LogMessage(txtConvertLog, $"Reading labels from {labelsFile}...");

            // Read and parse labels
            var dataEntries = new List<(string filename, string label, string imagePath)>();
            var lines = File.ReadAllLines(labelsFile, Encoding.UTF8);

            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;

                var parts = line.Trim().Split(new[] { ' ' }, 2);
                if (parts.Length == 2)
                {
                    var filename = parts[0];
                    var label = parts[1];
                    var imagePath = Path.Combine(trdgDir, filename);

                    if (File.Exists(imagePath))
                    {
                        dataEntries.Add((filename, label, imagePath));
                    }
                }
            }

            LogMessage(txtConvertLog, $"Found {dataEntries.Count} valid entries");

            // Shuffle and split data
            var shuffled = dataEntries.OrderBy(x => Guid.NewGuid()).ToList();
            var splitIndex = (int)(shuffled.Count * splitRatio);
            var trainData = shuffled.Take(splitIndex).ToList();
            var valData = shuffled.Skip(splitIndex).ToList();

            LogMessage(txtConvertLog, $"Train samples: {trainData.Count}");
            LogMessage(txtConvertLog, $"Validation samples: {valData.Count}");

            // Process training data
            var trainLabels = new List<string>();
            for (int i = 0; i < trainData.Count; i++)
            {
                var entry = trainData[i];
                var newFilename = $"train_{i:D6}.jpg";
                var newImagePath = Path.Combine(trainImgDir, newFilename);

                File.Copy(entry.imagePath, newImagePath, true);
                trainLabels.Add($"train_images/{newFilename}\t{entry.label}");
            }

            // Process validation data
            var valLabels = new List<string>();
            for (int i = 0; i < valData.Count; i++)
            {
                var entry = valData[i];
                var newFilename = $"val_{i:D6}.jpg";
                var newImagePath = Path.Combine(valImgDir, newFilename);

                File.Copy(entry.imagePath, newImagePath, true);
                valLabels.Add($"val_images/{newFilename}\t{entry.label}");
            }

            // Write label files
            File.WriteAllLines(Path.Combine(outputDir, "rec_gt_train.txt"), trainLabels, Encoding.UTF8);
            File.WriteAllLines(Path.Combine(outputDir, "rec_gt_val.txt"), valLabels, Encoding.UTF8);

            LogMessage(txtConvertLog, $"Label files created in {outputDir}");
        }

        #endregion

        #region Merge Datasets

        private void BrowseMergeOutput_Click(object sender, RoutedEventArgs e)
        {
            using (var dialog = new FolderBrowserDialog())
            {
                dialog.Description = "Select Output Directory for Merged Dataset";
                if (dialog.ShowDialog() == DialogResult.OK)
                {
                    txtMergeOutput.Text = dialog.SelectedPath;
                }
            }
        }

        private void AutoDiscoverDatasets_Click(object sender, RoutedEventArgs e)
        {
            using (var dialog = new FolderBrowserDialog())
            {
                dialog.Description = "Select Root Directory to Auto-Discover Datasets";
                if (dialog.ShowDialog() == DialogResult.OK)
                {
                    selectedDatasets.Clear();
                    AutoDiscoverDatasets(dialog.SelectedPath);
                    UpdateDatasetsList();
                }
            }
        }

        private void AddDatasetFolder_Click(object sender, RoutedEventArgs e)
        {
            using (var dialog = new FolderBrowserDialog())
            {
                dialog.Description = "Select Dataset Folder";
                if (dialog.ShowDialog() == DialogResult.OK)
                {
                    if (!selectedDatasets.Contains(dialog.SelectedPath))
                    {
                        selectedDatasets.Add(dialog.SelectedPath);
                        UpdateDatasetsList();
                    }
                }
            }
        }

        private void RemoveDataset_Click(object sender, RoutedEventArgs e)
        {
            if (lstDatasets.SelectedItem != null)
            {
                var selected = lstDatasets.SelectedItem.ToString();
                selectedDatasets.Remove(selected);
                UpdateDatasetsList();
            }
        }

        private void ClearDatasets_Click(object sender, RoutedEventArgs e)
        {
            selectedDatasets.Clear();
            UpdateDatasetsList();
        }

        private void AutoDiscoverDatasets(string rootDir, int maxDepth = 3)
        {
            try
            {
                FindDatasetFolders(rootDir, 0, maxDepth);
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Error during auto-discovery: {ex.Message}");
            }
        }

        private void FindDatasetFolders(string path, int currentDepth, int maxDepth)
        {
            if (currentDepth > maxDepth) return;

            try
            {
                // Check if current folder contains labels.txt and images
                var labelsFile = Path.Combine(path, "labels.txt");
                if (File.Exists(labelsFile))
                {
                    var imageExtensions = new[] { ".jpg", ".jpeg", ".png", ".bmp", ".tiff" };
                    var hasImages = Directory.GetFiles(path)
                        .Any(f => imageExtensions.Contains(Path.GetExtension(f).ToLower()));

                    if (hasImages)
                    {
                        selectedDatasets.Add(path);
                    }
                }

                // Recursively check subdirectories
                var subdirs = Directory.GetDirectories(path)
                    .Where(d => !Path.GetFileName(d).StartsWith("."));

                foreach (var subdir in subdirs)
                {
                    FindDatasetFolders(subdir, currentDepth + 1, maxDepth);
                }
            }
            catch (UnauthorizedAccessException)
            {
                // Skip directories we can't access
            }
        }

        private void UpdateDatasetsList()
        {
            lstDatasets.Items.Clear();
            foreach (var dataset in selectedDatasets)
            {
                lstDatasets.Items.Add(dataset);
            }
        }

        private async void MergeDatasets_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(txtMergeOutput.Text))
            {
                System.Windows.MessageBox.Show("Please select output directory.");
                return;
            }

            if (selectedDatasets.Count == 0)
            {
                System.Windows.MessageBox.Show("Please add datasets to merge.");
                return;
            }

            btnMergeDatasets.IsEnabled = false;
            txtMergeLog.Clear();

            try
            {
                var settings = new MergeSettings
                {
                    UseFixedHeight = rbFixedHeight.IsChecked == true,
                    FixedHeight = int.Parse(txtFixedHeight.Text),
                    MinHeight = int.Parse(txtMinHeight.Text),
                    MaxHeight = int.Parse(txtMaxHeight.Text),
                    Quality = int.Parse(txtQuality.Text)
                };

                if (!string.IsNullOrEmpty(txtSeed.Text) && int.TryParse(txtSeed.Text, out int seed))
                {
                    settings.Seed = seed;
                    random = new Random(seed);
                }

                await Task.Run(() => MergeDatasets(selectedDatasets.ToList(), txtMergeOutput.Text, settings));
                LogMessage(txtMergeLog, "Merge completed successfully!");
            }
            catch (Exception ex)
            {
                LogMessage(txtMergeLog, $"Error: {ex.Message}");
            }
            finally
            {
                btnMergeDatasets.IsEnabled = true;
            }
        }

        private void MergeDatasets(List<string> datasets, string outputDir, MergeSettings settings)
        {
            Directory.CreateDirectory(outputDir);

            var mergedLabels = new List<string>();
            int imageCounter = 0;

            LogMessage(txtMergeLog, $"Merging {datasets.Count} datasets into: {outputDir}");
            LogMessage(txtMergeLog, $"Scaling mode: {(settings.UseFixedHeight ? $"Fixed height {settings.FixedHeight}px" : $"Variable height {settings.MinHeight}-{settings.MaxHeight}px")}");
            LogMessage(txtMergeLog, $"JPEG quality: {settings.Quality}");

            foreach (var datasetDir in datasets)
            {
                LogMessage(txtMergeLog, $"Processing: {datasetDir}");

                var labelsFile = Path.Combine(datasetDir, "labels.txt");
                if (!File.Exists(labelsFile))
                {
                    LogMessage(txtMergeLog, $"  Warning: No labels.txt found in {datasetDir}");
                    continue;
                }

                // Load labels
                var labelsDict = new Dictionary<string, string>();
                var lines = File.ReadAllLines(labelsFile, Encoding.UTF8);
                foreach (var line in lines)
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    var parts = line.Trim().Split(new[] { ' ' }, 2);
                    if (parts.Length == 2)
                    {
                        labelsDict[parts[0]] = parts[1];
                    }
                }

                // Process images
                var imageExtensions = new[] { ".jpg", ".jpeg", ".png", ".bmp", ".tiff" };
                var imageFiles = Directory.GetFiles(datasetDir)
                    .Where(f => imageExtensions.Contains(Path.GetExtension(f).ToLower()))
                    .OrderBy(f => f)
                    .ToList();

                int processedCount = 0;
                foreach (var imagePath in imageFiles)
                {
                    try
                    {
                        var originalFilename = Path.GetFileName(imagePath);
                        var newFilename = $"{imageCounter}.jpg";
                        var outputPath = Path.Combine(outputDir, newFilename);

                        // Determine target height
                        int targetHeight = settings.UseFixedHeight
                            ? settings.FixedHeight
                            : random.Next(settings.MinHeight, settings.MaxHeight + 1);

                        // Scale and save image
                        ScaleImage(imagePath, outputPath, targetHeight, settings.Quality);

                        // Get label
                        var label = labelsDict.ContainsKey(originalFilename)
                            ? labelsDict[originalFilename]
                            : $"unknown_{imageCounter}";

                        mergedLabels.Add($"{newFilename} {label}");
                        imageCounter++;
                        processedCount++;
                    }
                    catch (Exception ex)
                    {
                        LogMessage(txtMergeLog, $"  Error processing {imagePath}: {ex.Message}");
                    }
                }

                LogMessage(txtMergeLog, $"  Processed {processedCount} images");
            }

            // Save merged labels
            var mergedLabelsFile = Path.Combine(outputDir, "labels.txt");
            File.WriteAllLines(mergedLabelsFile, mergedLabels, Encoding.UTF8);

            LogMessage(txtMergeLog, $"Saved {mergedLabels.Count} labels to {mergedLabelsFile}");
            LogMessage(txtMergeLog, $"Total images processed: {imageCounter}");
        }

        private void ScaleImage(string inputPath, string outputPath, int targetHeight, int quality)
        {
            using (var originalImage = System.Drawing.Image.FromFile(inputPath))
            {
                // Calculate new dimensions
                double aspectRatio = (double)originalImage.Width / originalImage.Height;
                int newWidth = (int)(targetHeight * aspectRatio);

                using (var scaledImage = new Bitmap(newWidth, targetHeight))
                using (var graphics = Graphics.FromImage(scaledImage))
                {
                    graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    graphics.SmoothingMode = SmoothingMode.HighQuality;
                    graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    graphics.CompositingQuality = CompositingQuality.HighQuality;

                    graphics.DrawImage(originalImage, 0, 0, newWidth, targetHeight);

                    // Save with specified quality
                    var encoderParams = new EncoderParameters(1);
                    encoderParams.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, quality);
                    var jpegEncoder = GetEncoder(ImageFormat.Jpeg);

                    scaledImage.Save(outputPath, jpegEncoder, encoderParams);
                }
            }
        }

        private ImageCodecInfo GetEncoder(ImageFormat format)
        {
            var codecs = ImageCodecInfo.GetImageDecoders();
            foreach (var codec in codecs)
            {
                if (codec.FormatID == format.Guid)
                {
                    return codec;
                }
            }
            return null;
        }

        #endregion

        #region Generate Character Set

        private void BrowseCharsetDataset_Click(object sender, RoutedEventArgs e)
        {
            using (var dialog = new FolderBrowserDialog())
            {
                dialog.Description = "Select Dataset Directory";
                if (dialog.ShowDialog() == DialogResult.OK)
                {
                    txtCharsetDatasetDir.Text = dialog.SelectedPath;
                }
            }
        }

        private void BrowseCharsetOutput_Click(object sender, RoutedEventArgs e)
        {
            using (var dialog = new SaveFileDialog())
            {
                dialog.Filter = "Text files (*.txt)|*.txt|All files (*.*)|*.*";
                dialog.DefaultExt = "txt";
                dialog.FileName = "custom_dict.txt";
                if (dialog.ShowDialog() == DialogResult.OK)
                {
                    txtCharsetOutput.Text = dialog.FileName;
                }
            }
        }

        private async void GenerateCharset_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(txtCharsetDatasetDir.Text) || string.IsNullOrEmpty(txtCharsetOutput.Text))
            {
                System.Windows.MessageBox.Show("Please select both dataset directory and output file.");
                return;
            }

            btnGenerateCharset.IsEnabled = false;
            txtCharsetLog.Clear();
            txtCharsetPreview.Clear();

            try
            {
                var uniqueChars = await Task.Run(() => GenerateCharacterSet(txtCharsetDatasetDir.Text, txtCharsetOutput.Text));

                var charString = string.Join("", uniqueChars.OrderBy(c => c));
                txtCharsetPreview.Text = charString;

                LogMessage(txtCharsetLog, "Character set generation completed successfully!");
            }
            catch (Exception ex)
            {
                LogMessage(txtCharsetLog, $"Error: {ex.Message}");
            }
            finally
            {
                btnGenerateCharset.IsEnabled = true;
            }
        }

        private HashSet<char> GenerateCharacterSet(string datasetDir, string outputFile)
        {
            var labelsFile = Path.Combine(datasetDir, "labels.txt");
            if (!File.Exists(labelsFile))
            {
                throw new Exception($"labels.txt not found in {datasetDir}");
            }

            var uniqueChars = new HashSet<char>();
            var lines = File.ReadAllLines(labelsFile, Encoding.UTF8);

            LogMessage(txtCharsetLog, $"Reading labels from {labelsFile}...");

            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;

                var parts = line.Trim().Split(new[] { ' ' }, 2);
                if (parts.Length == 2)
                {
                    var label = parts[1];
                    foreach (var ch in label)
                    {
                        uniqueChars.Add(ch);
                    }
                }
            }

            var sortedChars = uniqueChars.OrderBy(c => c).ToList();

            // Write character dictionary
            File.WriteAllLines(outputFile, sortedChars.Select(c => c.ToString()), Encoding.UTF8);

            LogMessage(txtCharsetLog, $"Generated character dictionary with {sortedChars.Count} characters");
            LogMessage(txtCharsetLog, $"Dictionary saved to: {outputFile}");

            // Show character breakdown
            var letters = sortedChars.Count(c => char.IsLetter(c));
            var digits = sortedChars.Count(c => char.IsDigit(c));
            var punctuation = sortedChars.Count(c => char.IsPunctuation(c) || char.IsSymbol(c));
            var spaces = sortedChars.Count(c => char.IsWhiteSpace(c));

            LogMessage(txtCharsetLog, $"Character breakdown:");
            LogMessage(txtCharsetLog, $"  Letters: {letters}");
            LogMessage(txtCharsetLog, $"  Digits: {digits}");
            LogMessage(txtCharsetLog, $"  Punctuation/Symbols: {punctuation}");
            LogMessage(txtCharsetLog, $"  Spaces: {spaces}");

            return uniqueChars;
        }

        #endregion

        #region Analyze Dataset

        private void BrowseAnalyzeDataset_Click(object sender, RoutedEventArgs e)
        {
            using (var dialog = new FolderBrowserDialog())
            {
                dialog.Description = "Select Dataset Directory to Analyze";
                if (dialog.ShowDialog() == DialogResult.OK)
                {
                    txtAnalyzeDatasetDir.Text = dialog.SelectedPath;
                }
            }
        }

        private async void AnalyzeDataset_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(txtAnalyzeDatasetDir.Text))
            {
                System.Windows.MessageBox.Show("Please select a dataset directory.");
                return;
            }

            btnAnalyzeDataset.IsEnabled = false;
            ClearAnalysisResults();

            try
            {
                string text = System.Windows.Application.Current.Dispatcher.Invoke(() => txtAnalyzeDatasetDir.Text);
                var info = await Task.Run(() => AnalyzeDataset(text));
                DisplayAnalysisResults(info);
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Error analyzing dataset: {ex.Message}");
            }
            finally
            {
                btnAnalyzeDataset.IsEnabled = true;
            }
        }

        private DatasetInfo AnalyzeDataset(string datasetDir)
        {
            var labelsFile = Path.Combine(datasetDir, "labels.txt");
            if (!File.Exists(labelsFile))
            {
                throw new Exception($"labels.txt not found in {datasetDir}");
            }

            var info = new DatasetInfo();
            var labels = new List<string>();
            var filenames = new List<string>();
            var allChars = new List<char>();

            var lines = File.ReadAllLines(labelsFile, Encoding.UTF8);

            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;

                var parts = line.Trim().Split(new[] { ' ' }, 2);
                if (parts.Length == 2)
                {
                    var filename = parts[0];
                    var label = parts[1];

                    // Check if image file exists
                    var imagePath = Path.Combine(datasetDir, filename);
                    if (File.Exists(imagePath))
                    {
                        filenames.Add(filename);
                        labels.Add(label);
                        allChars.AddRange(label);
                    }
                }
            }

            // Basic statistics
            info.TotalSamples = labels.Count;
            info.UniqueChars = allChars.Distinct().Count();

            var labelLengths = labels.Select(l => l.Length).ToList();
            info.AvgLabelLength = labelLengths.Average();
            info.MinLength = labelLengths.Min();
            info.MaxLength = labelLengths.Max();

            // Character breakdown
            var uniqueChars = allChars.Distinct().OrderBy(c => c).ToList();
            info.CharacterSet = string.Join("", uniqueChars);

            info.CharBreakdown = new Dictionary<string, int>
            {
                ["Letters"] = allChars.Count(c => char.IsLetter(c)),
                ["Digits"] = allChars.Count(c => char.IsDigit(c)),
                ["Punctuation"] = allChars.Count(c => char.IsPunctuation(c) || char.IsSymbol(c)),
                ["Spaces"] = allChars.Count(c => char.IsWhiteSpace(c)),
                ["Others"] = allChars.Count(c => !char.IsLetterOrDigit(c) && !char.IsPunctuation(c) && !char.IsSymbol(c) && !char.IsWhiteSpace(c))
            };

            return info;
        }

        private void ClearAnalysisResults()
        {
            lblTotalSamples.Content = "0";
            lblUniqueChars.Content = "0";
            lblAvgLength.Content = "0.0";
            lblMinLength.Content = "0";
            lblMaxLength.Content = "0";
            lblLetters.Content = "0";
            lblDigits.Content = "0";
            lblPunctuation.Content = "0";
            lblSpaces.Content = "0";
            lblOthers.Content = "0";
            lstSampleLabels.Items.Clear();
            txtRecommendations.Clear();
        }

        private void DisplayAnalysisResults(DatasetInfo info)
        {
            // Update statistics labels
            lblTotalSamples.Content = info.TotalSamples.ToString();
            lblUniqueChars.Content = info.UniqueChars.ToString();
            lblAvgLength.Content = info.AvgLabelLength.ToString("F1");
            lblMinLength.Content = info.MinLength.ToString();
            lblMaxLength.Content = info.MaxLength.ToString();

            // Update character breakdown
            lblLetters.Content = info.CharBreakdown["Letters"].ToString();
            lblDigits.Content = info.CharBreakdown["Digits"].ToString();
            lblPunctuation.Content = info.CharBreakdown["Punctuation"].ToString();
            lblSpaces.Content = info.CharBreakdown["Spaces"].ToString();
            lblOthers.Content = info.CharBreakdown["Others"].ToString();

            // Show sample labels
            var labelsFile = Path.Combine(txtAnalyzeDatasetDir.Text, "labels.txt");
            var lines = File.ReadAllLines(labelsFile, Encoding.UTF8);
            var sampleCount = Math.Min(10, lines.Length);

            for (int i = 0; i < sampleCount; i++)
            {
                var line = lines[i].Trim();
                if (!string.IsNullOrEmpty(line))
                {
                    var parts = line.Split(new[] { ' ' }, 2);
                    if (parts.Length == 2)
                    {
                        lstSampleLabels.Items.Add($"{parts[0]}: '{parts[1]}'");
                    }
                }
            }

            // Generate recommendations
            var recommendations = new List<string>();

            if (info.TotalSamples < 1000)
                recommendations.Add("⚠️ Dataset is small (<1000 samples). Consider generating more data.");
            else if (info.TotalSamples < 5000)
                recommendations.Add("⚠️ Dataset is moderate (1k-5k samples). Should work but more data is better.");
            else
                recommendations.Add("✅ Good dataset size for training.");

            if (info.AvgLabelLength > 20)
                recommendations.Add("⚠️ Long average label length. Consider adjusting max_text_length in config.");

            if (info.UniqueChars > 200)
                recommendations.Add("⚠️ Very large character set. Training might be challenging.");
            else if (info.UniqueChars < 20)
                recommendations.Add("⚠️ Small character set. Consider adding more character diversity.");

            txtRecommendations.Text = string.Join("\n", recommendations);
        }

        #endregion

        #region Utility Methods

        private void LogMessage(System.Windows.Controls.TextBox textBox, string message)
        {
            Dispatcher.Invoke(() =>
            {
                textBox.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}\n");
                textBox.ScrollToEnd();
            });
        }

        #endregion
    }
}