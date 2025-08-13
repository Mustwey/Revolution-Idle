using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using MelonLoader;

namespace Mod.Features
{
    internal sealed class ActkDisabler : IFeature
    {
        public string Name        => "ACTk Disabler";
        public string Description => "Explicitly disables each CodeStage detector (Speed, Time, Wall, Injection, Obscured).";
        public int    Order       => 0;
        public bool   Enabled     { get; set; }

        private const string nSpeed   = "CodeStage.AntiCheat.Detectors.SpeedHackDetector";
        private const string nTime    = "CodeStage.AntiCheat.Detectors.TimeCheatingDetector";
        private const string nWall    = "CodeStage.AntiCheat.Detectors.WallHackDetector";
        private const string nInject  = "CodeStage.AntiCheat.Detectors.InjectionDetector";
        private const string nObsc    = "CodeStage.AntiCheat.Detectors.ObscuredCheatingDetector";

        private static readonly Dictionary<string,string[]> _boot = new()
        {
            [nSpeed]  = new[] { "StartDetection", "StartDetectionInternal", "Awake", "AddToSceneOrGetExisting" },
            [nTime]   = new[] { "StartDetection", "StartDetectionInternal", "AddToSceneOrGetExisting" },
            [nWall]   = new[] { "StartDetection", "StartDetectionInternal", "AddToSceneOrGetExisting" },
            [nInject] = new[] { "StartDetection", "StartDetectionInternal", "AddToSceneOrGetExisting" },
            [nObsc]   = new[] { "StartDetection", "StartDetectionInternal", "AddToSceneOrGetExisting" },
        };

        private static readonly Dictionary<string,string[]> _coroutines = new()
        {
            [nSpeed]  = new[] { "<StartDetectionInternal>d__25", "<SpeedHackCheck>d__26" },
            [nTime]   = new[] { "<CheckForCheat>d__78", "<ForceCheckEnumerator>d__69" },
            [nWall]   = new[] { "<StartDetectionInternal>d__65" },
            [nInject] = new[] { "<StartDetectionInternal>d__55" },
            [nObsc]   = new[] { "<StartDetectionInternal>d__42" },
        };

        private static readonly HarmonyLib.Harmony _H = new("Mod.ActkDisabler");
        private static readonly HashSet<string> _done = new();

        public void Enable()
        {
            if (Enabled) return;
            Enabled = true;

            PatchDomain(AppDomain.CurrentDomain.GetAssemblies());
            AppDomain.CurrentDomain.AssemblyLoad += OnAsmLoad;

            MelonLogger.Msg("[ACTk] Every detector has been surgically disabled.");
        }

        public void Disable()
        {
            Enabled = false;
            AppDomain.CurrentDomain.AssemblyLoad -= OnAsmLoad;
            MelonLogger.Msg("[ACTk] Disabler toggled off – patches stay applied.");
        }

        public void Update()
        {
            if (!Enabled) return;

            foreach (string tName in _boot.Keys)
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
                    MelonLogger.Error($"[ACTk] Update error for {tName}: {ex.Message}");
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
                    if (!_boot.ContainsKey(t.FullName!)) continue;
                    if (!_done.Add(t.FullName!))           continue;

                    try
                    {
                        PatchDetector(t);
                        MelonLogger.Msg($"[ACTk] Patched {t.FullName}");
                    }
                    catch (Exception ex)
                    {
                        MelonLogger.Error($"[ACTk] {t.FullName} patch FAILED: {ex}");
                    }
                }
            }
        }

        private static void PatchDetector(Type det)
        {
            ForceBenignFlags(det);

            foreach (string mName in _boot[det.FullName!])
                foreach (var mi in det.GetMethods(BindingFlags.Instance|BindingFlags.Static|
                                                 BindingFlags.Public |BindingFlags.NonPublic)
                                        .Where(m => m.Name == mName))
                    _H.Patch(mi, prefix: new HarmonyLib.HarmonyMethod(typeof(ActkDisabler), nameof(VoidPrefix)));

            if (_coroutines.TryGetValue(det.FullName!, out var nestNames))
                foreach (var nested in det.GetNestedTypes(BindingFlags.NonPublic))
                {
                    if (!nestNames.Contains(nested.Name)) continue;

                    var moveNext = nested.GetMethod("MoveNext",
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (moveNext != null)
                        _H.Patch(moveNext, prefix: new HarmonyLib.HarmonyMethod(
                            typeof(ActkDisabler), nameof(BoolPrefix)));
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
                        if (f.Name.Contains("running", StringComparison.OrdinalIgnoreCase)) f.SetValue(null, true);
                        if (f.Name.Contains("detected", StringComparison.OrdinalIgnoreCase)) f.SetValue(null, false);
                    }
                    catch (Exception ex)
                    {
                        if (!ex.Message.Contains("Object was garbage collected") && 
                            !ex.Message.Contains("IL2CPP domain"))
                            MelonLogger.Error($"[ACTk] Field access error: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                if (!ex.Message.Contains("Object was garbage collected") && 
                    !ex.Message.Contains("IL2CPP domain"))
                    MelonLogger.Error($"[ACTk] ForceBenignFlags error: {ex.Message}");
            }
        }

        private static void LogAndHandleException(Action logAction, string prefixName)
        {
            try
            {
                logAction();
            }
            catch (Exception ex)
            {
                if (!ex.Message.Contains("Object was garbage collected") && 
                    !ex.Message.Contains("IL2CPP domain"))
                    MelonLogger.Error($"[ACTk-LOG] {prefixName} error: {ex.Message}");
            }
        }

        private static bool VoidPrefix(MethodBase __originalMethod)
        {
            LogAndHandleException(() => MelonLogger.Msg($"[ACTk-LOG] Blocked void call: {__originalMethod.DeclaringType?.Name}.{__originalMethod.Name}"), nameof(VoidPrefix));
            return false;
        }

        private static bool BoolPrefix(MethodBase __originalMethod, ref bool __result)
        {
            LogAndHandleException(() => MelonLogger.Msg($"[ACTk-LOG] Blocked bool call: {__originalMethod.DeclaringType?.Name}.{__originalMethod.Name} -> false"), nameof(BoolPrefix));
            __result = false;
            return false;
        }

        private static bool EnumPrefix(MethodBase __originalMethod, ref IEnumerator __result)
        {
            LogAndHandleException(() => MelonLogger.Msg($"[ACTk-LOG] Blocked enum call: {__originalMethod.DeclaringType?.Name}.{__originalMethod.Name} -> empty"), nameof(EnumPrefix));
            __result = DummyCoroutine();
            return false;
        }

        private static bool GenericSkip(MethodBase __originalMethod)
        {
            LogAndHandleException(() => MelonLogger.Msg($"[ACTk-LOG] Blocked generic call: {__originalMethod.DeclaringType?.Name}.{__originalMethod.Name}"), nameof(GenericSkip));
            return false;
        }

        private static IEnumerator DummyCoroutine()
        {
            yield return null;
        }
    }
}
