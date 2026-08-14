// Bundled by codely-unity-lsp: temporary Editor script to generate .sln via Visual Studio package.
// Class/file name must match (Unity). Placed under Assets/Editor/, then removed after batchmode.
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

public static class CodelyLspTmp__GenSln_9f3a2b81
{
    public static void Run()
    {
        Log("Begin");

        try
        {
            AssetDatabase.Refresh();

            var projectRoot = Directory.GetParent(Application.dataPath)?.FullName;
            if (string.IsNullOrEmpty(projectRoot))
            {
                LogError("Cannot resolve project root from Application.dataPath.");
                return;
            }

            Log("Project root: " + projectRoot);
            Log("Application.dataPath: " + Application.dataPath);

            var beforeSln = Directory.GetFiles(projectRoot, "*.sln", SearchOption.TopDirectoryOnly);
            var beforeCsproj = Directory.GetFiles(projectRoot, "*.csproj", SearchOption.TopDirectoryOnly);

            Log("Before generation: sln=" + beforeSln.Length + ", csproj=" + beforeCsproj.Length);

            var loadedAssemblies = AppDomain.CurrentDomain.GetAssemblies();
            var vsAssembly = loadedAssemblies.FirstOrDefault(a =>
                a.GetName().Name == "Microsoft.Unity.VisualStudio.Editor" ||
                a.GetType("Microsoft.Unity.VisualStudio.Editor.ProjectGeneration", false) != null ||
                a.GetType("Microsoft.Unity.VisualStudio.Editor.LegacyStyleProjectGeneration", false) != null);

            if (vsAssembly == null)
            {
                LogError("Cannot find Microsoft.Unity.VisualStudio.Editor assembly. Ensure com.unity.ide.visualstudio is installed and compiled.");
                DumpLoadedAssemblies(loadedAssemblies);
                return;
            }

            Log("VS assembly: " + vsAssembly.FullName);

            var generatorType =
                vsAssembly.GetType("Microsoft.Unity.VisualStudio.Editor.LegacyStyleProjectGeneration", false) ??
                vsAssembly.GetType("Microsoft.Unity.VisualStudio.Editor.ProjectGeneration", false);

            if (generatorType == null)
            {
                LogError("Cannot find LegacyStyleProjectGeneration or ProjectGeneration type.");
                return;
            }

            Log("Generator type: " + generatorType.FullName);

            object generator;
            try
            {
                generator = Activator.CreateInstance(generatorType, nonPublic: true);
            }
            catch (Exception ex)
            {
                LogError("Failed to create generator: " + ex);
                return;
            }

            if (generator == null)
            {
                LogError("Generator instance is null.");
                return;
            }

            var syncMethod = generatorType.GetMethod("Sync", BindingFlags.Instance | BindingFlags.Public);
            var solutionFileMethod = generatorType.GetMethod("SolutionFile", BindingFlags.Instance | BindingFlags.Public);
            var projectDirectoryProperty = generatorType.GetProperty("ProjectDirectory", BindingFlags.Instance | BindingFlags.Public);

            Log("Has Sync(): " + (syncMethod != null));
            Log("Has SolutionFile(): " + (solutionFileMethod != null));
            Log("Has ProjectDirectory: " + (projectDirectoryProperty != null));

            if (projectDirectoryProperty != null)
            {
                try
                {
                    var projectDirectory = projectDirectoryProperty.GetValue(generator, null) as string;
                    Log("Generator.ProjectDirectory: " + projectDirectory);
                }
                catch (Exception ex)
                {
                    Log("Read ProjectDirectory failed: " + ex.Message);
                }
            }

            if (solutionFileMethod != null)
            {
                try
                {
                    var solutionPathBefore = solutionFileMethod.Invoke(generator, null) as string;
                    Log("Solution path before Sync(): " + solutionPathBefore);
                    Log("Solution exists before Sync(): " + (!string.IsNullOrEmpty(solutionPathBefore) && File.Exists(solutionPathBefore)));
                }
                catch (Exception ex)
                {
                    Log("Read SolutionFile before Sync() failed: " + ex.Message);
                }
            }

            if (syncMethod == null)
            {
                LogError("Sync() not found.");
                return;
            }

            Log("Invoking Sync() ...");
            syncMethod.Invoke(generator, null);
            Log("Sync() finished.");

            string solutionPathAfter = null;
            if (solutionFileMethod != null)
            {
                try
                {
                    solutionPathAfter = solutionFileMethod.Invoke(generator, null) as string;
                }
                catch (Exception ex)
                {
                    Log("Read SolutionFile after Sync() failed: " + ex.Message);
                }
            }

            var afterSln = Directory.GetFiles(projectRoot, "*.sln", SearchOption.TopDirectoryOnly);
            var afterCsproj = Directory.GetFiles(projectRoot, "*.csproj", SearchOption.TopDirectoryOnly);
            var afterSlnx = Directory.GetFiles(projectRoot, "*.slnx", SearchOption.TopDirectoryOnly);

            Log("After generation: sln=" + afterSln.Length + ", csproj=" + afterCsproj.Length + ", slnx=" + afterSlnx.Length);
            Log("Solution path after Sync(): " + (solutionPathAfter ?? "<null>"));
            Log("Solution exists after Sync(): " + (!string.IsNullOrEmpty(solutionPathAfter) && File.Exists(solutionPathAfter)));

            foreach (var sln in afterSln.OrderBy(Path.GetFileName))
                Log("sln: " + sln);

            foreach (var csproj in afterCsproj.OrderBy(Path.GetFileName))
                Log("csproj: " + csproj);

            foreach (var slnx in afterSlnx.OrderBy(Path.GetFileName))
                Log("slnx: " + slnx);

            AssetDatabase.Refresh();
            Log("End");
        }
        catch (TargetInvocationException ex)
        {
            LogError("Invocation failed: " + ex);
            if (ex.InnerException != null)
                LogError("Inner exception: " + ex.InnerException);
        }
        catch (Exception ex)
        {
            LogError("Failed: " + ex);
        }
    }

    private static void DumpLoadedAssemblies(Assembly[] assemblies)
    {
        Log("Loaded assemblies snapshot:");
        foreach (var assembly in assemblies.OrderBy(a => a.GetName().Name))
        {
            var name = assembly.GetName().Name;
            if (name.IndexOf("VisualStudio", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("CodeEditor", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("Unity.", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                Log("  asm: " + assembly.FullName);
            }
        }
    }

    private static void Log(string message)
    {
        Debug.Log("[CodelyLspTmp__GenSln_9f3a2b81] " + message);
    }

    private static void LogError(string message)
    {
        Debug.LogError("[CodelyLspTmp__GenSln_9f3a2b81] " + message);
    }
}
