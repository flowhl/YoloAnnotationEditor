using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using CliWrap;
using CliWrap.Buffered;
using YoloAnnotationEditor.Models;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace YoloAnnotationEditor.Helpers
{
    public static class YoloDatasetHelper
    {
        /// <summary>
        /// Validates if a directory is a valid YOLO dataset
        /// </summary>
        public static bool ValidateYoloDataset(string path)
        {
            if (string.IsNullOrEmpty(path) || !Directory.Exists(path))
                return false;

            string imagesPath = Path.Combine(path, "images");
            string labelsPath = Path.Combine(path, "labels");

            return Directory.Exists(imagesPath) && Directory.Exists(labelsPath);
        }

        /// <summary>
        /// Gets all image files from a specific split
        /// </summary>
        public static List<string> GetImagesFromSplit(string datasetPath, string split)
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

        /// <summary>
        /// Gets all label files from a specific split
        /// </summary>
        public static List<string> GetLabelsFromSplit(string datasetPath, string split)
        {
            string labelsPath = Path.Combine(datasetPath, "labels", split);

            if (!Directory.Exists(labelsPath))
                return new List<string>();

            return Directory.GetFiles(labelsPath, "*.txt")
                .Select(f => Path.GetFileNameWithoutExtension(f))
                .ToList();
        }

        /// <summary>
        /// Reads class names from dataset YAML file
        /// </summary>
        public static Dictionary<int, string> LoadClassesFromYaml(string datasetPath)
        {
            Dictionary<int, string> classes = new Dictionary<int, string>();

            // Look for .yaml or .yml files
            var yamlFiles = Directory.GetFiles(datasetPath, "*.yaml")
                .Concat(Directory.GetFiles(datasetPath, "*.yml"))
                .ToList();

            if (yamlFiles.Count == 0)
                return classes;

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
            catch (Exception)
            {
                // Silently fail and return empty dictionary
            }

            return classes;
        }

        /// <summary>
        /// Reads a YOLO label file and returns class IDs with counts
        /// </summary>
        public static Dictionary<int, int> ReadLabelFile(string labelPath)
        {
            var classCounts = new Dictionary<int, int>();

            if (!File.Exists(labelPath))
                return classCounts;

            try
            {
                var lines = File.ReadAllLines(labelPath);
                foreach (var line in lines)
                {
                    if (string.IsNullOrWhiteSpace(line))
                        continue;

                    var parts = line.Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 5 && int.TryParse(parts[0], out int classId))
                    {
                        if (!classCounts.ContainsKey(classId))
                            classCounts[classId] = 0;
                        classCounts[classId]++;
                    }
                }
            }
            catch (Exception)
            {
                // Return empty dictionary on error
            }

            return classCounts;
        }

        /// <summary>
        /// Validates YOLO coordinates (should be between 0 and 1)
        /// </summary>
        public static bool ValidateYoloCoordinates(string line, out string error)
        {
            error = null;

            var parts = line.Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 5)
            {
                error = "Insufficient values (expected 5: class_id x y w h)";
                return false;
            }

            if (!int.TryParse(parts[0], out int classId) || classId < 0)
            {
                error = $"Invalid class ID: {parts[0]}";
                return false;
            }

            for (int i = 1; i < 5; i++)
            {
                if (!float.TryParse(parts[i], System.Globalization.NumberStyles.Float, 
                    System.Globalization.CultureInfo.InvariantCulture, out float value))
                {
                    error = $"Invalid numeric value at position {i}: {parts[i]}";
                    return false;
                }

                if (value < 0 || value > 1)
                {
                    error = $"Coordinate out of range [0,1] at position {i}: {value}";
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Creates dataset YAML file
        /// </summary>
        public static void CreateDatasetYaml(string outputPath, Dictionary<int, string> classes, List<string> splits)
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

        /// <summary>
        /// Copies image and label files
        /// </summary>
        public static void CopyImageAndLabel(string srcDatasetPath, string destDatasetPath, string split, string imageNameWithoutExt)
        {
            string srcImagesPath = Path.Combine(srcDatasetPath, "images", split);
            string srcLabelsPath = Path.Combine(srcDatasetPath, "labels", split);
            string destImagesPath = Path.Combine(destDatasetPath, "images", split);
            string destLabelsPath = Path.Combine(destDatasetPath, "labels", split);

            // Ensure destination directories exist
            Directory.CreateDirectory(destImagesPath);
            Directory.CreateDirectory(destLabelsPath);

            // Find the actual image file
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

        /// <summary>
        /// Calculates MD5 hash of a file
        /// </summary>
        public static string CalculateFileHash(string filePath)
        {
            using (var md5 = MD5.Create())
            {
                using (var stream = File.OpenRead(filePath))
                {
                    byte[] hash = md5.ComputeHash(stream);
                    return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
                }
            }
        }

        /// <summary>
        /// Gets available splits in a dataset
        /// </summary>
        public static List<string> GetAvailableSplits(string datasetPath)
        {
            List<string> splits = new List<string>();
            string imagesPath = Path.Combine(datasetPath, "images");

            if (!Directory.Exists(imagesPath))
                return splits;

            foreach (var split in new[] { "train", "val", "test" })
            {
                if (Directory.Exists(Path.Combine(imagesPath, split)))
                {
                    splits.Add(split);
                }
            }

            return splits;
        }

        /// <summary>
        /// Creates directory structure for a dataset
        /// </summary>
        public static void CreateDirectoryStructure(string basePath, List<string> splits)
        {
            Directory.CreateDirectory(basePath);

            foreach (var split in splits)
            {
                Directory.CreateDirectory(Path.Combine(basePath, "images", split));
                Directory.CreateDirectory(Path.Combine(basePath, "labels", split));
            }
        }

        /// <summary>
        /// Gets the full path to an image file
        /// </summary>
        public static string GetImagePath(string datasetPath, string split, string imageNameWithoutExt)
        {
            string imagesPath = Path.Combine(datasetPath, "images", split);
            string[] possibleExtensions = { ".png", ".jpg", ".jpeg", ".PNG", ".JPG", ".JPEG" };

            foreach (var ext in possibleExtensions)
            {
                string testPath = Path.Combine(imagesPath, imageNameWithoutExt + ext);
                if (File.Exists(testPath))
                {
                    return testPath;
                }
            }

            return null;
        }

        /// <summary>
        /// Gets the full path to a label file
        /// </summary>
        public static string GetLabelPath(string datasetPath, string split, string imageNameWithoutExt)
        {
            return Path.Combine(datasetPath, "labels", split, imageNameWithoutExt + ".txt");
        }

        /// <summary>
        /// Copies files using robocopy for better performance
        /// </summary>
        public static async Task RobocopyFilesAsync(string sourceDir, string destDir, string filePattern, Action<string> logCallback = null)
        {
            try
            {
                // Ensure destination directory exists
                Directory.CreateDirectory(destDir);

                // Calculate thread count (all CPU cores except 2)
                int threadCount = Math.Max(1, Environment.ProcessorCount - 2);

                // Build robocopy command using CliWrap
                // /MT:n = multithreaded (n threads)
                // /NFL = no file list (less verbose)
                // /NDL = no directory list (less verbose)
                // /NJH = no job header
                // /NJS = no job summary
                // /R:2 = retry 2 times on failure
                // /W:1 = wait 1 second between retries
                var result = await Cli.Wrap("robocopy")
                    .WithArguments(args => args
                        .Add(sourceDir)
                        .Add(destDir)
                        .Add(filePattern)
                        .Add($"/MT:{threadCount}")
                        .Add("/NFL")
                        .Add("/NDL")
                        .Add("/NJH")
                        .Add("/NJS")
                        .Add("/R:2")
                        .Add("/W:1"))
                    .WithValidation(CommandResultValidation.None) // Don't throw on non-zero exit codes
                    .ExecuteBufferedAsync();

                // Robocopy exit codes:
                // 0 = No files copied, no failures
                // 1 = Files copied successfully
                // 2 = Extra files or directories detected
                // 4 = Mismatched files or directories
                // 8 = Some files or directories could not be copied (copy errors occurred)
                // Exit codes 0-7 are considered success, 8+ are errors
                if (result.ExitCode >= 8)
                {
                    logCallback?.Invoke($"Robocopy warning (exit code {result.ExitCode}): {result.StandardError}");
                }
            }
            catch (Exception ex)
            {
                logCallback?.Invoke($"Robocopy failed: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Checks if robocopy is available on the system
        /// </summary>
        private static bool? _isRobocopyAvailable = null;
        private static string _robocopyPath = null;

        private static async Task<bool> IsRobocopyAvailableAsync()
        {
            if (_isRobocopyAvailable.HasValue)
                return _isRobocopyAvailable.Value;

            try
            {
                // Try robocopy from PATH first (most reliable on Windows)
                var result = await Cli.Wrap("robocopy.exe")
                    .WithArguments("/?")
                    .WithValidation(CommandResultValidation.None)
                    .ExecuteBufferedAsync();

                _robocopyPath = "robocopy.exe";
                _isRobocopyAvailable = true;
                return true;
            }
            catch
            {
                _isRobocopyAvailable = false;
                return false;
            }
        }

        /// <summary>
        /// Copies image and label files using robocopy for better performance
        /// Uses batching to copy multiple files efficiently
        /// Falls back to File.Copy if robocopy is not available
        /// </summary>
        public static async Task CopyImageAndLabelWithRobocopyAsync(string srcDatasetPath, string destDatasetPath, string split,
            List<string> imageNamesWithoutExt, Action<string> logCallback = null)
        {
            if (imageNamesWithoutExt == null || imageNamesWithoutExt.Count == 0)
                return;

            string srcImagesPath = Path.Combine(srcDatasetPath, "images", split);
            string srcLabelsPath = Path.Combine(srcDatasetPath, "labels", split);
            string destImagesPath = Path.Combine(destDatasetPath, "images", split);
            string destLabelsPath = Path.Combine(destDatasetPath, "labels", split);

            // Ensure destination directories exist
            Directory.CreateDirectory(destImagesPath);
            Directory.CreateDirectory(destLabelsPath);

            // Build file lists
            var imageFiles = new List<string>();
            var labelFiles = new List<string>();
            var imagePaths = new List<string>();
            var labelPaths = new List<string>();

            foreach (var imageName in imageNamesWithoutExt)
            {
                // Find image file with extension
                string[] possibleExtensions = { ".png", ".jpg", ".jpeg", ".PNG", ".JPG", ".JPEG" };
                foreach (var ext in possibleExtensions)
                {
                    string testPath = Path.Combine(srcImagesPath, imageName + ext);
                    if (File.Exists(testPath))
                    {
                        imageFiles.Add(Path.GetFileName(testPath));
                        imagePaths.Add(testPath);
                        break;
                    }
                }

                // Add label file
                string labelFile = imageName + ".txt";
                string labelPath = Path.Combine(srcLabelsPath, labelFile);
                if (File.Exists(labelPath))
                {
                    labelFiles.Add(labelFile);
                    labelPaths.Add(labelPath);
                }
            }

            // Try robocopy first, fall back to File.Copy if it fails
            bool useRobocopy = await IsRobocopyAvailableAsync();

            if (!useRobocopy)
            {
                logCallback?.Invoke("Robocopy not available, using standard file copy...");

                // Fallback to standard file copy
                await Task.Run(() =>
                {
                    for (int i = 0; i < imagePaths.Count; i++)
                    {
                        string destPath = Path.Combine(destImagesPath, Path.GetFileName(imagePaths[i]));
                        File.Copy(imagePaths[i], destPath, true);
                    }

                    for (int i = 0; i < labelPaths.Count; i++)
                    {
                        string destPath = Path.Combine(destLabelsPath, Path.GetFileName(labelPaths[i]));
                        File.Copy(labelPaths[i], destPath, true);
                    }
                });

                return;
            }

            // Calculate thread count (all CPU cores except 2)
            int threadCount = Math.Max(1, Environment.ProcessorCount - 2);

            // Use the cached robocopy path that was validated in IsRobocopyAvailableAsync()
            string robocopyExe = _robocopyPath;

            try
            {
                // Batch copy files in groups to avoid command line length limits
                int batchSize = 1000;
                int totalFiles = imageFiles.Count + labelFiles.Count;
                int filesProcessed = 0;

                // Copy images in batches
                if (imageFiles.Count > 0)
                {
                    logCallback?.Invoke($"Starting robocopy for {imageFiles.Count} images with {threadCount} threads...");

                    for (int i = 0; i < imageFiles.Count; i += batchSize)
                    {
                        var batch = imageFiles.Skip(i).Take(batchSize).ToList();
                        int batchNumber = (i / batchSize) + 1;
                        int totalBatches = (int)Math.Ceiling(imageFiles.Count / (double)batchSize);

                        logCallback?.Invoke($"Copying image batch {batchNumber}/{totalBatches} ({batch.Count} files)...");

                        // Build arguments using CliWrap
                        var result = await Cli.Wrap(robocopyExe)
                            .WithArguments(args =>
                            {
                                args.Add(srcImagesPath);
                                args.Add(destImagesPath);
                                foreach (var file in batch)
                                {
                                    args.Add(file);
                                }
                                args.Add($"/MT:{threadCount}");
                                args.Add("/R:2");
                                args.Add("/W:1");
                                args.Add("/NFL");
                                args.Add("/NDL");
                                args.Add("/NJH");
                                args.Add("/NJS");
                            })
                            .WithValidation(CommandResultValidation.None)
                            .ExecuteBufferedAsync();

                        filesProcessed += batch.Count;

                        // Exit codes 0-7 are success, 8+ are errors
                        if (result.ExitCode >= 8)
                        {
                            logCallback?.Invoke($"Robocopy images batch warning (exit code {result.ExitCode})");
                        }
                        else
                        {
                            logCallback?.Invoke($"Completed image batch {batchNumber}/{totalBatches} ({filesProcessed}/{totalFiles} total files)");
                        }
                    }

                    logCallback?.Invoke($"Completed copying {imageFiles.Count} images");
                }

                // Copy labels in batches
                if (labelFiles.Count > 0)
                {
                    logCallback?.Invoke($"Starting robocopy for {labelFiles.Count} labels with {threadCount} threads...");

                    for (int i = 0; i < labelFiles.Count; i += batchSize)
                    {
                        var batch = labelFiles.Skip(i).Take(batchSize).ToList();
                        int batchNumber = (i / batchSize) + 1;
                        int totalBatches = (int)Math.Ceiling(labelFiles.Count / (double)batchSize);

                        logCallback?.Invoke($"Copying label batch {batchNumber}/{totalBatches} ({batch.Count} files)...");

                        var result = await Cli.Wrap(robocopyExe)
                            .WithArguments(args =>
                            {
                                args.Add(srcLabelsPath);
                                args.Add(destLabelsPath);
                                foreach (var file in batch)
                                {
                                    args.Add(file);
                                }
                                args.Add($"/MT:{threadCount}");
                                args.Add("/R:2");
                                args.Add("/W:1");
                                args.Add("/NFL");
                                args.Add("/NDL");
                                args.Add("/NJH");
                                args.Add("/NJS");
                            })
                            .WithValidation(CommandResultValidation.None)
                            .ExecuteBufferedAsync();

                        filesProcessed += batch.Count;

                        // Exit codes 0-7 are success, 8+ are errors
                        if (result.ExitCode >= 8)
                        {
                            logCallback?.Invoke($"Robocopy labels batch warning (exit code {result.ExitCode})");
                        }
                        else
                        {
                            logCallback?.Invoke($"Completed label batch {batchNumber}/{totalBatches} ({filesProcessed}/{totalFiles} total files)");
                        }
                    }

                    logCallback?.Invoke($"Completed copying {labelFiles.Count} labels");
                }
            }
            catch (Exception ex)
            {
                logCallback?.Invoke($"Robocopy failed: {ex.Message}, falling back to File.Copy...");

                // Fallback to standard file copy if robocopy fails
                await Task.Run(() =>
                {
                    for (int i = 0; i < imagePaths.Count; i++)
                    {
                        string destPath = Path.Combine(destImagesPath, Path.GetFileName(imagePaths[i]));
                        if (!File.Exists(destPath))
                            File.Copy(imagePaths[i], destPath, true);
                    }

                    for (int i = 0; i < labelPaths.Count; i++)
                    {
                        string destPath = Path.Combine(destLabelsPath, Path.GetFileName(labelPaths[i]));
                        if (!File.Exists(destPath))
                            File.Copy(labelPaths[i], destPath, true);
                    }
                });
            }
        }
    }
}
