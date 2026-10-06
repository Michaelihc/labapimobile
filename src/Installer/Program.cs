using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace LabApiMobile.Installer;

/// <summary>
/// Installs LabAPI-Mobile into a Carl Mod server: copies the framework assemblies into Managed and injects one
/// <c>LabApi.Loader.PluginLoader.Initialize()</c> call into <c>ServerStatic.Awake</c>, the same place the
/// official game calls it. Everything else (events, wrappers) is applied at runtime by LabApi itself.
/// </summary>
internal static class Program
{
    private const string BackupSuffix = ".labapi-original";
    private const string ManifestName = "LabApiMobile.installed.txt";
    private const string LoaderType = "LabApi.Loader.PluginLoader";
    private const string LoaderMethod = "Initialize";
    private static readonly string[] FrameworkFiles = ["LabApi.dll", "0Harmony.dll"];

    /// <summary>Game versions (GameCore.Version.VersionString) this release was tested on.</summary>
    private static readonly string[] TestedVersions = ["0.0.4"];

    private const int ExitOk = 0;
    private const int ExitError = 1;
    private const int ExitUsage = 2;
    private const int ExitUnsupported = 3;

    private static int Main(string[] args)
    {
        List<string> positional = args.Where(a => !a.StartsWith("--", StringComparison.Ordinal)).ToList();
        bool uninstall = args.Contains("--uninstall");
        if (args.Contains("--help") || args.Contains("-h") || args.Contains("/?") || positional.Count is < 1 or > 2
            || args.Any(a => a.StartsWith("--", StringComparison.Ordinal) && a is not "--uninstall"))
        {
            PrintUsage();
            return ExitUsage;
        }

        try
        {
            string serverDir = Path.GetFullPath(positional[0].Trim('"'));
            string managed = Path.Combine(serverDir, "Carl Mod_Data", "Managed");
            string gameAssembly = Path.Combine(managed, "Assembly-CSharp.dll");
            if (!File.Exists(gameAssembly))
            {
                Console.Error.WriteLine($"error: {gameAssembly} not found.");
                Console.Error.WriteLine("Pass the server folder that contains \"Carl Mod.exe\" and \"Carl Mod_Data\".");
                return ExitError;
            }

            if (uninstall)
                return Uninstall(serverDir, managed, gameAssembly);

            string frameworkDir = positional.Count > 1
                ? Path.GetFullPath(positional[1].Trim('"'))
                : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "framework");
            return Install(serverDir, managed, gameAssembly, frameworkDir);
        }
        catch (IOException e)
        {
            Console.Error.WriteLine($"error: {e.Message}");
            Console.Error.WriteLine("If the server is running, stop it and run the installer again.");
            return ExitError;
        }
        catch (UnauthorizedAccessException e)
        {
            Console.Error.WriteLine($"error: {e.Message}");
            Console.Error.WriteLine("Run the installer as a user that can write to the server folder.");
            return ExitError;
        }
        catch (BadImageFormatException e)
        {
            Console.Error.WriteLine($"error: not a valid .NET assembly: {e.FileName ?? e.Message}");
            return ExitError;
        }
        catch (InstallerException e)
        {
            Console.Error.WriteLine($"error: {e.Message}");
            return e.ExitCode;
        }
        catch (Exception e)
        {
            Console.Error.WriteLine($"error: unexpected failure: {e}");
            return ExitError;
        }
    }

    private static void PrintUsage()
    {
        Console.Error.WriteLine("LabAPI-Mobile installer for the Carl Mod SCP:SL server");
        Console.Error.WriteLine();
        Console.Error.WriteLine("usage:");
        Console.Error.WriteLine("  LabApiMobile.Installer <server-dir> [framework-dir]   install or reinstall");
        Console.Error.WriteLine("  LabApiMobile.Installer <server-dir> --uninstall       restore the original game files");
        Console.Error.WriteLine();
        Console.Error.WriteLine("  server-dir     folder containing \"Carl Mod.exe\"; stop the server first");
        Console.Error.WriteLine("  framework-dir  folder with LabApi.dll and 0Harmony.dll (default: the \"framework\" folder");
        Console.Error.WriteLine("                 next to this program)");
    }

    private static int Install(string serverDir, string managed, string gameAssembly, string frameworkDir)
    {
        foreach (string file in FrameworkFiles)
        {
            if (!File.Exists(Path.Combine(frameworkDir, file)))
                throw new InstallerException($"{file} not found in {frameworkDir}.", ExitUsage);
        }

        EnsureWritable(gameAssembly);
        string backup = gameAssembly + BackupSuffix;
        byte[] current = File.ReadAllBytes(gameAssembly);
        GameInfo currentInfo = Inspect(current, managed);

        // Always patch from the untouched original, so reinstalling is idempotent.
        byte[] original;
        bool writeBackup;
        if (File.Exists(backup))
        {
            byte[] saved = File.ReadAllBytes(backup);
            if (currentInfo.IsPatched || current.SequenceEqual(saved))
            {
                original = saved;
                writeBackup = false;
            }
            else
            {
                // An unpatched Assembly-CSharp that differs from the backup: the game files were updated or replaced
                // since the last install. The current file is the new original.
                Console.WriteLine("Assembly-CSharp.dll changed since the last install (game update?); backing up the new file.");
                original = current;
                writeBackup = true;
            }
        }
        else
        {
            if (currentInfo.IsPatched)
            {
                throw new InstallerException(
                    "Assembly-CSharp.dll already contains the LabAPI-Mobile loader, but the backup "
                    + $"{Path.GetFileName(backup)} is missing. Restore the original game files and run the installer again.");
            }

            original = current;
            writeBackup = true;
        }

        GameInfo info = ReferenceEquals(original, current) ? currentInfo : Inspect(original, managed);
        Console.WriteLine($"Server:       {serverDir}");
        Console.WriteLine($"Game version: {info.VersionText}");
        if (info.Problem != null)
        {
            throw new InstallerException(
                $"unsupported Assembly-CSharp.dll: {info.Problem}. This installer supports the Carl Mod server "
                + $"(tested: {string.Join(", ", TestedVersions)}). Nothing was changed.", ExitUnsupported);
        }

        if (!TestedVersions.Contains(info.VersionString))
            Console.WriteLine($"warning: game version {info.VersionString} was not tested (tested: {string.Join(", ", TestedVersions)}).");

        string labApiPath = Path.Combine(frameworkDir, "LabApi.dll");
        byte[] patched;
        string labApiVersion;
        using (DefaultAssemblyResolver resolver = CreateResolver(managed, frameworkDir))
        using (AssemblyDefinition labApi = AssemblyDefinition.ReadAssembly(labApiPath, new ReaderParameters { AssemblyResolver = resolver }))
        using (AssemblyDefinition game = AssemblyDefinition.ReadAssembly(new MemoryStream(original), new ReaderParameters { AssemblyResolver = resolver }))
        {
            labApiVersion = labApi.Name.Version.ToString(3);
            MethodDefinition initialize = labApi.MainModule.GetType(LoaderType)?.Methods
                    .FirstOrDefault(m => m.Name == LoaderMethod && m.IsStatic && m.IsPublic && !m.HasParameters)
                ?? throw new InstallerException($"{LoaderType}.{LoaderMethod}() not found in {labApiPath}.");

            List<string> missing = labApi.MainModule.AssemblyReferences
                .Select(r => r.Name + ".dll")
                .Where(f => !File.Exists(Path.Combine(managed, f)) && !File.Exists(Path.Combine(frameworkDir, f)))
                .ToList();
            if (missing.Count > 0)
            {
                throw new InstallerException(
                    $"this server lacks assemblies LabApi.dll needs: {string.Join(", ", missing)}. Nothing was changed.",
                    ExitUnsupported);
            }

            MethodDefinition awake = FindAwake(game.MainModule)!;
            Instruction anchor = FindAnchor(awake)!;
            ILProcessor il = awake.Body.GetILProcessor();
            il.InsertAfter(anchor, il.Create(OpCodes.Call, game.MainModule.ImportReference(initialize)));

            using MemoryStream output = new();
            game.Write(output);
            patched = output.ToArray();
        }

        // Verify the result before touching the server.
        GameInfo patchedInfo = Inspect(patched, managed);
        if (!patchedInfo.IsPatched || patchedInfo.Problem != null)
            throw new InstallerException("internal error: the patched assembly failed verification. Nothing was changed.");

        List<string> sources = [];
        foreach (string file in FrameworkFiles)
        {
            string source = Path.Combine(frameworkDir, file);
            sources.Add(source);
            string pdb = Path.ChangeExtension(source, ".pdb");
            if (File.Exists(pdb))
                sources.Add(pdb);
        }

        // Fail before changing anything if a file that would be replaced is locked (e.g. by a running server).
        foreach (string source in sources)
        {
            string target = Path.Combine(managed, Path.GetFileName(source));
            if (File.Exists(target))
                EnsureWritable(target);
        }

        if (writeBackup)
            File.WriteAllBytes(backup, original);

        Manifest manifest = Manifest.Read(managed);
        foreach (string source in sources)
            CopyTracked(source, managed, manifest);

        // Rewrites a manifest from an earlier installer (bare file names) in the current format.
        manifest.Save();

        WriteAtomically(gameAssembly, patched);
        if (!Inspect(File.ReadAllBytes(gameAssembly), managed).IsPatched)
            throw new InstallerException($"verification of {gameAssembly} failed after writing it.");

        Console.WriteLine($"Installed LabAPI-Mobile {labApiVersion}: loader call added to ServerStatic.Awake.");
        Console.WriteLine($"Backup of the original: {backup}");
        PrintDataFolder(serverDir);
        return ExitOk;
    }

    private static int Uninstall(string serverDir, string managed, string gameAssembly)
    {
        string backup = gameAssembly + BackupSuffix;
        GameInfo currentInfo = Inspect(File.ReadAllBytes(gameAssembly), managed);
        if (!File.Exists(backup))
        {
            if (currentInfo.IsPatched)
            {
                throw new InstallerException(
                    $"Assembly-CSharp.dll contains the LabAPI-Mobile loader, but the backup {Path.GetFileName(backup)} "
                    + "is missing. Restore the original game files (reinstall the server) to remove LabAPI-Mobile.");
            }

            RemoveInstalledFiles(managed);
            Console.WriteLine("LabAPI-Mobile is not installed on this server; nothing to restore.");
            return ExitOk;
        }

        EnsureWritable(gameAssembly);
        byte[] original = File.ReadAllBytes(backup);
        if (Inspect(original, managed).IsPatched)
            throw new InstallerException($"the backup {backup} contains the LabAPI-Mobile loader; restore the original game files instead.");

        if (currentInfo.IsPatched)
        {
            WriteAtomically(gameAssembly, original);
            Console.WriteLine("Restored the original Assembly-CSharp.dll.");
        }
        else
        {
            Console.WriteLine("Assembly-CSharp.dll does not contain the LabAPI-Mobile loader; left as is.");
        }

        File.Delete(backup);
        RemoveInstalledFiles(managed);
        Console.WriteLine("Uninstalled LabAPI-Mobile. Plugins and configs in the LabAPI-Mobile data folder were kept:");
        PrintDataFolder(serverDir);
        return ExitOk;
    }

    /// <summary>Information about one Assembly-CSharp image.</summary>
    private sealed class GameInfo
    {
        public string VersionString = "unknown";
        public string VersionText = "unknown (GameCore.Version not found)";
        public bool IsPatched;

        /// <summary>Why the image cannot be patched; null when it is a recognised Carl Mod build.</summary>
        public string? Problem;
    }

    private static GameInfo Inspect(byte[] image, string managed)
    {
        GameInfo info = new();
        using DefaultAssemblyResolver resolver = CreateResolver(managed, null);
        using AssemblyDefinition game = AssemblyDefinition.ReadAssembly(new MemoryStream(image), new ReaderParameters { AssemblyResolver = resolver });
        ModuleDefinition module = game.MainModule;

        ReadVersion(module, info);

        MethodDefinition? awake = FindAwake(module);
        if (awake == null)
        {
            info.Problem = "ServerStatic.Awake() not found";
            return info;
        }

        info.IsPatched = awake.Body.Instructions.Any(i =>
            i.OpCode == OpCodes.Call && i.Operand is MethodReference { Name: LoaderMethod } m && m.DeclaringType.FullName == LoaderType);

        Instruction? anchor = FindAnchor(awake);
        if (anchor == null)
            info.Problem = "the SceneManager.sceneLoaded subscription in ServerStatic.Awake() was not found";
        else if (!info.IsPatched && anchor.Next?.OpCode != OpCodes.Ret)
            info.Problem = "ServerStatic.Awake() does not end with the SceneManager.sceneLoaded subscription";
        else if (module.GetType("GameCore.Version") == null)
            info.Problem = "GameCore.Version not found";

        return info;
    }

    private static MethodDefinition? FindAwake(ModuleDefinition module) =>
        module.GetType("ServerStatic")?.Methods.FirstOrDefault(m => m.Name == "Awake" && !m.IsStatic && !m.HasParameters && m.HasBody);

    // Anchor: the scene-loaded subscription that ends the successful startup path of ServerStatic.Awake.
    private static Instruction? FindAnchor(MethodDefinition awake) =>
        awake.Body.Instructions.LastOrDefault(i =>
            i.OpCode == OpCodes.Call && i.Operand is MethodReference { Name: "add_sceneLoaded" } m
            && m.DeclaringType.FullName == "UnityEngine.SceneManagement.SceneManager");

    /// <summary>Reads the constants that GameCore.Version's static constructor assigns.</summary>
    private static void ReadVersion(ModuleDefinition module, GameInfo info)
    {
        TypeDefinition? type = module.GetType("GameCore.Version");
        MethodDefinition? cctor = type?.Methods.FirstOrDefault(m => m.IsConstructor && m.IsStatic && m.HasBody);
        if (type == null || cctor == null)
            return;

        Dictionary<string, object?> values = new();
        object? last = null;
        bool known = false;
        foreach (Instruction i in cctor.Body.Instructions)
        {
            if (i.OpCode == OpCodes.Stsfld && i.Operand is FieldReference field)
            {
                if (known)
                    values[field.Name] = last;
                known = false;
                continue;
            }

            known = TryConstant(i, out last);
        }

        string Get(string name) => values.TryGetValue(name, out object? v) ? v?.ToString() ?? "null" : "?";
        string buildType = Get("BuildType");
        TypeDefinition? buildEnum = type.NestedTypes.FirstOrDefault(t => t.Name == "VersionType");
        FieldDefinition? buildName = buildEnum?.Fields.FirstOrDefault(f => f.HasConstant && f.Constant?.ToString() == buildType);
        if (buildName != null)
            buildType = buildName.Name;

        string backward = values.TryGetValue("BackwardCompatibility", out object? b) && b is int flag
            ? flag != 0 ? $"from revision {Get("BackwardRevision")}" : "off"
            : "?";
        info.VersionString = values.TryGetValue("VersionString", out object? s) && s is string str ? str : "unknown";
        info.VersionText = $"{info.VersionString} (GameCore.Version {Get("Major")}.{Get("Minor")}.{Get("Revision")}, "
            + $"build type {buildType}, backward compatibility {backward})";
    }

    private static bool TryConstant(Instruction i, out object? value)
    {
        value = null;
        OpCode op = i.OpCode;
        if (op == OpCodes.Ldstr) { value = i.Operand; return true; }
        if (op == OpCodes.Ldnull) return true;
        if (op == OpCodes.Ldc_I4_S) { value = (int)(sbyte)i.Operand; return true; }
        if (op == OpCodes.Ldc_I4) { value = (int)i.Operand; return true; }
        if (op == OpCodes.Ldc_I4_M1) { value = -1; return true; }
        if (op.Code >= Code.Ldc_I4_0 && op.Code <= Code.Ldc_I4_8) { value = op.Code - Code.Ldc_I4_0; return true; }
        return false;
    }

    private static DefaultAssemblyResolver CreateResolver(string managed, string? frameworkDir)
    {
        DefaultAssemblyResolver resolver = new();
        foreach (string dir in resolver.GetSearchDirectories())
            resolver.RemoveSearchDirectory(dir);
        resolver.AddSearchDirectory(managed);
        if (frameworkDir != null)
            resolver.AddSearchDirectory(frameworkDir);
        return resolver;
    }

    private static void EnsureWritable(string path)
    {
        try
        {
            using FileStream _ = new(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        }
        catch (IOException)
        {
            throw new InstallerException($"{path} is in use. Stop the server and run the installer again.");
        }
    }

    private static void WriteAtomically(string path, byte[] content)
    {
        string temp = path + ".labapi-tmp";
        File.WriteAllBytes(temp, content);
        File.Copy(temp, path, true);
        File.Delete(temp);
    }

    /// <summary>
    /// Copies a framework file into Managed. A file the installer did not create is first backed up next to it and
    /// restored on uninstall. The manifest entry is saved before the target is touched, so an interrupted install
    /// can be repeated or uninstalled without losing track of any file.
    /// </summary>
    private static void CopyTracked(string source, string managed, Manifest manifest)
    {
        string name = Path.GetFileName(source);
        string target = Path.Combine(managed, name);
        if (!manifest.Owns(name))
        {
            if (File.Exists(target))
            {
                // A backup without a manifest entry is left over from an interrupted install that never replaced the
                // target, so the target is still the original and the backup is refreshed from it.
                File.Copy(target, target + BackupSuffix, true);
                manifest.Set(name, Manifest.Replaced);
                if (!File.ReadAllBytes(target).SequenceEqual(File.ReadAllBytes(source)))
                    Console.WriteLine($"warning: replaced the existing {name}; the original is kept as {name}{BackupSuffix} and restored on uninstall.");
            }
            else
            {
                manifest.Set(name, Manifest.Added);
            }

            manifest.Save();
        }

        File.Copy(source, target, true);
    }

    /// <summary>Deletes the files the installer added and restores the ones it replaced.</summary>
    private static void RemoveInstalledFiles(string managed)
    {
        Manifest manifest = Manifest.Read(managed);
        foreach (KeyValuePair<string, string> entry in manifest.Entries.ToList())
        {
            string path = Path.Combine(managed, entry.Key);
            if (entry.Value == Manifest.Replaced)
            {
                string saved = path + BackupSuffix;
                if (File.Exists(saved))
                {
                    File.Copy(saved, path, true);
                    File.Delete(saved);
                }
                else
                {
                    Console.WriteLine($"warning: the backup of {entry.Key} is missing; left the current {entry.Key} in place.");
                }
            }
            else if (File.Exists(path))
            {
                File.Delete(path);
            }

            // Saved per file, so an interrupted uninstall can be repeated.
            manifest.Remove(entry.Key);
            manifest.Save();
        }

        manifest.Delete();
    }

    /// <summary>
    /// The framework files this installer manages, as <c>added &lt;file&gt;</c> (created by the installer) or
    /// <c>replaced &lt;file&gt;</c> (overwritten after a backup to <c>&lt;file&gt;.labapi-original</c>). A bare file name,
    /// as written by earlier installers, means added.
    /// </summary>
    private sealed class Manifest
    {
        public const string Added = "added";
        public const string Replaced = "replaced";

        private readonly string _path;

        private Manifest(string path) => _path = path;

        public Dictionary<string, string> Entries { get; } = new(StringComparer.OrdinalIgnoreCase);

        public static Manifest Read(string managed)
        {
            Manifest manifest = new(Path.Combine(managed, ManifestName));
            if (!File.Exists(manifest._path))
                return manifest;

            foreach (string raw in File.ReadAllLines(manifest._path))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line[0] == '#')
                    continue;

                string kind = Added;
                string name = line;
                int space = line.IndexOf(' ');
                if (space > 0 && line.Substring(0, space) is Added or Replaced)
                {
                    kind = line.Substring(0, space);
                    name = line.Substring(space + 1).Trim();
                }

                // Only bare file names written by this installer; ignore anything else.
                if (name.Length == 0 || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name != Path.GetFileName(name))
                    continue;
                manifest.Entries[name] = kind;
            }

            return manifest;
        }

        public bool Owns(string name) => Entries.ContainsKey(name);

        public void Set(string name, string kind) => Entries[name] = kind;

        public void Remove(string name) => Entries.Remove(name);

        public void Save()
        {
            List<string> lines = ["# LabAPI-Mobile installer: files to delete (added) or restore from <file>.labapi-original (replaced) on uninstall."];
            lines.AddRange(Entries.OrderBy(e => e.Key, StringComparer.OrdinalIgnoreCase).Select(e => $"{e.Value} {e.Key}"));
            string temp = _path + ".tmp";
            File.WriteAllLines(temp, lines);
            File.Copy(temp, _path, true);
            File.Delete(temp);
        }

        public void Delete()
        {
            if (File.Exists(_path))
                File.Delete(_path);
        }
    }

    private static void PrintDataFolder(string serverDir)
    {
        string policy = Path.Combine(serverDir, "hoster_policy.txt");
        bool gameDir = File.Exists(policy)
            && File.ReadAllLines(policy).Any(l => l.IndexOf("gamedir_for_configs: true", StringComparison.OrdinalIgnoreCase) >= 0);
        string appData = gameDir
            ? Path.Combine(serverDir, "AppData")
            : Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        string data = Path.Combine(appData, "SCP Secret Laboratory", "LabAPI-Mobile");
        Console.WriteLine($"LabAPI-Mobile data folder: {data}");
        Console.WriteLine($"  plugins: {Path.Combine(data, "plugins", "global")} (or plugins\\<port>)");
        if (!gameDir)
            Console.WriteLine("  (hoster_policy.txt with \"gamedir_for_configs: true\" moves it to the server's AppData folder)");
    }

    private sealed class InstallerException(string message, int exitCode = ExitError) : Exception(message)
    {
        public int ExitCode { get; } = exitCode;
    }
}
