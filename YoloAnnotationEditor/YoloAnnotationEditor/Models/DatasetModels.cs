using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace YoloAnnotationEditor.Models
{
    public class ImageItem : INotifyPropertyChanged
    {
        private string _fileName;
        private string _filePath;
        private string _labelPath;
        private BitmapImage _thumbnail;
        private List<YoloLabel> _annotations = new List<YoloLabel>();
        private List<int> _classIds = new List<int>();

        public string FileName
        {
            get => _fileName;
            set => SetProperty(ref _fileName, value);
        }

        public string FilePath
        {
            get => _filePath;
            set => SetProperty(ref _filePath, value);
        }

        public string LabelPath
        {
            get => _labelPath;
            set => SetProperty(ref _labelPath, value);
        }

        public BitmapImage Thumbnail
        {
            get => _thumbnail;
            set => SetProperty(ref _thumbnail, value);
        }

        public List<YoloLabel> Annotations
        {
            get => _annotations;
            set => SetProperty(ref _annotations, value);
        }

        public List<int> ClassIds
        {
            get => _classIds;
            set => SetProperty(ref _classIds, value);
        }

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

    public class YoloLabel
    {
        public int ClassId { get; set; }
        public float CenterX { get; set; }
        public float CenterY { get; set; }
        public float Width { get; set; }
        public float Height { get; set; }
    }

    public class ClassItem
    {
        public int ClassId { get; set; }
        public string Name { get; set; }
        public System.Windows.Media.Brush Color { get; set; }
        public string DisplayName => $"{ClassId}: {Name}";
    }
}
