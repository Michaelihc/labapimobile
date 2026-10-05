using Mono.Cecil;
using Mono.Cecil.Cil;

namespace LabApiMobile.Installer;

/// <summary>
/// Installs LabApi into a Carl Mod server: copies the framework assemblies into Managed and injects one
/// <c>LabApi.Loader.PluginLoader.Initialize()</c> call into <c>ServerStatic.Awake</c>, the same place the
/// official game calls it. Everything else (events, wrappers) is applied at runtime by LabApi itself.
/// </summary>
internal static class Program
{
    private const string BackupSuffix = ".labapi-original";
    private const string LoaderType = "LabApi.Loader.PluginLoader";
    private const string LoaderMethod = "Initialize";

    private static int Main(string[] args)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("usage: LabApiMobile.Installer <server-dir> <framework-dir> [--uninstall]");
            Console.Error.WriteLine("  framework-dir must contain LabApi.dll and 0Harmony.dll");
            return 2;
        }

        string managed = Path.Combine(args[0], "Carl Mod_Data", "Managed");
        string gameAssembly = Path.Combine(managed, "Assembly-CSharp.dll");
        string backup = gameAssembly + BackupSuffix;
        if (!File.Exists(gameAssembly))
        {
            Console.Error.WriteLine($"Assembly-CSharp.dll not found under {managed}");
            return 1;
        }

        if (args.Contains("--uninstall"))
        {
            if (File.Exists(backup))
            {
                File.Copy(backup, gameAssembly, true);
                Console.WriteLine("Restored original Assembly-CSharp.dll");
            }

            return 0;
        }

        // Always patch from the untouched original so reinstalling is idempotent.
        if (!File.Exists(backup))
            File.Copy(gameAssembly, backup);

        foreach (string file in new[] { "LabApi.dll", "0Harmony.dll" })
        {
            string source = Path.Combine(args[1], file);
            if (!File.Exists(source))
            {
                Console.Error.WriteLine($"Missing {source}");
                return 1;
            }

            File.Copy(source, Path.Combine(managed, file), true);
            string pdb = Path.ChangeExtension(source, ".pdb");
            if (File.Exists(pdb))
                File.Copy(pdb, Path.Combine(managed, Path.GetFileName(pdb)), true);
        }

        DefaultAssemblyResolver resolver = new();
        resolver.AddSearchDirectory(managed);
        using MemoryStream original = new(File.ReadAllBytes(backup));
        using AssemblyDefinition game = AssemblyDefinition.ReadAssembly(original, new ReaderParameters { AssemblyResolver = resolver });
        using AssemblyDefinition labApi = AssemblyDefinition.ReadAssembly(Path.Combine(managed, "LabApi.dll"), new ReaderParameters { AssemblyResolver = resolver });

        MethodDefinition initialize = labApi.MainModule.GetType(LoaderType)?.Methods.FirstOrDefault(m => m.Name == LoaderMethod && m.IsStatic && !m.HasParameters)
            ?? throw new InvalidOperationException($"{LoaderType}.{LoaderMethod}() not found in LabApi.dll");

        TypeDefinition serverStatic = game.MainModule.GetType("ServerStatic") ?? throw new InvalidOperationException("ServerStatic not found");
        MethodDefinition awake = serverStatic.Methods.FirstOrDefault(m => m.Name == "Awake" && !m.HasParameters)
            ?? throw new InvalidOperationException("ServerStatic.Awake not found");

        // Anchor: the scene-loaded subscription that ends the successful startup path.
        Instruction anchor = awake.Body.Instructions.LastOrDefault(i =>
                i.OpCode == OpCodes.Call && i.Operand is MethodReference { Name: "add_sceneLoaded" })
            ?? throw new InvalidOperationException("SceneManager.sceneLoaded subscription not found in ServerStatic.Awake; unsupported game build");

        ILProcessor il = awake.Body.GetILProcessor();
        il.InsertAfter(anchor, il.Create(OpCodes.Call, game.MainModule.ImportReference(initialize)));

        game.Write(gameAssembly);
        Console.WriteLine($"Installed LabApi bootstrap into {gameAssembly}");
        return 0;
    }
}
