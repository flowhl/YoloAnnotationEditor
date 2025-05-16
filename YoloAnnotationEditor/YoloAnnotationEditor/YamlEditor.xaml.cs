using System;
using System.Collections.Generic;
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
using UserControl = System.Windows.Controls.UserControl;

namespace YoloAnnotationEditor
{
    /// <summary>
    /// Interaction logic for YamlEditor.xaml
    /// </summary>
    public partial class YamlEditor : UserControl
    {
        public YamlEditor()
        {
            InitializeComponent();
            // Create and add the YAML editor control to the main window
            var yamlEditor = new YamlEditorControl();

            // Subscribe to events if needed
            yamlEditor.YamlFileLoaded += OnYamlFileLoaded;
            yamlEditor.YamlFileSaved += OnYamlFileSaved;

            // Add the control to the main content area
            MainContent.Children.Add(yamlEditor);
        }
        private void OnYamlFileLoaded(object sender, string filePath)
        {
            // This is optional - you can handle file loaded events here if needed
            // For example, update status bar or logs
            StatusText.Text = $"Loaded: {filePath}";
        }

        private void OnYamlFileSaved(object sender, string filePath)
        {
            // This is optional - you can handle file saved events here if needed
            StatusText.Text = $"Saved: {filePath}";
        }
    }
}
