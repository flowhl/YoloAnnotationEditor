# YOLO Annotation Editor
A comprehensive WPF application serving two equally important workflows: **YOLO object detection dataset management** and **PaddleOCR text recognition dataset management**. Covers the full pipeline from raw images to training-ready data — annotation, dataset manipulation, quality control, and live inference — for both YOLO v11 and PaddleOCR models.

![image](https://github.com/user-attachments/assets/49975b66-5f5b-4190-b599-1f1444a097aa)
![image](https://github.com/user-attachments/assets/59f38549-7757-459a-8233-4f9182b9d47a)
![image](https://github.com/user-attachments/assets/a110f64c-c802-4ff5-a797-70220d3a056a)

## Features

### Dataset Editor
- **Interactive Annotation Editor**: Create, edit, and delete bounding box annotations with click-and-drag; select existing annotations to reclassify or delete them
- **Class Search & Quick Select**: Filter the class dropdown by name or ID while annotating — single matches are auto-selected
- **Dataset Management**: Load YOLO format datasets via YAML file; auto-detects train/val splits and generates empty label files for images missing them
- **Decimal Separator Fix**: On load, detects label files using comma as decimal separator and offers to convert the entire dataset to dot notation
- **Edit State Tracking**: Green indicators per image show which have been reviewed; toggle individual states or batch-mark all images up to the current one
- **Advanced Search**: Filter the image list by filename or class name in real time
- **Statistics & Visualization**: Bar chart of annotation counts per class, plus totals for images, annotations, unique classes, and edit progress
- **YOLO Auto-Annotation**: Load an ONNX model (CUDA with CPU fallback) to detect annotations on the current image, redetect clearing existing ones, or run batch redetection across the entire dataset; auto-triggers on unannotated images when a model is loaded and edit mode is active
- **Batch Label Editor**: Multi-step wizard to select images, draw a spatial region on a reference image, pick a target class, preview the impact, and reclassify all labels whose center falls within the region across the selected images
- **Keyboard Navigation**: Arrow keys to move between images; `Ctrl+E` toggle edit mode; `Ctrl+S` save; `Delete` remove selected annotation; `Ctrl+D` detect / `Ctrl+Shift+D` redetect with YOLO
- **Zoom & Pan**: Zoom control locks during edit mode for precise annotation placement

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

### PaddleOCR Runner
- **Multiple Built-in Models**: English V3, English V4, and Chinese V5 via one-click load
- **Custom Local Models**: Load your own trained PaddleOCR model by pointing to separate detection and recognition model directories (V5 format)
- **Per-region Results**: Displays each detected text region with its confidence score
- **Performance Metrics**: Shows total inference time in milliseconds
- **Image Display**: Renders the loaded image alongside results for quick visual verification

### OCR Annotation
- **Text Annotation Interface**: Dedicated tool for labeling image crops with their text content, producing a `labels.txt` file in PaddleOCR format (`filename text`)
- **Automatic OCR Detection**: One-click OCR on the current image using the loaded PaddleOCR model to pre-fill the text label
- **Dataset Preparation**: On folder load, offers to convert PNG files to JPG and rename all files to sequential numeric names (`0.jpg`, `1.jpg`, …) — the format expected by PaddleOCR training pipelines
- **Auto-save**: Labels are saved automatically on every navigation change and on application close
- **Progress Tracking**: Per-image labeled/unlabeled indicator (green check / red warning) and a running `Labeled: N/Total` counter
- **Navigation**: Previous/Next buttons with current position display

### OCR Dataset Tools
- **TRDG to PaddleOCR Conversion**: Convert Text Recognition Data Generator output (`labels.txt` + images) to PaddleOCR training format (`rec_gt_train.txt` / `rec_gt_val.txt` + split image folders) with a configurable train/val ratio slider
- **Dataset Merging**: Merge any number of PaddleOCR datasets into one; choose image scaling mode — fixed height (aspect-ratio preserved), variable height (random within a min/max range), or no scaling — with configurable JPEG quality and optional random seed; outputs sequentially numbered images and a merged `labels.txt`
- **Auto-Discovery**: Scan a root directory (up to 3 levels deep) to automatically find all folders that contain a `labels.txt` and at least one image, and queue them for merging
- **Character Set Generation**: Scan a dataset's labels to extract every unique character and write a one-character-per-line dictionary file; shows a breakdown of letters, digits, punctuation, and spaces
- **Dataset Analysis**: Total samples, unique characters, avg/min/max label length, per-character frequency table with percentage and dictionary coverage flag, dictionary comparison (characters in dataset missing from dict and vice versa), and training recommendations based on dataset size, character set breadth, and dictionary alignment

### YOLO Dataset Tools
- **Merge Datasets**: Combine multiple YOLO datasets into one, merging class maps and resolving filename conflicts automatically; outputs a ready-to-use `dataset.yaml`
- **Split Datasets**: Split a dataset into N equal parts, into parts of a fixed image count, or extract a random subset — with optional seed for reproducibility; choose which splits (train/val/test) to include
- **Consolidate**: Move all images and labels from any split into train, collapsing the directory structure
- **Train/Val/Test Split**: Redistribute a flat dataset into train/val/test splits with configurable ratios
- **Filter Datasets**: Subset a dataset by class presence (contains / does not contain, AND / OR logic), by first N images, or by random N images; filtered output includes only the relevant classes in the YAML
- **Analyze Dataset**: Per-split and per-class annotation counts, images with no annotations, bounding box size distribution, aspect ratio statistics, and duplicate detection; export results to CSV or a full HTML report
- **Balance Datasets**: Merge a primary and secondary dataset while balancing class representation
- **Validate Dataset**: Check image-label consistency, detect corrupt images, missing label files, and out-of-bounds annotations; export a clean subset of only valid images

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
4. Select a class from the right panel dropdown (or type to search)
5. Click an existing annotation to select and reclassify or delete it
6. Use `Delete` or "Delete Selected" to remove the selected annotation
7. Click "Save Changes" or press `Ctrl+S` to save

### Tracking Edit Progress
- Green indicators show which images have been edited
- Use "Toggle Edit State" to manually mark an image as edited/unedited
- Use "Mark All Edited Until Here" to batch mark multiple images
- View overall edit progress in the Statistics tab

### YOLO Auto-Annotation
1. Browse and load an ONNX model in the toolbar (CUDA with CPU fallback)
2. Enable Edit Mode and navigate to an image
3. Click "Detect with YOLO" to add detections on top of existing annotations, or "Redetect with YOLO" to clear and redetect
4. Use "Batch Redetect" to run detection across all images in the dataset
5. With a model loaded and Edit Mode active, the editor auto-detects on unannotated, unreviewed images

### Batch Label Editor
1. Click "Batch Label Editor" (requires a loaded dataset)
2. Step 1: Select the images to modify (filter, select all, invert)
3. Step 2: Draw a region on a reference image to define where labels will be changed
4. Step 3: Choose the target class for all labels whose center falls inside the region
5. Step 4: Review the summary and apply — results are written directly to label files

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
2. **Convert TRDG to PaddleOCR**: Select the TRDG directory and an output directory, set the train/val split ratio, and convert
3. **Merge Datasets**: Add dataset folders manually or use Auto-Discover; pick a scaling mode and quality; merge into a single numbered dataset
4. **Generate Character Set**: Select a dataset folder and an output `.txt` path; the tool writes one character per line and previews the full set
5. **Analyze Dataset**: Select a dataset folder, optionally load a dictionary file for comparison, and run the analysis to review statistics and recommendations

### YOLO Dataset Tools
1. Navigate to the YOLO Dataset Tools tab
2. **Merge**: Add dataset folders, select an output directory, and merge
3. **Split**: Choose a dataset, pick a split mode (N parts / by count / random subset), and split
4. **Filter**: Load classes from the dataset, select which to include/exclude with AND/OR logic, and filter
5. **Analyze**: Run a full analysis and export results to CSV or HTML
6. **Balance**: Select primary and secondary datasets to produce a class-balanced merge
7. **Validate**: Scan for corrupt images, missing labels, and out-of-bounds annotations; export only valid images

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
