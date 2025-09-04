using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace YoloAnnotationEditor.Models
{
    public class DatasetEntry
    {
        public string FileName { get; set; }
        public string Label { get; set; }
        public string FullPath { get; set; }
    }

    public class DatasetInfo : INotifyPropertyChanged
    {
        private int _totalSamples;
        private int _uniqueChars;
        private double _avgLabelLength;
        private int _minLength;
        private int _maxLength;
        private string _characterSet;
        private Dictionary<string, int> _charBreakdown;

        public int TotalSamples
        {
            get => _totalSamples;
            set { _totalSamples = value; OnPropertyChanged(); }
        }

        public int UniqueChars
        {
            get => _uniqueChars;
            set { _uniqueChars = value; OnPropertyChanged(); }
        }

        public double AvgLabelLength
        {
            get => _avgLabelLength;
            set { _avgLabelLength = value; OnPropertyChanged(); }
        }

        public int MinLength
        {
            get => _minLength;
            set { _minLength = value; OnPropertyChanged(); }
        }

        public int MaxLength
        {
            get => _maxLength;
            set { _maxLength = value; OnPropertyChanged(); }
        }

        public string CharacterSet
        {
            get => _characterSet;
            set { _characterSet = value; OnPropertyChanged(); }
        }

        public Dictionary<string, int> CharBreakdown
        {
            get => _charBreakdown;
            set { _charBreakdown = value; OnPropertyChanged(); }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public class MergeSettings : INotifyPropertyChanged
    {
        private bool _useFixedHeight = true;
        private int _fixedHeight = 32;
        private int _minHeight = 14;
        private int _maxHeight = 20;
        private int _quality = 95;
        private int? _seed;

        public bool UseFixedHeight
        {
            get => _useFixedHeight;
            set { _useFixedHeight = value; OnPropertyChanged(); }
        }

        public int FixedHeight
        {
            get => _fixedHeight;
            set { _fixedHeight = value; OnPropertyChanged(); }
        }

        public int MinHeight
        {
            get => _minHeight;
            set { _minHeight = value; OnPropertyChanged(); }
        }

        public int MaxHeight
        {
            get => _maxHeight;
            set { _maxHeight = value; OnPropertyChanged(); }
        }

        public int Quality
        {
            get => _quality;
            set { _quality = value; OnPropertyChanged(); }
        }

        public int? Seed
        {
            get => _seed;
            set { _seed = value; OnPropertyChanged(); }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public class ConversionSettings : INotifyPropertyChanged
    {
        private double _splitRatio = 0.8;

        public double SplitRatio
        {
            get => _splitRatio;
            set { _splitRatio = value; OnPropertyChanged(); }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
