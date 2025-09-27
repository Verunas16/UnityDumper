namespace UnityDumper;

public static class SceneAnalyser
{
    // This function dumps the hierarchies of all the scenes in the "Assets" folder
        public static void DumpSceneHierarchies(string inputFolder, string outputFolder)
        {
            string assetsFolder = Path.Combine(inputFolder, "Assets");
            if (!Directory.Exists(assetsFolder)) return;
            
            foreach (var sceneFile in Directory.EnumerateFiles(assetsFolder, "*.unity", SearchOption.AllDirectories))
            {
                string sceneName = Path.GetFileNameWithoutExtension(sceneFile);
                string outputPath = Path.Combine(outputFolder, $"{sceneName}.unity.dump");
                DumpSceneHierarchy(sceneFile, outputPath);
            }
        }

        // This function handles the process of dumping the hierarchy of a scene
        private static void DumpSceneHierarchy(string scenePath, string outputPath)
        {
            var objects = new Dictionary<string, (string Name, List<string> Children, string Parent)>();
            string? currentId = null;
            var idDictionary = new Dictionary<string, string?>();

            // The loop adds all scene objects in the "objects" dictionary
            foreach (var raw in File.ReadLines(scenePath))
            {
                var line = raw.TrimEnd();

                // Assigns id's to each of the scene objects
                if (line.StartsWith("--- !u!1 &"))
                {
                    currentId = line.Split('&')[1];
                    objects[currentId] = ("", new List<string>(), null)!;
                    continue;
                }
                // Connects the id's of GameObjects and the transforms
                if (line.StartsWith("--- !u!4 &"))
                {
                    var transformId = line.Split('&')[1];
                    idDictionary[transformId] = currentId;
                    continue;
                }

                // Defines the name of a game object
                if (currentId != null && line.Contains("m_Name:"))
                {
                    string name = line.Split("m_Name:")[1].Trim().Trim('"');
                    if (name.Length > 0)
                    {
                        objects[currentId] = (name, objects[currentId].Children, objects[currentId].Parent);
                    }
                }
            }
            foreach (var raw in File.ReadLines(scenePath))
            {
                var line = raw.TrimEnd();

                if (line.StartsWith("--- !u!1 &"))
                {
                    currentId = line.Split('&')[1];
                    continue;
                }

                // Determines if a game object has child objects or if it is a child object itself
                if (currentId != null && line.Contains("m_Father: {fileID:"))
                {
                    var parentTransformId = line.Split("fileID:")[1].Trim(' ', '}');
                    var parentId = (parentTransformId != "0") ? idDictionary[parentTransformId] : null;
                    if (parentId != null)
                    {
                        objects[parentId].Children.Add(currentId);
                        objects[currentId] = (objects[currentId].Name, objects[currentId].Children, parentId);
                    }
                }
            }
            
            // The objects that have no parent objects are placed at the roots of the hierarchy
            var roots = objects.Keys.Where(id => objects[id].Parent == null).ToList();

            using var writer = new StreamWriter(outputPath);
            
            foreach (var root in roots)
            {
                WriteObject(writer, objects, root, 0);
            }
        }

        // This function writes the hierarchy of the scene into the text file
        private static void WriteObject(StreamWriter writer,
            Dictionary<string, (string Name, List<string> Children, string Parent)> objects, string id, int indent)
        {
            try
            {
                if (!objects.TryGetValue(id, out var obj)) return;
                if (string.IsNullOrEmpty(obj.Name)) return;

                writer.WriteLine(new string('-', indent * 2) + obj.Name);
                foreach (var child in obj.Children)
                {
                    WriteObject(writer, objects, child, indent + 1);
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Warning: Error writing object hierarchy: {ex.Message}");
            }
            
        }
}