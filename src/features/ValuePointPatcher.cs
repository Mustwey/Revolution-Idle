// Mod/Features/ValuePointPatcher.cs
#nullable enable
using System;
using System.Collections;
using System.Reflection;
using HarmonyLib;
using MelonLoader;
using UnityEngine;

namespace Mod.Features
{
    internal sealed class ValuePointPatcher : IFeature
    {
        public string Name        => "Value Points Patcher";
        public string Description => "Force MineralsData valuePoints and caps to 1e1000000000 each frame";
        public int    Order       => 4;
        public bool   Enabled     { get; set; }

        private static HarmonyLib.Harmony? H = new("Mod.ValuePointPatcher");
        private static Type? _mineralsType;
        private static object? _vHuge;
        private static object? _cachedInstance;
        private static PropertyInfo? _pVPIncome;
        private static PropertyInfo? _pValuePoints, _pVPMaxTotal, _pVPMaxPolish, _pVPMaxRefine, _pVPRewardMultGain, _pVPRewardAscPower;
        private static bool _softHooked;
        private static bool _displayHooks;

        public void Enable()
        {
            if (Enabled) return;
            Enabled = true;
            MelonCoroutines.Start(Init());
        }

        public void Disable()
        {
            Enabled = false;
            H?.UnpatchSelf();
            H = null;

            _softHooked = false;
            _cachedInstance = null;
            _mineralsType = null;
            _vHuge = null;
            _pVPIncome = null;
            _pValuePoints = _pVPMaxTotal = _pVPMaxPolish = _pVPMaxRefine = _pVPRewardMultGain = _pVPRewardAscPower = null;
        }

        public void Update() { }

