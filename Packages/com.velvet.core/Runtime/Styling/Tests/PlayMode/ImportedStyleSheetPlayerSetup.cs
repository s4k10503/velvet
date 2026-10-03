#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace Velvet.Tests
{
    public sealed class ImportedStyleSheetPlayerSetup : IPrebuildSetup, IPostBuildCleanup
    {
        private const string RecordKey = "Velvet.Tests.ImportedStyleSheetPlayerSetup.Record";
        private const string ImportingSheetPath =
            "Packages/com.velvet.core/Runtime/Styling/Tests/Editor/ImportsVelvetUtilities.uss";
        private const string PanelSettingsPath = "Assets/VelvetStarterSample/StarterAppPanelSettings.asset";

        [Serializable]
        private sealed class BuildState
        {
            public string AssetDirectory;
            public ScriptingImplementation Backend;
            public ManagedStrippingLevel Stripping;
            public bool RunInBackground;
            public string Architecture;
            public string[] Preloaded;
        }

        public static void BuildProbe()
        {
            var output = Environment.GetEnvironmentVariable("VELVET_IMPORT_PLAYER_OUTPUT");
            if (string.IsNullOrEmpty(output))
                throw new InvalidOperationException("The imported-sheet probe needs a build output path.");
            var scenes = EditorSceneManager.GetSceneManagerSetup();
            var setup = new ImportedStyleSheetPlayerSetup();
            try
            {
                setup.Setup();
                var state = JsonUtility.FromJson<BuildState>(File.ReadAllText(SessionState.GetString(RecordKey, string.Empty)));
                var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                var probe = new GameObject("Imported stylesheet player probe").AddComponent<ImportedStyleSheetPlayerProbe>();
                probe.Assets = AssetDatabase.LoadAssetAtPath<ImportedStyleSheetPlayerAssets>(
                    state.AssetDirectory + "/ImportedStyleSheetPlayerAssets.asset");
                var scenePath = state.AssetDirectory + "/Probe.unity";
                EditorSceneManager.SaveScene(scene, scenePath);
                var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
                {
                    scenes = new[] { scenePath },
                    locationPathName = Path.GetFullPath(output),
                    target = BuildTarget.StandaloneOSX,
                    options = BuildOptions.IncludeTestAssemblies,
                });
                if (report.summary.result != BuildResult.Succeeded)
                    throw new InvalidOperationException("The imported-sheet player build failed: " + report.summary.result);
            }
            finally
            {
                setup.Cleanup();
                if (scenes.Any(scene => scene.isLoaded && scene.isActive))
                    EditorSceneManager.RestoreSceneManagerSetup(scenes);
                else
                    EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            }
        }

        public void Setup()
        {
            if (SessionState.GetString(RecordKey, string.Empty).Length != 0)
                throw new InvalidOperationException("The previous imported-sheet player setup still needs cleanup.");
            var configured = Environment.GetEnvironmentVariable("VELVET_IMPORT_PLAYER_EVIDENCE");
            var evidence = string.IsNullOrEmpty(configured)
                ? Path.GetFullPath(Path.Combine("Logs", "imported-sheet-player-" + Guid.NewGuid().ToString("N")))
                : Path.GetFullPath(configured);
            Directory.CreateDirectory(evidence);
            var state = new BuildState
            {
                AssetDirectory = "Assets/VelvetImportedSheetPlayer-" + Guid.NewGuid().ToString("N"),
                Backend = PlayerSettings.GetScriptingBackend(NamedBuildTarget.Standalone),
                Stripping = PlayerSettings.GetManagedStrippingLevel(NamedBuildTarget.Standalone),
                RunInBackground = PlayerSettings.runInBackground,
                Architecture = EditorUserBuildSettings.GetPlatformSettings(
                    BuildPipeline.GetBuildTargetName(BuildTarget.StandaloneOSX), "Architecture"),
                Preloaded = PlayerSettings.GetPreloadedAssets().Select(asset => asset == null ? string.Empty
                    : GlobalObjectId.GetGlobalObjectIdSlow(asset).ToString()).ToArray(),
            };
            var record = Path.Combine(evidence, "build-state.json");
            File.WriteAllText(record, JsonUtility.ToJson(state, true));
            SessionState.SetString(RecordKey, record);
            try
            {
                AssetDatabase.CreateFolder("Assets", Path.GetFileName(state.AssetDirectory));
                var carrier = ScriptableObject.CreateInstance<ImportedStyleSheetPlayerAssets>();
                carrier.ImportingSheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(ImportingSheetPath);
                carrier.PanelSettings = Object.Instantiate(AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelSettingsPath));
                if (carrier.ImportingSheet == null || carrier.PanelSettings == null)
                    throw new InvalidOperationException("The importing USS or serialized runtime panel settings is missing.");
                AssetDatabase.CreateAsset(carrier.PanelSettings, state.AssetDirectory + "/PanelSettings.asset");
                PlayerSettings.SetScriptingBackend(NamedBuildTarget.Standalone, ScriptingImplementation.IL2CPP);
                PlayerSettings.SetManagedStrippingLevel(NamedBuildTarget.Standalone, ManagedStrippingLevel.High);
                PlayerSettings.runInBackground = true;
                EditorUserBuildSettings.SetPlatformSettings(
                    BuildPipeline.GetBuildTargetName(BuildTarget.StandaloneOSX), "Architecture", "arm64");
                carrier.ScriptingBackend = PlayerSettings.GetScriptingBackend(NamedBuildTarget.Standalone).ToString();
                carrier.StrippingLevel = PlayerSettings.GetManagedStrippingLevel(NamedBuildTarget.Standalone).ToString();
                AssetDatabase.CreateAsset(carrier, state.AssetDirectory + "/ImportedStyleSheetPlayerAssets.asset");
                PlayerSettings.SetPreloadedAssets(PlayerSettings.GetPreloadedAssets().Concat(new Object[] { carrier }).ToArray());
                AssetDatabase.SaveAssets();
                File.WriteAllText(Path.Combine(evidence, "build-settings.json"),
                    "{\"scriptingBackend\":\"" + carrier.ScriptingBackend + "\",\"strippingLevel\":\"" + carrier.StrippingLevel + "\",\"architecture\":\""
                    + EditorUserBuildSettings.GetPlatformSettings(
                        BuildPipeline.GetBuildTargetName(BuildTarget.StandaloneOSX), "Architecture") + "\"}");
            }
            catch
            {
                Cleanup();
                throw;
            }
        }

        public void Cleanup()
        {
            var record = SessionState.GetString(RecordKey, string.Empty);
            if (record.Length == 0) return;
            var state = JsonUtility.FromJson<BuildState>(File.ReadAllText(record));
            PlayerSettings.SetPreloadedAssets(state.Preloaded.Select(identifier =>
                GlobalObjectId.TryParse(identifier, out var id) ? GlobalObjectId.GlobalObjectIdentifierToObjectSlow(id) : null).ToArray());
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Standalone, state.Backend);
            PlayerSettings.SetManagedStrippingLevel(NamedBuildTarget.Standalone, state.Stripping);
            PlayerSettings.runInBackground = state.RunInBackground;
            EditorUserBuildSettings.SetPlatformSettings(
                BuildPipeline.GetBuildTargetName(BuildTarget.StandaloneOSX), "Architecture", state.Architecture);
            AssetDatabase.DeleteAsset(state.AssetDirectory);
            AssetDatabase.SaveAssets();
            SessionState.EraseString(RecordKey);
        }
    }
}
#endif
