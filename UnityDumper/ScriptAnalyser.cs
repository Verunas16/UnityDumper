namespace UnityDumper;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

public static class ScriptAnalyser
{
    //This bool checks if the code contains serialized fields
        public static bool HasSerializedFields(string scriptPath)
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
        public static List<(string Path, string Guid)> DetectUnusedScripts(Dictionary<string, string> guidMap,
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
        public static void DumpUnusedScripts(string outputFolder, List<(string Path, string Guid)> unused)
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
}