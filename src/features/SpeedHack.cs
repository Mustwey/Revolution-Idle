// ────────────────────────────────────────────────────────────
//  SpeedHack – allows user to set Unity time scale via
//  text input in the mod feature menu. No sliders, no hotkeys.
// ────────────────────────────────────────────────────────────
//  Requirements: MelonLoader, UnityEngine
//  Author:  YourName
//  2025-07-31
// ────────────────────────────────────────────────────────────

using System;
using MelonLoader;
using UnityEngine;

namespace Mod.Features
{
    internal sealed class SpeedHack : IFeature
    {
        public string Name        => "Speed Hack";
        public string Description => "Set Unity time scale to any value. Enter a number and press Apply.";
        public int    Order       => 2;
        public bool   Enabled     { get; set; }

        private string _input = "1.0";
        private float  _lastApplied = 1.0f;
        private bool   _inputError = false;

        public void Enable()
        {
            if (Enabled) return;
            Enabled = true;
            SetTimeScale();
        }

        public void Disable()
        {
            Enabled = false;
            Time.timeScale = 1.0f;
            Time.fixedDeltaTime = 0.02f;
            MelonLogger.Msg("[SpeedHack] Reset to 1.0");
        }

        public void Update() { /* no per-frame logic */ }

        public void OnGUI()
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label("Set Time Scale:", GUILayout.Width(110));
            _input = GUILayout.TextField(_input, GUILayout.Width(60));
            if (GUILayout.Button("Apply", GUILayout.Width(50)))
            {
                SetTimeScale();
            }
            GUILayout.EndHorizontal();
            if (_inputError)
                GUILayout.Label("<color=red>Enter a valid number (0 < x < 100000)</color>", new GUIStyle(GUI.skin.label) { richText = true });
            else
                GUILayout.Label($"Current: {Time.timeScale:F3}");
        }

        private void SetTimeScale()
        {
            if (float.TryParse(_input, out float val) && val > 0f && val < 1000000000000000000f)
            {
                Time.timeScale = val;
                Time.fixedDeltaTime = 0.02f * val;
                _lastApplied = val;
                _inputError = false;
                MelonLogger.Msg($"[SpeedHack] Set to {val}");
            }
            else
            {
                _inputError = true;
            }
        }
    }
}