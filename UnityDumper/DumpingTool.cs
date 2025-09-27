using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace UnityDumper
{
    public partial class DumpingTool
    {
        // In the main function all operations start
        static async Task Main(string[] args)
        {
            if (args.Length < 2)
            {
                Console.WriteLine("Usage: UnityDumper.exe <InputFolder> <OutputFolder>");
                return;
            }

            string inputFolder = args[0];
            string outputFolder = args[1];
            /* The function that dumps unused scripts runs parallel with the function that dumps
             scene hierarchies. */
            await Task.Run(() =>
            {
                Directory.CreateDirectory(outputFolder);
                Parallel.Invoke(
                    () =>
                    {
                        var guidMap = CollectGuids(inputFolder);
                        var usedGuids = FindReferencedGuids(inputFolder);

                        var unused = DetectUnusedScripts(guidMap, usedGuids);

                        DumpUnusedScripts(outputFolder, unused);
                    },
                    () => DumpSceneHierarchies(inputFolder, outputFolder));
            });
        }

        //This function collects all the guids from the meta files in the "Assets" folder
        static Dictionary<string, string> CollectGuids(string root)
        {
            var map = new Dictionary<string, string>();
            foreach (var file in Directory.EnumerateFiles(root, "*.meta", SearchOption.AllDirectories))
            {
                // This line checks if the file is in the "Assets" folder
                if (!file.Replace("\\", "/").Contains("/Assets/")) continue;
                foreach (var line in File.ReadLines(file))
                {
                    if (line.StartsWith("guid:"))
                    {
                        var guid = line.Split(' ')[1].Trim();
                        map[guid] = file.Replace(".meta", string.Empty);
                    }
                }
            }

            return map;
        }

        // This function finds guids that are referenced in a Unity scene
        static HashSet<string> FindReferencedGuids(string root)
        {
            var regex = MyRegex();
            var used = new HashSet<string>();

            foreach (var file in Directory.EnumerateFiles(Path.Combine(root, "Assets"), "*.*", 
                             SearchOption.AllDirectories).Where(f => f.EndsWith(".unity")))
            {
                foreach (var line in File.ReadLines(file))
                {
                    var match = regex.Match(line);
                    if (match.Success && line.Contains("m_"))
                    {
                        used.Add(match.Groups[1].Value);
                    }
                }
            }
            
            return used;
        }

        //This bool checks if the code contains serialized fields
        static bool HasSerializedFields(string scriptPath)
        {
            try
            {
                var code = File.ReadAllText(scriptPath);
                var tree = CSharpSyntaxTree.ParseText(code);
                var root = tree.GetCompilationUnitRoot();

                var fields = root.DescendantNodes().OfType<FieldDeclarationSyntax>();
                foreach (var field in fields)
                {
                    if (field.AttributeLists.Any(attr =>
                            attr.Attributes.Any(a => a.Name.ToString().Contains("SerializedField"))))
                    {
                        return true;
                    }

                    if (field.Modifiers.Any(m => m.IsKind(SyntaxKind.PublicKeyword)))
                    {
                        return true;
                    }
                }
                return false;
            }
            catch
            {
                return false;
            }
        }

        //This function checks the code for unused scripts
        static List<(string Path, string Guid)> DetectUnusedScripts(Dictionary<string, string> guidMap,
            HashSet<string> used)
        {
            var unused = new List<(string Path, string Guid)>();
            foreach (var kv in guidMap)
            {
                if(!kv.Value.EndsWith(".cs")) continue;
                if(!kv.Value.Replace("\\", "/").Contains("/Assets/")) continue;

                if (!HasSerializedFields(kv.Value) && !used.Contains(kv.Key))
                {
                    string relPath = kv.Value.Replace("\\", "/");
                    int idx = relPath.IndexOf("Assets/", StringComparison.Ordinal);
                    if (idx >= 0)
                    {
                        relPath = relPath.Substring(idx);
                    }
                    unused.Add((relPath, kv.Key));
                }
            }
            
            return unused.OrderBy(x => x.Path).ToList();
        }

        // This function Dumps all the unused scripts
        static void DumpUnusedScripts(string outputFolder, List<(string Path, string Guid)> unused)
        {
            string csvPath = Path.Combine(outputFolder, "UnusedScripts.csv");

            try
            {
                using var writer = new StreamWriter(csvPath);
                writer.WriteLine("Relative Path,GUID");

                foreach (var (path, guid) in unused)
                {
                    writer.WriteLine($"{path},{guid}");
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Unexpected error writing CSV '{csvPath}': {ex.Message}");
            }
        }
        
        // This function dumps the hierarchies of all the scenes in the "Assets" folder
        static void DumpSceneHierarchies(string inputFolder, string outputFolder)
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
        static void DumpSceneHierarchy(string scenePath, string outputPath)
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
        static void WriteObject(StreamWriter writer,
            Dictionary<string, (string Name, List<string> Children, string Parent)> objects, string id, int indent)
        {
            if (!objects.TryGetValue(id, out var obj)) return;
            if (string.IsNullOrEmpty(obj.Name)) return;

            writer.WriteLine(new string('-', indent * 2) + obj.Name);
            foreach (var child in obj.Children)
            {
                WriteObject(writer, objects, child, indent + 1);
            }
        }

        [GeneratedRegex(@"guid: ([a-f0-9]{32})")]
        private static partial Regex MyRegex();
    }
}

