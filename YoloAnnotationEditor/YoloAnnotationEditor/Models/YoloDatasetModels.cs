using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace YoloAnnotationEditor.Models
{
    public class DatasetStatistics
    {
        public int TotalImages { get; set; }
        public int TotalAnnotations { get; set; }
        public int TrainImages { get; set; }
        public int ValImages { get; set; }
        public int TestImages { get; set; }
        public Dictionary<int, ClassStatistics> ClassStats { get; set; } = new Dictionary<int, ClassStatistics>();
        public double AverageAnnotationsPerImage { get; set; }
        public int MinAnnotationsPerImage { get; set; }
        public int MaxAnnotationsPerImage { get; set; }
    }

    public class ClassStatistics : INotifyPropertyChanged
    {
        private int _classId;
        private string _className;
        private int _instanceCount;
        private int _imageCount;
        private double _percentage;

        public int ClassId
        {
            get => _classId;
            set => SetProperty(ref _classId, value);
        }

        public string ClassName
        {
            get => _className;
            set => SetProperty(ref _className, value);
        }

        public int InstanceCount
        {
            get => _instanceCount;
            set => SetProperty(ref _instanceCount, value);
        }

        public int ImageCount
        {
            get => _imageCount;
            set => SetProperty(ref _imageCount, value);
        }

        public double Percentage
        {
            get => _percentage;
            set => SetProperty(ref _percentage, value);
        }

        public string DisplayText => $"{ClassId}: {ClassName} ({InstanceCount} instances, {Percentage:F2}%)";

        public event PropertyChangedEventHandler PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value))
                return false;

            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }
    }

    public class ValidationResult
    {
        public string ImagePath { get; set; }
        public string LabelPath { get; set; }
        public List<ValidationIssue> Issues { get; set; } = new List<ValidationIssue>();
        public ValidationCategory Category { get; set; }
        public bool IsValid => Issues.Count == 0;
    }

    public class ValidationIssue
    {
        public ValidationIssueType Type { get; set; }
        public string Description { get; set; }
        public int? LineNumber { get; set; }
    }

    public enum ValidationIssueType
    {
        MinimumROI,
        DuplicateClass,
        InvalidCoordinates,
        SpecificClass,
        KnownConfusion,
        MissingLabel,
        MissingImage,
        EmptyLabel
    }

    public enum ValidationCategory
    {
        Fine,
        MinimumROI,
        DuplicateClasses,
        SpecificClass,
        KnownConfusions,
        InvalidCoordinates,
        MissingFiles
    }

    public class BalanceConfiguration
    {
        public string PrimaryDataset { get; set; }
        public string SecondaryDataset { get; set; }
        public int TargetInstancesPerClass { get; set; }
        public Dictionary<int, int> CustomTargets { get; set; } = new Dictionary<int, int>();
        public BalanceStrategy Strategy { get; set; }
    }

    public enum BalanceStrategy
    {
        EqualForAll,
        ToMinimumClass,
        CustomPerClass
    }

    public class ImageData
    {
        public string ImagePath { get; set; }
        public string LabelPath { get; set; }
        public List<int> Classes { get; set; } = new List<int>();
        public Dictionary<int, int> ClassCounts { get; set; } = new Dictionary<int, int>();
        public bool IsTraining { get; set; }
        public string Split { get; set; } // "train", "val", or "test"
    }

    public class RemapRule
    {
        public int OldClassId { get; set; }
        public int NewClassId { get; set; }
        public string Description { get; set; }
    }

    public class DuplicateImageInfo
    {
        public string ImagePath { get; set; }
        public string Hash { get; set; }
        public List<string> DuplicatePaths { get; set; } = new List<string>();
        public long FileSize { get; set; }
    }

    public class MissingFileInfo
    {
        public string FilePath { get; set; }
        public MissingFileType Type { get; set; }
        public string ExpectedPath { get; set; }
    }

    public enum MissingFileType
    {
        MissingLabel,
        MissingImage
    }

    public class DatasetComparisonResult
    {
        public DatasetStatistics Dataset1Stats { get; set; }
        public DatasetStatistics Dataset2Stats { get; set; }
        public string Dataset1Name { get; set; }
        public string Dataset2Name { get; set; }
        public Dictionary<int, ClassComparison> ClassComparisons { get; set; } = new Dictionary<int, ClassComparison>();
    }

    public class ClassComparison
    {
        public int ClassId { get; set; }
        public string ClassName { get; set; }
        public int Dataset1Count { get; set; }
        public int Dataset2Count { get; set; }
        public int Difference => Dataset2Count - Dataset1Count;
        public double PercentageDifference => Dataset1Count > 0 ? ((double)Difference / Dataset1Count) * 100 : 0;
    }
}
