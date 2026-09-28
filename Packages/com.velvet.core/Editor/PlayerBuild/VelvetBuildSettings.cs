using System;
using System.Collections.Generic;
using System.IO;
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
    /// Read from the file on every use rather than held in memory. A held copy outlives a pull that changes the
    /// file, so a build would use the stale choice and the next toggle would write it back over the new one.
    /// </remarks>
    [Serializable]
    internal sealed class VelvetBuildSettings
    {
        internal const string SettingsFile = "ProjectSettings/VelvetBuildSettings.json";

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

        internal static VelvetBuildSettings Read()
            => File.Exists(SettingsFile)
                ? JsonUtility.FromJson<VelvetBuildSettings>(File.ReadAllText(SettingsFile))
                : new VelvetBuildSettings();

        // Applied to what the file holds now, so one toggle changes one choice and leaves the rest as they are on
        // disk.
        internal static void Change(Action<VelvetBuildSettings> change)
        {
            var settings = Read();
            change(settings);
            // MUTANT_SURVIVES(equivalent, literal): pretty-printing changes only whitespace, which Read parses the same.
            File.WriteAllText(SettingsFile, JsonUtility.ToJson(settings, true));
        }

        // A project that leaves the holder out has said the sheet reaches its panels some other way or not at
        // all, which is also what a player built without the holder reads: there the report stays quiet.
        [InitializeOnLoadMethod]
        private static void SilenceMissingSheetReportWhenHolderExcluded()
            => VelvetStyleUtilities.MissingReportSilenced = () => Read().ExcludeStyleSheet;

        [SettingsProvider]
        internal static SettingsProvider CreateProvider() => new("Project/Velvet", SettingsScope.Project)
        {
            label = "Velvet",
            activateHandler = (_, root) => Populate(root),
            keywords = new[] { "Velvet", "stylesheet", "shader", "player build", "Always Included Shaders" },
        };

        private static void Populate(VisualElement root)
        {
            var settings = Read();
            var sheet = new Toggle(VelvetStyleUtilities.RuntimeAssetsPath)
            {
                value = !settings.ExcludeStyleSheet,
                tooltip = "Preload the utility stylesheet's holder in player builds.",
            };
            sheet.RegisterValueChangedCallback(change =>
                Change(current => current.ExcludeStyleSheet = !change.newValue));
            root.Add(sheet);

            foreach (var name in VelvetShaders.Names)
            {
                var shader = new Toggle(name)
                {
                    value = !settings.Excludes(name),
                    tooltip = "Add this shader to Always Included Shaders in player builds.",
                };
                shader.RegisterValueChangedCallback(change =>
                    Change(current => current.SetExcluded(name, !change.newValue)));
                root.Add(shader);
            }
        }
    }
}
