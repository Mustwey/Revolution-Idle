using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using MelonLoader;
using System.Threading.Tasks;

namespace Mod.Features
{
    internal sealed class AcDisabler : IFeature
    {
        public string Name        => "Custom Anti-Cheat Disabler";
        public string Description => "Disables game's native anti-cheat (Anticheat class + GameController time validation)";
        public int    Order       => 1;
        public bool   Enabled     { get; set; }

        private const string nAnticheat = "Il2Cpp.Anticheat";
        private const string nGameController = "Il2Cpp.GameController";

        private static readonly Dictionary<string,string[]> _anticheatMethods = new()
        {
            [nAnticheat] = new[] { "PreventSpeedHack", "ObscuredCheatingDetected", "BanPlayer", "Start", "Awake" }
        };

        private static readonly Dictionary<string,string[]> _gameControllerMethods = new()
        {
            [nGameController] = new[] { "FetchServerTime" }
        };

        private static readonly string[] _timeProperties = {
            "get_gameSpeed", "set_gameSpeed", "get_devSpeed", "set_devSpeed",
            "get_minutesSpend", "set_minutesSpend", "get_spendTime", "set_spendTime",
            "get_lastFetchServerTime", "set_lastFetchServerTime"
        };

        private static readonly Dictionary<string,string[]> _coroutines = new()
        {
            [nGameController] = new[] { "<FetchServerTime>d__192" }
        };

        private static readonly HarmonyLib.Harmony _H = new("Mod.AcDisabler");
        private static readonly HashSet<string> _done = new();

        public void Enable()
        {
            if (Enabled) return;
            Enabled = true;
            PatchDomain(AppDomain.CurrentDomain.GetAssemblies());
            AppDomain.CurrentDomain.AssemblyLoad += OnAsmLoad;
            MelonLogger.Msg("[Custom AC] Native anti-cheat mechanisms disabled.");
        }

        public void Disable()
        {
            Enabled = false;
            AppDomain.CurrentDomain.AssemblyLoad -= OnAsmLoad;
            MelonLogger.Msg("[Custom AC] Disabler toggled off – patches remain applied.");
        }

        public void Update()
        {
            if (!Enabled) return;
            
            foreach (string tName in new[] { nAnticheat, nGameController })
            {
                try
                {
                    var t = Type.GetType(tName);
                    if (t != null)
                        ForceBenignFlags(t);
                }
                catch (Exception ex)
                {
                    if (ex.Message.Contains("Object was garbage collected") || 
                        ex.Message.Contains("IL2CPP domain"))
                        continue;
                    MelonLogger.Error($"[Custom AC] Update error for {tName}: {ex.Message}");
                }
            }
        }

        private static void OnAsmLoad(object? _, AssemblyLoadEventArgs e)
            => PatchDomain(new[] { e.LoadedAssembly });

        private static void PatchDomain(IEnumerable<Assembly> asms)
        {
            foreach (var asm in asms)
            {
                IEnumerable<Type> types;
                try { types = asm.GetTypes(); } catch { continue; }

                foreach (var t in types)
                {
                    string typeName = t.FullName!;
                    
                    if (_anticheatMethods.ContainsKey(typeName) && _done.Add(typeName))
                    {
                        try
                        {
                            PatchAnticheatClass(t);
                            MelonLogger.Msg($"[Custom AC] Patched {typeName}");
                        }
                        catch (Exception ex)
                        {
                            MelonLogger.Error($"[Custom AC] {typeName} patch FAILED: {ex}");
                        }
                    }
                    
                    if (_gameControllerMethods.ContainsKey(typeName) && _done.Add(typeName))
                    {
                        try
                        {
                            PatchGameControllerClass(t);
                            MelonLogger.Msg($"[Custom AC] Patched {typeName}");
                        }
                        catch (Exception ex)
                        {
                            MelonLogger.Error($"[Custom AC] {typeName} patch FAILED: {ex}");
                        }
                    }
                }
            }
        }

