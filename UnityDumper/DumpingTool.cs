using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace UnityDumper
{
    public class DumpingTool
    {
        static void Main(string[] args)
        {
            if (args.Length < 2)
            {
                Console.WriteLine("Usage: UnityDumper.exe <InputFolder> <OutputFolder>");
                return;
            }

            string inputFolder = args[0];
            string outputFolder = args[1];
            Directory.CreateDirectory(outputFolder);
            
            var guidMap = CollectGuids(inputFolder);
            var usedGuids = FindReferencedGuids(inputFolder);
            
            var unused = DetectUnusedScripts(guidMap, usedGuids);
            
            DumpUnusedScripts(outputFolder, unused);
            DumpSceneHierarchies(inputFolder, outputFolder);
        }

        static Dictionary<string, string> CollectGuids(string root)
        {
            var map = new Dictionary<string, string>();
            foreach (var file in Directory.EnumerateFiles(root, "*.meta", SearchOption.AllDirectories))
            {
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

        static HashSet<string> FindReferencedGuids(string root)
        {
            var regex = new Regex(@"guid: ([a-f0-9]{32})");
            var used = new HashSet<string>();

            foreach (var file in Directory.EnumerateFiles(Path.Combine(root, "Assets"), "*.*", 
                             SearchOption.AllDirectories).Where(f => f.EndsWith(".unity")))
            {
                foreach (var line in File.ReadLines(file))
                {
                    var match = regex.Match(line);
                    if (match.Success)
                    {
                        used.Add(match.Groups[1].Value);
                    }
                }
            }
            
            return used;
        }

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

        static List<(string Path, string Guid)> DetectUnusedScripts(Dictionary<string, string> guidMap,
            HashSet<string> used)
        {
            var unused = new List<(string Path, string Guid)>();
            foreach (var kv in guidMap)
            {
                if(!kv.Value.EndsWith(".cs")) continue;
                if(!kv.Value.Replace("\\", "/").Contains("/Assets/")) continue;
                
                /*var code = File.ReadAllText(kv.Value);
                var tree =  CSharpSyntaxTree.ParseText(code);
                var root = tree.GetRoot();
                
                var classes = root.DescendantNodes().OfType<ClassDeclarationSyntax>();
                bool hasMonoBehaviour = false;

                foreach (var cls in classes)
                {
                    var baseTypes =  cls.BaseList?.Types.Select(t => t.ToString()) 
                                     ?? Enumerable.Empty<string>();
                    if (baseTypes.Any(bt => bt.Contains("MonoBehaviour")))
                    {
                        hasMonoBehaviour = true;
                        break;
                    }
                }*/

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

        static void DumpUnusedScripts(string outputFolder, List<(string Path, string Guid)> unused)
        {
            string csvPath = Path.Combine(outputFolder, "UnusedScripts.csv");
            using var writer = new StreamWriter(csvPath);
            writer.WriteLine("Relative Path,GUID");
            foreach (var (path, guid) in unused)
            {
                writer.WriteLine($"{path},{guid}");
            }
        }
        
        static void DumpSceneHierarchies(string inputFolder, string outputFolder)
        {
            string assetsFolder = Path.Combine(inputFolder, "Assets");
            if (!Directory.Exists(assetsFolder)) return;
            
            foreach (var sceneFile in Directory.EnumerateFiles(inputFolder, "*.unity", SearchOption.AllDirectories))
            {
                string sceneName = Path.GetFileNameWithoutExtension(sceneFile);
                string outputPath = Path.Combine(outputFolder, $"{sceneName}.unity.dump");
                DumpSceneHierarchy(sceneFile, outputPath);
            }
        }

        static void DumpSceneHierarchy(string scenePath, string outputPath)
        {
            var objects = new Dictionary<string, (string Name, List<string> Children, string Parent)>();
            string? currentId = null;
            var idDictionary = new Dictionary<string, string?>();

            foreach (var raw in File.ReadLines(scenePath))
            {
                var line = raw.TrimEnd();

                if (line.StartsWith("--- !u!1 &"))
                {
                    currentId = line.Split('&')[1];
                    objects[currentId] = ("", new List<string>(), null)!;
                    continue;
                }
                if (line.StartsWith("--- !u!4 &"))
                {
                    var transformId = line.Split('&')[1];
                    idDictionary[transformId] = currentId;
                    continue;
                }

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
            
            var roots = objects.Keys.Where(id => objects[id].Parent == null).ToList();
            
            using var writer = new StreamWriter(outputPath);

            foreach (var root in roots)
            {
                WriteObject(writer, objects, root, 0);
            }
        }

        static void WriteObject(StreamWriter writer,
            Dictionary<string, (string Name, List<string> Children, string Parent)> objects,
            string id, int indent)
        {
            if (!objects.TryGetValue(id, out var obj)) return;
            if (string.IsNullOrEmpty(obj.Name)) return;

            writer.WriteLine(new string('-', indent * 2) + obj.Name);
            foreach (var child in obj.Children)
            {
                WriteObject(writer, objects, child, indent + 1);
            }
        }
    }
}

