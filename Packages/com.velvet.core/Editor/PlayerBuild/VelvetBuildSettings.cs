using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Velvet.Editor
{
    /// <summary>
    /// What a project has opted out of carrying into its player builds: the utility stylesheet's holder and any
    /// of the bundled shaders. Edited under <b>Project Settings ▸ Velvet</b>; <c>Documentation~/player-builds.md</c>
    /// owns what each opt-out costs.
    /// </summary>
    /// <remarks>
    /// Stored under <c>ProjectSettings/</c> rather than in editor preferences, so the choice travels with the
    /// project. A project that never changes one has no file and includes everything.
    /// </remarks>
    [FilePath("ProjectSettings/VelvetBuildSettings.asset", FilePathAttribute.Location.ProjectFolder)]
    internal sealed class VelvetBuildSettings : ScriptableSingleton<VelvetBuildSettings>
    {
        [SerializeField] private bool _excludeStyleSheet;
        [SerializeField] private List<string> _excludedShaders = new();

        internal bool ExcludeStyleSheet
        {
            get => _excludeStyleSheet;
            set => _excludeStyleSheet = value;
        }

        internal bool Excludes(string shaderName) => _excludedShaders.Contains(shaderName);

        internal void SetExcluded(string shaderName, bool excluded)
        {
            _excludedShaders.Remove(shaderName);
            if (excluded)
            {
                _excludedShaders.Add(shaderName);
            }
        }

        internal void Persist() => Save(true);

        [SettingsProvider]
        internal static SettingsProvider CreateProvider() => new("Project/Velvet", SettingsScope.Project)
        {
            label = "Velvet",
            activateHandler = (_, root) => Populate(root),
            keywords = new[] { "Velvet", "stylesheet", "shader", "player build", "Always Included Shaders" },
        };

        private static void Populate(VisualElement root)
        {
            var settings = instance;
            var sheet = new Toggle(VelvetStyleUtilities.RuntimeAssetsPath)
            {
                value = !settings.ExcludeStyleSheet,
                tooltip = "Preload the utility stylesheet's holder in player builds.",
            };
            sheet.RegisterValueChangedCallback(change =>
            {
                settings.ExcludeStyleSheet = !change.newValue;
                settings.Persist();
            });
            root.Add(sheet);

            foreach (var name in VelvetShaders.Names)
            {
                var shader = new Toggle(name)
                {
                    value = !settings.Excludes(name),
                    tooltip = "Add this shader to Always Included Shaders in player builds.",
                };
                shader.RegisterValueChangedCallback(change =>
                {
                    settings.SetExcluded(name, !change.newValue);
                    settings.Persist();
                });
                root.Add(shader);
            }
        }
    }
}