        private static void PatchAnticheatClass(Type anticlass)
        {
            ForceBenignFlags(anticlass);

            foreach (string methodName in _anticheatMethods[anticlass.FullName!])
            {
                var method = anticlass.GetMethod(methodName,
                    BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                if (method != null)
                    _H.Patch(method, prefix: new HarmonyMethod(typeof(AcDisabler), nameof(VoidPrefix)));
            }

            if (_coroutines.TryGetValue(anticlass.FullName!, out var coroutines))
                PatchCoroutines(anticlass, coroutines);
        }

        private static void PatchGameControllerClass(Type gcClass)
        {
            ForceBenignFlags(gcClass);

            foreach (string methodName in _gameControllerMethods[gcClass.FullName!])
            {
                var method = gcClass.GetMethod(methodName,
                    BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                if (method != null)
                    _H.Patch(method, prefix: new HarmonyLib.HarmonyMethod(typeof(AcDisabler), nameof(ReturnTrueTask)));
            }

            if (_coroutines.TryGetValue(gcClass.FullName!, out var coroutines))
                PatchCoroutines(gcClass, coroutines);
        }

        private static void PatchCoroutines(Type targetClass, string[] coroutineNames)
        {
            foreach (var nested in targetClass.GetNestedTypes(BindingFlags.NonPublic))
            {
                if (!coroutineNames.Contains(nested.Name)) continue;

                var moveNext = nested.GetMethod("MoveNext",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (moveNext != null)
                    _H.Patch(moveNext, prefix: new HarmonyMethod(typeof(AcDisabler), nameof(BoolFalse)));
            }
        }

        private static void ForceBenignFlags(Type t)
        {
            try
            {
                foreach (var f in t.GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
                {
                    if (f.FieldType != typeof(bool)) continue;
                    try
                    {
                        if (f.Name.Contains("cheat", StringComparison.OrdinalIgnoreCase)) f.SetValue(null, false);
                        if (f.Name.Contains("detect", StringComparison.OrdinalIgnoreCase)) f.SetValue(null, false);
                        if (f.Name.Contains("speed", StringComparison.OrdinalIgnoreCase) && f.Name.Contains("hack", StringComparison.OrdinalIgnoreCase)) f.SetValue(null, false);
                        if (f.Name.Contains("ban", StringComparison.OrdinalIgnoreCase)) f.SetValue(null, false);
                        if (f.Name.Contains("running", StringComparison.OrdinalIgnoreCase)) f.SetValue(null, true);
                        else if (f.Name.Contains("detected", StringComparison.OrdinalIgnoreCase)) f.SetValue(null, false);
                    }
                    catch (Exception ex)
                    {
                        if (!ex.Message.Contains("Object was garbage collected") && 
                            !ex.Message.Contains("IL2CPP domain"))
                            MelonLogger.Error($"[Custom AC] Field access error: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                if (!ex.Message.Contains("Object was garbage collected") && 
                    !ex.Message.Contains("IL2CPP domain"))
                    MelonLogger.Error($"[Custom AC] ForceBenignFlags error: {ex.Message}");
            }
        }

        private static bool VoidPrefix(MethodBase __originalMethod)
        {
            string methodName = "Unknown";
            string declaringTypeName = "Unknown";
            try
            {
                declaringTypeName = __originalMethod.DeclaringType?.Name ?? "Unknown";
                methodName = __originalMethod.Name ?? "Unknown";
            }
            catch (Il2CppInterop.Runtime.ObjectCollectedException)
            {
                // Ignore, names will remain "Unknown"
            }
            MelonLogger.Msg($"[AC-LOG] Blocked void call: {declaringTypeName}.{methodName}");
            return false;
        }

        private static bool BoolPrefix(ref bool __result, MethodBase __originalMethod)
        {
            string methodName = "Unknown";
            string declaringTypeName = "Unknown";
            try
            {
                declaringTypeName = __originalMethod.DeclaringType?.Name ?? "Unknown";
                methodName = __originalMethod.Name ?? "Unknown";
            }
            catch (Il2CppInterop.Runtime.ObjectCollectedException)
            {
                // Ignore, names will remain "Unknown"
            }
            MelonLogger.Msg($"[AC-LOG] Blocked bool call: {declaringTypeName}.{methodName} -> false");
            __result = false;
            return false;
        }

        private static bool EnumPrefix(ref IEnumerator __result, MethodBase __originalMethod)
        {
            string methodName = "Unknown";
            string declaringTypeName = "Unknown";
            try
            {
                declaringTypeName = __originalMethod.DeclaringType?.Name ?? "Unknown";
                methodName = __originalMethod.Name ?? "Unknown";
            }
            catch (Il2CppInterop.Runtime.ObjectCollectedException)
            {
                // Ignore, names will remain "Unknown"
            }
            MelonLogger.Msg($"[AC-LOG] Blocked enum call: {declaringTypeName}.{methodName} -> empty");
            __result = DummyCoroutine();
            return false;
        }

        private static bool GenericPrefix(MethodBase __originalMethod)
        {
            string methodName = "Unknown";
            string declaringTypeName = "Unknown";
            try
            {
                declaringTypeName = __originalMethod.DeclaringType?.Name ?? "Unknown";
                methodName = __originalMethod.Name ?? "Unknown";
            }
            catch (Il2CppInterop.Runtime.ObjectCollectedException)
            {
                // Ignore, names will remain "Unknown"
            }
            MelonLogger.Msg($"[AC-LOG] Blocked generic call: {declaringTypeName}.{methodName}");
            return false;
        }

        private static bool ReturnSafeValue(ref object __result, MethodBase __originalMethod)
        {
            string methodName = "Unknown";
            string declaringTypeName = "Unknown";
            try
            {
                declaringTypeName = __originalMethod.DeclaringType?.Name ?? "Unknown";
                methodName = __originalMethod.Name ?? "Unknown";
            }
            catch (Il2CppInterop.Runtime.ObjectCollectedException)
            {
                // Ignore, names will remain "Unknown"
            }
            MelonLogger.Msg($"[AC-LOG] Safe value for: {declaringTypeName}.{methodName} -> 1.0f");
            __result = 1.0f;
            return false;
        }

        private static bool ReturnTrueTask(ref object __result, MethodBase __originalMethod)
        {
            string methodName = "Unknown";
            string declaringTypeName = "Unknown";
            try
            {
                declaringTypeName = __originalMethod.DeclaringType?.Name ?? "Unknown";
                methodName = __originalMethod.Name ?? "Unknown";
            }
            catch (Il2CppInterop.Runtime.ObjectCollectedException)
            {
                // Ignore, names will remain "Unknown"
            }
            MelonLogger.Msg($"[AC-LOG] Task result for: {declaringTypeName}.{methodName} -> true");
            __result = Task.FromResult(true);
            return false;
        }

        private static bool BoolFalse(ref bool __result, MethodBase __originalMethod)
        {
            string methodName = "Unknown";
            string declaringTypeName = "Unknown";
            try
            {
                declaringTypeName = __originalMethod.DeclaringType?.Name ?? "Unknown";
                methodName = __originalMethod.Name ?? "Unknown";
            }
            catch (Il2CppInterop.Runtime.ObjectCollectedException)
            {
                // Ignore, names will remain "Unknown"
            }
            MelonLogger.Msg($"[AC-LOG] Enum move next: {declaringTypeName}.{methodName} -> false");
            __result = false;
            return false;
        }

        private static IEnumerator DummyCoroutine()
        {
            yield return null;
        }
    }
}