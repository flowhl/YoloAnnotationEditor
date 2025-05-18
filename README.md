# YOLO Annotation Editor
A comprehensive WPF application for **annotating images**, **editing YAML configurations**, and **previewing YOLO object detection** - the complete toolkit for managing YOLO format datasets. Designed for Yolo v11 Datasets.
![image](https://github.com/user-attachments/assets/49975b66-5f5b-4190-b599-1f1444a097aa)
![image](https://github.com/user-attachments/assets/1004b75e-fc22-4d74-b437-d0128f000a63)
![image](https://github.com/user-attachments/assets/56d1c424-1e92-46aa-adae-3b7513a81cc3)

## Features
- **Interactive Annotation Editor**: Create, edit, and delete bounding box annotations with an intuitive interface
- **Dataset Management**: Load and save YOLO format datasets with automatic handling of data files
- **Edit State Tracking**: Visual indicators and state management for tracking which images have been edited
- **Statistics & Visualization**: Analyze your dataset with built-in statistics charts and metrics
- **Advanced Search**: Filter images by filename or class
- **YAML Editor**: Intuitive interface for managing dataset YAML files and class definitions
- **YOLO Preview**: Live object detection preview using ONNX models with webcam or screen capture support
- **Auto Updates**: Automatic updates via GitHub releases

## Getting Started

### Installation
Download the latest release from our [GitHub Releases](https://github.com/flowhl/YoloAnnotationEditor/releases) page.
The application uses Velopack for easy installation and automatic updates.

### Requirements
- Windows 10/11
- .NET 8.0 Runtime

## Usage

### Loading a Dataset
1. Launch the application
2. Click "Browse..." to select your YOLO dataset YAML file
3. The application will load all images and annotations from the dataset

### Editing Annotations
1. Select an image from the thumbnail list
2. Toggle "Edit Mode" to enable annotation editing
3. Click and drag on the image to create a new bounding box
4. Select a class from the right panel dropdown
5. Use the delete key or "Delete Selected" button to remove annotations
6. Click "Save Changes" to save your work

### Tracking Edit Progress
- Green indicators show which images have been edited
- Use "Toggle Edit State" to manually mark an image as edited/unedited
- Use "Mark All Edited Until Here" to batch mark multiple images
- View overall edit progress in the Statistics tab

### YAML Editor
1. Navigate to the YAML Editor tab
2. Use the toolbar to create, open, save, or save as YAML files
3. Configure dataset paths in the Path Settings section:
   - Base Path: Root directory for your dataset
   - Train Path: Path to training images
   - Validation Path: Path to validation images
   - Test Path: Path to test images
4. Manage class definitions:
   - Add or remove classes using the dedicated buttons
   - Edit class names directly in the data grid
   - Search/filter classes using the search box
   - Sort classes by ID or name using the radio buttons
5. Changes are tracked and can be saved to the YAML file

### YOLO Preview
1. Navigate to the YOLO Preview tab
2. Configure the detection settings:
   - Select an ONNX model file using the Browse button
   - Choose source type: Webcam or Screen capture
   - Select input device from the dropdown
3. Control the detection:
   - Start/Stop capture using the respective buttons
   - Toggle YOLO detection on/off with the checkbox
4. View real-time detection results directly in the application
5. Detection is performed using the loaded ONNX model and displays bounding boxes with class labels

## Development

### Building from Source
1. Clone the repository
   ```
   git clone https://github.com/flowhl/YoloAnnotationEditor.git
   ```
2. Open the solution in Visual Studio 2022 or later
3. Restore NuGet packages
4. Build the solution

### Dependencies
- .NET 8.0
- WPF (Windows Presentation Foundation)
- [LiveChartsCore](https://github.com/beto-rodriguez/LiveCharts2) - for statistics visualization
- [YamlDotNet](https://github.com/aaubry/YamlDotNet) - for parsing YAML config files
- [OpenCVSharp4](https://github.com/shimat/opencvsharp) - for image manipulation
- [Denxorz.ZoomControl](https://github.com/denxorz/ZoomControl) - for zoom functionality
- [SkiaSharp](https://github.com/mono/SkiaSharp) - for high-performance 2D graphics
- [YoloDotNet](https://github.com/NickSwardh/YoloDotNet) - for YOLO object detection with ONNX models
- [Velopack](https://github.com/velopack/velopack) - for automatic updates

## Contributing
Contributions are welcome! Please feel free to submit a Pull Request.
1. Fork the repository
2. Create your feature branch (`git checkout -b feature/amazing-feature`)
3. Commit your changes (`git commit -m 'Add some amazing feature'`)
4. Push to the branch (`git push origin feature/amazing-feature`)
5. Open a Pull Request

## License
This project is licensed under the Apache License 2.0 - see the [LICENSE](LICENSE) file for details.

### Third-Party Licenses
- [Denxorz.ZoomControl](https://github.com/denxorz/ZoomControl) - Microsoft Public License (MS-PL)
- Other dependencies may have their own licenses - see respective projects for details

## Acknowledgments
- [YOLO](https://github.com/ultralytics/) - for the annotation format
- [OpenCV](https://opencv.org/) - for image processing capabilities

---

## About
YOLO Annotation Editor was created to simplify the process of creating and maintaining high-quality datasets for computer vision applications.
For questions or support, please [open an issue](https://github.com/flowhl/YoloAnnotationEditor/issues) on our GitHub repository.
