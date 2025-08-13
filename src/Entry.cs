using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using MelonLoader;
using Mod.Features;
using UnityEngine;

[assembly: MelonInfo(typeof(Mod.Entry), "Mod", "1.0.0", "YourName")]
[assembly: MelonGame(null, null)]

namespace Mod
{
    public sealed class Entry : MelonMod
    {
        private readonly List<IFeature> _features = new();
        private GUIStyle? _title;
        private GUIStyle? _desc;
        private Rect _win = new(10, 10, 320, 240);

        public override void OnInitializeMelon()
        {
            LoadFeatures();
            MelonLogger.Msg($"Loaded {_features.Count} feature module(s).");
        }

        public override void OnUpdate()
        {
            foreach (var f in _features)
                if (f.Enabled) f.Update();
        }

        public override void OnGUI()
        {
            if (_title == null)
            {
                _title = new GUIStyle(GUI.skin.label) { richText = true };
                _desc = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 12,
                    wordWrap = true,
                    normal = { textColor = Color.gray }
                };
            }

            _win.height = Math.Max(160, 60 + _features.Count * 160);

            _win = GUI.Window(333, _win, (GUI.WindowFunction)(id => {
                GUILayout.Label("<b><color=white>Mod Feature Console</color></b>", _title);
                GUILayout.Space(5);

                foreach (var f in _features)
                {
                    bool newOn = GUILayout.Toggle(f.Enabled, f.Name);
                    if (newOn != f.Enabled)
                    {
                        try
                        {
                            if (newOn) f.Enable(); else f.Disable();
                            f.Enabled = newOn;
                            MelonLogger.Msg($"{(newOn ? "Enabled" : "Disabled")}: {f.Name}");
                        }
                        catch (Exception ex)
                        {
                            MelonLogger.Error($"Feature {f.Name} threw: {ex}");
                            f.Enabled = false;
                        }
                    }

                    if (!string.IsNullOrEmpty(f.Description))
                        GUILayout.Label("    " + f.Description, _desc);

                    if (f.Enabled)
                    {
                        var onGuiMethod = f.GetType().GetMethod("OnGUI", BindingFlags.Public | BindingFlags.Instance);
                        if (onGuiMethod != null)
                        {
                            try
                            {
                                onGuiMethod.Invoke(f, null);
                            }
                            catch (Exception ex)
                            {
                                MelonLogger.Error($"Feature {f.Name} OnGUI threw: {ex}");
                            }
                        }
                    }

                    GUILayout.Space(6);
                }

                GUI.DragWindow(new Rect(0, 0, 10000, 20));
            }), "Cheats");
        }

        private void LoadFeatures()
        {
            _features.Clear();
            var types = Assembly.GetExecutingAssembly()
                                .GetTypes()
                                .Where(t => typeof(IFeature).IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract);

            foreach (var t in types)
            {
                try
                {
                    if (Activator.CreateInstance(t) is IFeature f)
                        _features.Add(f);
                }
                catch (Exception ex)
                {
                    MelonLogger.Error($"Failed to instantiate {t.FullName}: {ex}");
                }
            }
            _features.Sort((a, b) => a.Order.CompareTo(b.Order));
        }
    }
}
