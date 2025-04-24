using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace YoloAnnotationEditor
{
    public class EditStateManager
    {
        private string _labelInfoPath;
        private HashSet<string> _editedFiles = new HashSet<string>();

        public event EventHandler EditStateChanged;

        public EditStateManager(string yamlFilePath)
        {
            string datasetBasePath = Path.GetDirectoryName(yamlFilePath);
            _labelInfoPath = Path.Combine(datasetBasePath, "labelInfo.xml");
            LoadEditStates();
        }

        public bool IsEdited(string fileName)
        {
            return _editedFiles.Contains(Path.GetFileName(fileName));
        }

        public void MarkAsEdited(string fileName)
        {
            string name = Path.GetFileName(fileName);
            if (_editedFiles.Add(name))
            {
                SaveEditStates();
                OnEditStateChanged();
            }
        }

        public void MarkAsUnedited(string fileName)
        {
            string name = Path.GetFileName(fileName);
            if (_editedFiles.Remove(name))
            {
                SaveEditStates();
                OnEditStateChanged();
            }
        }

        public void ToggleEditState(string fileName)
        {
            string name = Path.GetFileName(fileName);
            if (_editedFiles.Contains(name))
            {
                _editedFiles.Remove(name);
            }
            else
            {
                _editedFiles.Add(name);
            }
            SaveEditStates();
            OnEditStateChanged();
        }

        public void MarkAllEditedUntil(string fileName, IEnumerable<string> allFiles)
        {
            // Get all files up to and including the specified file
            bool changed = false;
            string targetName = Path.GetFileName(fileName);

            foreach (var file in allFiles)
            {
                string currentName = Path.GetFileName(file);
                if (_editedFiles.Add(currentName))
                {
                    changed = true;
                }

                if (currentName == targetName)
                {
                    break;
                }
            }

            if (changed)
            {
                SaveEditStates();
                OnEditStateChanged();
            }
        }

        private void LoadEditStates()
        {
            _editedFiles.Clear();

            if (File.Exists(_labelInfoPath))
            {
                try
                {
                    XDocument doc = XDocument.Load(_labelInfoPath);
                    var fileElements = doc.Root.Elements("file");

                    foreach (var element in fileElements)
                    {
                        string fileName = element.Attribute("name")?.Value;
                        if (!string.IsNullOrEmpty(fileName))
                        {
                            _editedFiles.Add(fileName);
                        }
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error loading edit states: {ex.Message}");
                }
            }
        }

        private void SaveEditStates()
        {
            try
            {
                XDocument doc = new XDocument(
                    new XElement("EditedFiles",
                        from fileName in _editedFiles
                        select new XElement("file", new XAttribute("name", fileName))
                    )
                );

                doc.Save(_labelInfoPath);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error saving edit states: {ex.Message}");
            }
        }

        protected virtual void OnEditStateChanged()
        {
            EditStateChanged?.Invoke(this, EventArgs.Empty);
        }
    }

}
