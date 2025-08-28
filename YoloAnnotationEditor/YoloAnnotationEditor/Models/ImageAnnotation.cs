using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Collections.ObjectModel;
using System.IO;
using System.Globalization;
using System.Windows.Data;

namespace YoloAnnotationEditor
{
    public class ImageAnnotation : INotifyPropertyChanged
    {
        private string _filename;
        private string _text;
        private string _fullPath;

        public string Filename
        {
            get => _filename;
            set
            {
                _filename = value;
                OnPropertyChanged();
            }
        }

        public string Text
        {
            get => _text;
            set
            {
                _text = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasLabel));
                TextChanged?.Invoke(this);
            }
        }

        public string FullPath
        {
            get => _fullPath;
            set
            {
                _fullPath = value;
                OnPropertyChanged();
            }
        }

        public bool HasLabel => !string.IsNullOrWhiteSpace(Text);

        public event PropertyChangedEventHandler PropertyChanged;
        public event Action<ImageAnnotation> TextChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public class AnnotationDataSet : INotifyPropertyChanged
    {
        private ObservableCollection<ImageAnnotation> _annotations;
        private ImageAnnotation _selectedAnnotation;
        private string _folderPath;

        public ObservableCollection<ImageAnnotation> Annotations
        {
            get => _annotations;
            set
            {
                _annotations = value;
                OnPropertyChanged();
            }
        }

        public ImageAnnotation SelectedAnnotation
        {
            get => _selectedAnnotation;
            set
            {
                _selectedAnnotation = value;
                OnPropertyChanged();
            }
        }

        public string FolderPath
        {
            get => _folderPath;
            set
            {
                _folderPath = value;
                OnPropertyChanged();
            }
        }

        public string LabelsFilePath => string.IsNullOrEmpty(FolderPath) ? null : Path.Combine(FolderPath, "labels.txt");

        public AnnotationDataSet()
        {
            Annotations = new ObservableCollection<ImageAnnotation>();
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public class BooleanConverter : IValueConverter
    {
        public static readonly BooleanConverter NotNullConverter = new BooleanConverter();

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value != null;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}