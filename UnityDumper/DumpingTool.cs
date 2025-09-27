namespace UnityDumper
{
    public class DumpingTool
    {
        // In the main function all operations start
        public static void Main(string[] args)
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
            Directory.CreateDirectory(outputFolder);
            Parallel.Invoke(
                () =>
                {
                    var guidMap = GuidCollector.CollectGuids(inputFolder);
                    var usedGuids = GuidCollector.FindReferencedGuids(inputFolder);

                    var unused = ScriptAnalyser.DetectUnusedScripts(guidMap, usedGuids);

                    ScriptAnalyser.DumpUnusedScripts(outputFolder, unused);
                },
                () => SceneAnalyser.DumpSceneHierarchies(inputFolder, outputFolder));
        }
    }
}

