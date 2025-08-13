using System;
using System.IO;
using System.Linq;
using System.Reflection;
using MelonLoader;
using UnityEngine;

namespace Mod.Features
{
    internal sealed class InfoDumper : IFeature
    {
        public string Name        => "Info Dumper";
        public string Description => "Dumps various game information to files for debugging.";
        public int    Order       => 99; // Low priority
        public bool   Enabled     { get; set; }

        public void Enable()
        {
            if (Enabled) return;
            Enabled = true;
        }

        public void Disable()
        {
            Enabled = false;
        }

        public void Update()
        {
            if (!Enabled) return;
        }

        public void OnGUI()
        {
            GUILayout.BeginVertical();
            GUILayout.Label("Select info to dump to files:");

            if (GUILayout.Button("Dump Assemblies & Types"))
            {
                DumpAssembliesAndTypes();
            }

            if (GUILayout.Button("Dump Scene Hierarchy & Components"))
            {
                DumpSceneHierarchy();
            }

            if (GUILayout.Button("Dump Loaded Resources"))
            {
                DumpLoadedResources();
            }

            GUILayout.EndVertical();
        }



        private void DumpSceneHierarchy()
        {
            var lines = new System.Collections.Generic.List<string>();
            var rootObjects = UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects();

            lines.Add("====== Scene Hierarchy ======");
            foreach (var root in rootObjects)
            {
                DumpGameObjectRecursive(root.transform, "", lines);
            }

            WriteDumpFile("SceneHierarchy.txt", lines);
        }

        private void DumpGameObjectRecursive(Transform transform, string indent, System.Collections.Generic.List<string> lines)
        {
            lines.Add($"{indent}[G] {transform.name} (InstanceID: {transform.gameObject.GetInstanceID()})");

            foreach (var component in transform.GetComponents<Component>())
            {
                lines.Add($"{indent}  [C] {component.GetType().FullName}");
            }

            for (int i = 0; i < transform.childCount; i++)
            {
                Transform child = transform.GetChild(i);
                DumpGameObjectRecursive(child, indent + "  ", lines);
            }
        }

        private void DumpAssembliesAndTypes()
        {
            var lines = new System.Collections.Generic.List<string>();
            var assemblies = AppDomain.CurrentDomain.GetAssemblies().OrderBy(a => a.FullName);

            lines.Add("====== Assemblies, Types, and Members ======");
            foreach (var asm in assemblies)
            {
                lines.Add($"\n[Assembly] {asm.FullName}");
                try
                {
                    var types = asm.GetTypes().OrderBy(t => t.FullName);
                    foreach (var type in types)
                    {
                        lines.Add($"  [Type] {type.FullName}");
                        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
                        foreach (var field in type.GetFields(flags).OrderBy(f => f.Name))
                        {
                            lines.Add($"    [F] {field.FieldType.Name} {field.Name}");
                        }
                        foreach (var prop in type.GetProperties(flags).OrderBy(p => p.Name))
                        {
                            lines.Add($"    [P] {prop.PropertyType.Name} {prop.Name}");
                        }
                        foreach (var method in type.GetMethods(flags).OrderBy(m => m.Name))
                        {
                            lines.Add($"    [M] {method.ReturnType.Name} {method.Name}({string.Join(", ", method.GetParameters().Select(p => p.ParameterType.Name))})");
                        }
                    }
                }
                catch (ReflectionTypeLoadException)
                {
                    lines.Add($"  [Could not load types from this assembly.]");
                }
                catch (Exception ex)
                {
                    lines.Add($"  [Error reading assembly: {ex.Message}]");
                }
            }
            WriteDumpFile("Assemblies.txt", lines);
        }

        private void DumpLoadedResources()
        {
            var lines = new System.Collections.Generic.List<string>();
            lines.Add("====== Loaded Resources ======");

            var resources = Resources.FindObjectsOfTypeAll<UnityEngine.Object>().OrderBy(r => r.GetType().FullName).ThenBy(r => r.name);

            foreach (var resource in resources)
            {
                lines.Add($"[{resource.GetType().Name}] {resource.name} (InstanceID: {resource.GetInstanceID()})");
            }

            WriteDumpFile("LoadedResources.txt", lines);
        }



        private void WriteDumpFile(string fileName, System.Collections.Generic.IEnumerable<string> lines)
        {
            const string dumpPath = @"C:\Users\aspec\Downloads\Projects\Games\Revolution-Idle\.old\.data\dump";
            try
            {
                Directory.CreateDirectory(dumpPath);
                string filePath = Path.Combine(dumpPath, fileName);
                File.WriteAllLines(filePath, lines);
                MelonLogger.Msg($"Successfully dumped info to {filePath}");
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"Failed to write dump file {fileName}: {ex.Message}");
            }
        }
    }
}