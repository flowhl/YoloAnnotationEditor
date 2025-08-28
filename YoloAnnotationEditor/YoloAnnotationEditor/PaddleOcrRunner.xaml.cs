using OpenCvSharp;
using Sdcb.PaddleInference;
using Sdcb.PaddleOCR;
using Sdcb.PaddleOCR.Models;
using Sdcb.PaddleOCR.Models.Local;
using System;
using System.Collections.Generic;
using System.Diagnostics;
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
using MessageBox = System.Windows.MessageBox;
using UserControl = System.Windows.Controls.UserControl;

namespace YoloAnnotationEditor
{
    /// <summary>
    /// Interaction logic for PaddleOcrRunner.xaml
    /// </summary>
    public partial class PaddleOcrRunner : UserControl
    {
        private PaddleOcrAll _ocr = null;
        private FullOcrModel _model = null;
        private string _imagePath = string.Empty;

        public PaddleOcrRunner()
        {
            InitializeComponent();
        }

        public void LoadOcrModel(int model)
        {
            string modelName = "Model not loaded";
            txtResult.Text = null;
            if (model == 0)
            {
                _model = LocalFullModels.EnglishV3;
                modelName = "English V3 Model Loaded";
            }
            else if (model == 1)
            {
                _model = LocalFullModels.EnglishV4;
                modelName = "English V4 Model Loaded";
            }
            else if (model == 2)
            {
                _model = LocalFullModels.ChineseV5;
                modelName = "Chinese V5 Model Loaded";
            }
            else if (model == 3)
            {
                var _det = DetectionModel.FromDirectory("D:\\AI\\OCR\\Models\\Pretrained\\PP-OCRv5_server_det_infer", ModelVersion.V5);
                var _rec = RecognizationModel.FromDirectoryV5("D:\\AI\\OCR\\Training\\V1\\FinishedModels\\Dataset-V2\\last-epoch");
                _model = new FullOcrModel(_det, _rec);
                modelName = "Custom Local Model Loaded";
            }
            else
            {
                throw new ArgumentException("Invalid model index");
            }

            _ocr = new PaddleOcrAll(_model, PaddleDevice.Onnx());
            txtModelStatus.Text = modelName;
        }

        private void BtnLoadModel_Click(object sender, RoutedEventArgs e)
        {
            LoadOcrModel(cbModelVersion.SelectedIndex);
        }

        private void Detect()
        {

            txtResult.Text = null;

            if (string.IsNullOrEmpty(_imagePath))
            {
                MessageBox.Show("Please select an image first.");
                return;
            }

            //display image in ui
            var uri = new Uri(_imagePath, UriKind.Absolute);
            var bitmap = new BitmapImage(uri);
            imgDisplay.Source = bitmap;

            var mat = new Mat(_imagePath, ImreadModes.Color);
            imgDisplay.Height = mat.Height;
            imgDisplay.Width = mat.Width;

            string whitelist = GetWhiteList(); //TODO: Later

            var sw = Stopwatch.StartNew();
            var results = _ocr.Run(mat);
            sw.Stop();

            var sb = new StringBuilder();
            foreach (var result in results.Regions)
            {
                sb.AppendLine($"Text: {result.Text}, Confidence: {result.Score}");
            }
            sb.AppendLine($"Took: {sw.ElapsedMilliseconds}ms");
            txtResult.Text = sb.ToString();
        }


        private string GetWhiteList()
        {
            return null;
        }

        private void BtnDetect_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Detect();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error during detection: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnSelectImage_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog openFileDialog = new OpenFileDialog
            {
                Filter = "Image Files (*.jpg;*.jpeg;*.png)|*.jpg;*.jpeg;*.png|All Files (*.*)|*.*",
                Title = "Select Image File"
            };

            openFileDialog.ShowDialog();

            if (openFileDialog.FileName != null)
            {
                _imagePath = openFileDialog.FileName;
                TxtImagePath.Text = _imagePath;
            }
        }
    }
}