        private static IEnumerator Init()
        {
            yield return null; yield return null;

            _mineralsType = TryType("Il2Cpp.MineralsData") ?? TryType("MineralsData");

            // Patch DisplayMinerals getters/update so we write at the exact read point
            var displayType = TryType("Il2Cpp.DisplayMinerals") ?? TryType("DisplayMinerals");
            if (displayType != null && !_displayHooks)
            {
                try
                {
                    if (H == null) H = new HarmonyLib.Harmony("Mod.ValuePointPatcher");

                    var getMinerals = displayType.GetMethod("get_Minerals", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (getMinerals != null)
                        H!.Patch(getMinerals, postfix: new HarmonyMethod(typeof(ValuePointPatcher), nameof(MineralsGetterPostfix)));

                    var mUpdate = displayType.GetMethod("Update", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (mUpdate != null)
                        H!.Patch(mUpdate, prefix: new HarmonyMethod(typeof(ValuePointPatcher), nameof(DisplayUpdatePrefix)));

                    _displayHooks = true;
                }
                catch { /* continue with soft path */ }
            }

            if (!_softHooked)
            {
                MelonEvents.OnLateUpdate.Subscribe(SoftUpdate);
                _softHooked = true;
            }
        }

        private static void SoftUpdate()
        {
            try
            {
                if (_cachedInstance == null)
                {
                    foreach (var c in UnityEngine.Object.FindObjectsOfType<Component>())
                    {
                        var inst = TryGetMineralsFrom(c);
                        if (inst != null && EnsureConstsAndPropsFrom(inst.GetType()))
                        {
                            _cachedInstance = inst;
                            break;
                        }
                    }

                    if (_cachedInstance == null) return;
                }

                // write caps first, then current value, then reward multipliers
                _pVPMaxTotal?.SetValue(_cachedInstance, _vHuge);
                _pVPMaxPolish?.SetValue(_cachedInstance, _vHuge);
                _pVPMaxRefine?.SetValue(_cachedInstance, _vHuge);
                _pValuePoints!.SetValue(_cachedInstance, _vHuge);
                _pVPRewardMultGain?.SetValue(_cachedInstance, _vHuge);
                _pVPRewardAscPower?.SetValue(_cachedInstance, _vHuge);
            }
            catch { }
        }

        // Patch points
        private static void MineralsGetterPostfix(object __result)
        {
            try
            {
                if (__result == null) return;
                if (!EnsureConstsAndPropsFrom(__result.GetType())) return;

                _pVPMaxTotal?.SetValue(__result, _vHuge);
                _pVPMaxPolish?.SetValue(__result, _vHuge);
                _pVPMaxRefine?.SetValue(__result, _vHuge);
                _pValuePoints!.SetValue(__result, _vHuge);
                _pVPRewardMultGain?.SetValue(__result, _vHuge);
                _pVPRewardAscPower?.SetValue(__result, _vHuge);

                _cachedInstance ??= __result;
            }
            catch { }
        }

        private static void DisplayUpdatePrefix(object __instance)
        {
            try
            {
                var t = __instance.GetType();
                var p = t.GetProperty("Minerals", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                var inst = p != null ? p.GetValue(__instance) : null;
                if (inst == null) return;
                if (!EnsureConstsAndPropsFrom(inst.GetType())) return;

                _pVPMaxTotal?.SetValue(inst, _vHuge);
                _pVPMaxPolish?.SetValue(inst, _vHuge);
                _pVPMaxRefine?.SetValue(inst, _vHuge);
                _pValuePoints!.SetValue(inst, _vHuge);
                _pVPRewardMultGain?.SetValue(inst, _vHuge);
                _pVPRewardAscPower?.SetValue(inst, _vHuge);

                _cachedInstance ??= inst;
            }
            catch { }
        }

        private static Type? TryType(string shortName)
        {
            foreach (var asm in new[] { "Il2CppAssembly-CSharp", "Assembly-CSharp" })
            {
                var t = Type.GetType($"{shortName}, {asm}", throwOnError: false);
                if (t != null) return t;
            }
            return null;
        }

        private static bool EnsureConstsAndPropsFrom(Type minerals)
        {
            const BindingFlags BF = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

            _pVPIncome        ??= minerals.GetProperty("VPIncome", BF); // optional
            _pValuePoints     ??= minerals.GetProperty("valuePoints", BF);
            _pVPMaxPolish     ??= minerals.GetProperty("valuePointsMaxPolish", BF);
            _pVPMaxRefine     ??= minerals.GetProperty("valuePointsMaxRefine", BF);
            _pVPMaxTotal      ??= minerals.GetProperty("valuePointsMaxTotal", BF);
            _pVPRewardMultGain??= minerals.GetProperty("VPRewardMultGain", BF);
            _pVPRewardAscPower??= minerals.GetProperty("VPRewardAscPower", BF);

            if (_pValuePoints == null) return false;

            if (_vHuge == null)
            {
                var bd = _pValuePoints.PropertyType; // Il2Cpp.BigDouble
                var tp = bd.GetMethod("TryParse", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(string), bd.MakeByRefType() }, null);
                if (tp == null) return false;
                var box = Activator.CreateInstance(bd);
                var args = new object?[] { "1e1000000000", box };
                if (!(bool)tp.Invoke(null, args)!) return false;
                _vHuge = args[1];
            }
            return true;
        }

        private static object? TryGetMineralsFrom(Component c)
        {
            const BindingFlags BF = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

            // 1) direct field of type MineralsData
            foreach (var f in c.GetType().GetFields(BF))
            {
                if (_mineralsType != null && f.FieldType == _mineralsType)
                {
                    var v = f.GetValue(c);
                    if (v != null && LooksLikeMinerals(v.GetType())) return v;
                }
            }

            // 2) property "Minerals"
            var pMinerals = c.GetType().GetProperty("Minerals", BF);
            if (pMinerals != null)
            {
                var v = pMinerals.GetValue(c);
                if (v != null && LooksLikeMinerals(v.GetType())) return v;
            }

            // 3) method get_Minerals()
            var mGet = c.GetType().GetMethod("get_Minerals", BF, Type.DefaultBinder, Type.EmptyTypes, null);
            if (mGet != null)
            {
                var v = mGet.Invoke(c, null);
                if (v != null && LooksLikeMinerals(v.GetType())) return v;
            }

            // 4) common chains: Data->Minerals, Controller->Minerals
            foreach (var hop in new[] { "Data", "Controller" })
            {
                var pHop = c.GetType().GetProperty(hop, BF);
                if (pHop != null)
                {
                    var mid = pHop.GetValue(c);
                    if (mid != null)
                    {
                        var pm = mid.GetType().GetProperty("Minerals", BF);
                        if (pm != null)
                        {
                            var v = pm.GetValue(mid);
                            if (v != null && LooksLikeMinerals(v.GetType())) return v;
                        }
                        var gm = mid.GetType().GetMethod("get_Minerals", BF, Type.DefaultBinder, Type.EmptyTypes, null);
                        if (gm != null)
                        {
                            var v = gm.Invoke(mid, null);
                            if (v != null && LooksLikeMinerals(v.GetType())) return v;
                        }
                    }
                }
            }

            return null;
        }

        private static bool LooksLikeMinerals(Type t)
        {
            int hit = 0;
            foreach (var n in new[] { "valuePoints", "VPIncome", "valuePointsMaxTotal", "VPRewardMultGain" })
            {
                if (t.GetProperty(n, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) != null)
                {
                    if (++hit >= 2) return true;
                }
            }
            return false;
        }
    }
}


