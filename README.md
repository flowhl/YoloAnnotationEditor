# YOLO Annotation Editor
A comprehensive WPF application for **annotating images**, **editing YAML configurations**, **previewing YOLO object detection**, and **OCR dataset management** - the complete toolkit for managing YOLO format datasets and OCR training data. Designed for YOLO v11 Datasets with extensive OCR capabilities.

![image](https://github.com/user-attachments/assets/49975b66-5f5b-4190-b599-1f1444a097aa)
![image](https://github.com/user-attachments/assets/59f38549-7757-459a-8233-4f9182b9d47a)
![image](https://github.com/user-attachments/assets/a110f64c-c802-4ff5-a797-70220d3a056a)

## Features

### Dataset Editor
- **Interactive Annotation Editor**: Create, edit, and delete bounding box annotations with an intuitive interface
- **Dataset Management**: Load and save YOLO format datasets with automatic handling of data files
- **Edit State Tracking**: Visual indicators and state management for tracking which images have been edited
- **Advanced Search**: Filter images by filename or class
- **Statistics & Visualization**: Analyze your dataset with built-in statistics charts and metrics
- **Integrated YOLO Detection**: Generate annotations automatically using loaded ONNX models with support for both CUDA and CPU inference
- **Keyboard Navigation**: Navigate through images and manage annotations with hotkeys

### YAML Editor
- **YAML Configuration Management**: Intuitive interface for managing dataset YAML files and class definitions
- **Path Settings**: Configure base paths and train/validation/test directories
- **Class Management**: Add, remove, and edit class definitions with search and sorting capabilities
- **File Operations**: Create, open, save, and save-as functionality for YAML configurations

### YOLO Preview
- **Live Object Detection**: Real-time YOLO object detection preview using ONNX models
- **Multiple Input Sources**: Support for webcam and screen capture
- **Model Flexibility**: Load custom ONNX models with automatic fallback from CUDA to CPU
- **Detection Control**: Toggle detection on/off and control capture settings

### PaddleOCR
- **Multiple OCR Models**: Support for English V3/V4, Chinese V5, and custom trained models
- **Image Processing**: Load images and run OCR detection with confidence scoring
- **Model Management**: Easy switching between different OCR model versions
- **Results Display**: View OCR results with processing time and confidence metrics

### OCR Annotation
- **Text Annotation Interface**: Dedicated tool for creating and managing text labels for images
- **Automatic OCR Detection**: Generate text annotations using integrated PaddleOCR
- **Dataset Organization**: Automatic file conversion (PNG to JPG) and sequential renaming
- **Progress Tracking**: Visual indicators for labeled vs unlabeled images with progress counters
- **Navigation Controls**: Easy navigation through image datasets with Previous/Next functionality

### OCR Dataset Tools
- **TRDG to PaddleOCR Conversion**: Convert Text Recognition Data Generator datasets to PaddleOCR training format with configurable train/validation splits
- **Dataset Merging**: Merge multiple datasets with flexible scaling options (fixed height, variable height, or no scaling) and quality control
- **Character Set Generation**: Generate character dictionaries from existing datasets with frequency analysis
- **Dataset Analysis**: Comprehensive analysis tools including character frequency, label length statistics, and dictionary comparison with AI-powered recommendations
- **Auto-Discovery**: Automatically find and process datasets in directory structures
- **Batch Processing**: Handle large datasets efficiently with progress tracking and logging

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
3. Configure dataset paths in the Path Settings section
4. Manage class definitions using the dedicated buttons and data grid
5. Search/filter classes and sort by ID or name

### YOLO Preview
1. Navigate to the YOLO Preview tab
2. Select an ONNX model file using the Browse button
3. Choose source type: Webcam or Screen capture
4. Select input device and start/stop capture
5. Toggle YOLO detection on/off and view real-time results

### OCR Annotation
1. Navigate to the OCR Annotation tab
2. Open a folder containing images
3. Use "Detect with OCR" for automatic text recognition
4. Navigate through images and edit text annotations
5. Progress is automatically saved

### OCR Dataset Tools
1. Navigate to the OCR Dataset Tools tab
2. **Convert TRDG to PaddleOCR**: Convert datasets with configurable train/val splits
3. **Merge Datasets**: Combine multiple datasets with flexible scaling options
4. **Generate Character Set**: Create character dictionaries from existing datasets
5. **Analyze Dataset**: Get comprehensive statistics and recommendations for dataset improvement

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
- [Sdcb.PaddleOCR](https://github.com/sdcb/PaddleSharp) - for OCR functionality
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
- [PaddleOCR](https://github.com/PaddlePaddle/PaddleOCR) - for OCR functionality

---

## About
YOLO Annotation Editor was created to simplify the process of creating and maintaining high-quality datasets for computer vision applications, now expanded with comprehensive OCR dataset management capabilities.
For questions or support, please [open an issue](https://github.com/flowhl/YoloAnnotationEditor/issues) on our GitHub repository.
