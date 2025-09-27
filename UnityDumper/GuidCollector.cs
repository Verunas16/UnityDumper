namespace UnityDumper;
using System.Text.RegularExpressions;

public static partial class GuidCollector
{
    //This function collects all the guids from the meta files in the "Assets" folder
    public static Dictionary<string, string> CollectGuids(string root)
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
    public static HashSet<string> FindReferencedGuids(string root)
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
    [GeneratedRegex(@"guid: ([a-f0-9]{32})")]
    private static partial Regex MyRegex();
}
