# YOLO Annotation Editor

A feature-rich WPF application for viewing, editing, and managing YOLO format datasets for machine learning. Designed for Yolo v11 Datasets.

![image](https://github.com/user-attachments/assets/9e0ef580-a20b-4991-ba3a-0bd5572df280)


## Features

- **Interactive Annotation Editor**: Create, edit, and delete bounding box annotations with an intuitive interface
- **Dataset Management**: Load and save YOLO format datasets with automatic handling of data files
- **Edit State Tracking**: Visual indicators and state management for tracking which images have been edited
- **Statistics & Visualization**: Analyze your dataset with built-in statistics charts and metrics
- **Advanced Search**: Filter images by filename or class
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
