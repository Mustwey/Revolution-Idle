// Mod/Features/MultiplierPatcher.cs
#nullable enable
using System;
using System.Collections;
using System.Reflection;
using HarmonyLib;
using MelonLoader;
using UnityEngine;

namespace Mod.Features
{
    internal sealed class MultiplierPatcher : IFeature
    {
        public string Name        => "Unity Multiplier Patcher";
        public string Description => "Force UnityBonuses gains to 1e750 via Calculate or soft tick.";
        public int    Order       => 3;
        public bool   Enabled     { get; set; }

        private static HarmonyLib.Harmony? H = new("Mod.MultiplierPatcher");
        private static Type? _bonusesType;                   // cached Il2Cpp.*UnityBonuses
        private static object? _v1e750;                      // cached BigDouble(1e750)
        private static object? _cachedInstance;              // cached bonuses instance (fallback)
        private static PropertyInfo? _pIP, _pEP, _pET, _pDP; // cached props
        private static bool _softHooked;

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
            _bonusesType = null;
            _v1e750 = null;
            _pIP = _pEP = _pET = _pDP = null;
        }

        public void Update() { }

        // ---------- Init ----------
        private static IEnumerator Init()
        {
            yield return null; yield return null; // let Il2Cpp proxies load

            _bonusesType = TryType("Il2Cpp.UnityBonuses") ?? TryType("UnityBonuses");
            if (_bonusesType != null)
            {
                var calc = _bonusesType.GetMethod("Calculate",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (calc != null && EnsureConstAndPropsFrom(_bonusesType))
                {
                    if (H == null) H = new HarmonyLib.Harmony("Mod.MultiplierPatcher");
                    H!.Patch(calc, prefix: new HarmonyMethod(typeof(MultiplierPatcher), nameof(Prefix)));
                    yield break;
                }
            }

            // Soft fallback: OnUpdate once per frame, but *only* until we cache one instance
            if (!_softHooked)
            {
                MelonEvents.OnUpdate.Subscribe(SoftUpdate);
                _softHooked = true;
            }
        }

        // ---------- Prefix: overwrite & skip ----------
        private static bool Prefix(object __instance)
        {
            try
            {
                if (_v1e750 == null || _pIP == null) EnsureConstAndPropsFrom(__instance.GetType());
                _pIP!.SetValue(__instance, _v1e750);
                _pEP!.SetValue(__instance, _v1e750);
                _pET!.SetValue(__instance, _v1e750);
                _pDP!.SetValue(__instance, _v1e750);
                return false;
            }
            catch { return true; }
        }

        // ---------- Soft update fallback ----------
        private static void SoftUpdate()
        {
            try
            {
                if (_cachedInstance == null)
                {
                    // one light pass to find a bonuses-looking instance; keep it forever
                    foreach (var c in UnityEngine.Object.FindObjectsOfType<Component>())
                    {
                        var t = c.GetType();
                        var inst = t.GetField("currentBonuses", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(c)
                                ?? (LooksLikeBonuses(t) ? c : null);
                        if (inst != null && EnsureConstAndPropsFrom(inst.GetType()))
                        {
                            _cachedInstance = inst;
                            break;
                        }
                    }
                    if (_cachedInstance == null) return; // try again next frame
                }

                // slam cached instance fast (no reflection discovery anymore)
                _pIP!.SetValue(_cachedInstance, _v1e750);
                _pEP!.SetValue(_cachedInstance, _v1e750);
                _pET!.SetValue(_cachedInstance, _v1e750);
                _pDP!.SetValue(_cachedInstance, _v1e750);
            }
            catch { /* keep silent */ }
        }

        // ---------- Helpers ----------
        private static Type? TryType(string shortName)
        {
            // minimal AQN probes; no assembly scans
            foreach (var asm in new[] { "Il2CppAssembly-CSharp", "Assembly-CSharp" })
            {
                var t = Type.GetType($"{shortName}, {asm}", throwOnError: false);
                if (t != null) return t;
            }
            return null;
        }

        private static bool EnsureConstAndPropsFrom(Type bonuses)
        {
            // cache properties once
            if (_pIP == null || _pIP.DeclaringType != bonuses)
            {
                _pIP = bonuses.GetProperty("IPGain",   BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                _pEP = bonuses.GetProperty("EPGain",   BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                _pET = bonuses.GetProperty("eterGain", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic); // lowercase e
                _pDP = bonuses.GetProperty("DPGain",   BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (_pIP == null || _pEP == null || _pET == null || _pDP == null) return false;
            }

            // cache BigDouble(1e750) once using property type
            if (_v1e750 == null)
            {
                var bd = _pIP.PropertyType;
                var tp = bd.GetMethod("TryParse", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(string), bd.MakeByRefType() }, null);
                if (tp == null) return false;
                var box = Activator.CreateInstance(bd);
                var args = new object?[] { "1e750", box };
                if (!(bool)tp.Invoke(null, args)!) return false;
                _v1e750 = args[1];
            }
            return _v1e750 != null;
        }

        private static bool LooksLikeBonuses(Type t)
        {
            // cheap check: 3+ of the 4 property names
            int hit = 0;
            foreach (var n in new[] { "IPGain", "EPGain", "eterGain", "DPGain" })
                if (t.GetProperty(n, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) != null)
                    if (++hit >= 3) return true;
            return false;
        }
    }
}
