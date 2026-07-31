#if UNITY_EDITOR
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;
using Velvet.Samples.StarterApp;

namespace Velvet.EditorTools
{
    /// <summary>
    /// Authors the starter sample's binary-ish assets — its theme, its <see cref="PanelSettings"/> and its
    /// scene — and registers the scene in the build settings so a player build carries it. Written as a
    /// script for the same reason as <see cref="URPSetup"/>: the scene and the panel reference each other
    /// and the host script by GUID, and hand-editing those is error-prone.
    /// <para>
    /// Run headless via <c>-executeMethod Velvet.EditorTools.StarterSampleSetup.CreateAssets</c>. Re-run it
    /// against a checkout that already has the assets and check the result before committing: the scene and
    /// the shipped copy both address the theme, the panel and the host script by GUID, so a regeneration
    /// that does not reuse them in place is a silently broken sample rather than a failing build.
    /// </para>
    /// </summary>
    public static class StarterSampleSetup
    {
        private const string SampleDir = "Assets/VelvetStarterSample";
        private const string ThemePath = SampleDir + "/StarterAppTheme.tss";
        private const string PanelPath = SampleDir + "/StarterAppPanelSettings.asset";
        private const string ScenePath = SampleDir + "/StarterApp.unity";

        public static void CreateAssets()
        {
            EnsureTheme();
            var panel = EnsurePanelSettings();
            CreateScene(panel);
            EnsureInBuildSettings();
            AssetDatabase.SaveAssets();
        }

        private static void EnsureTheme()
        {
            if (File.Exists(ThemePath))
            {
                return;
            }

            // The one-line body Unity itself writes for a project's default runtime theme. Shipping a copy
            // inside the sample is what lets the PanelSettings reference a theme that travels with it.
            File.WriteAllText(ThemePath, "@import url(\"unity-theme://default\");\n");
            AssetDatabase.ImportAsset(ThemePath, ImportAssetOptions.ForceSynchronousImport);
        }

        private static PanelSettings EnsurePanelSettings()
        {
            var panel = AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelPath);
            if (panel == null)
            {
                panel = ScriptableObject.CreateInstance<PanelSettings>();
                AssetDatabase.CreateAsset(panel, PanelPath);
            }

            panel.themeStyleSheet = AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>(ThemePath);
            panel.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            panel.referenceResolution = new Vector2Int(1280, 720);
            panel.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
            panel.match = 0.5f;
            EditorUtility.SetDirty(panel);
            return panel;
        }

        private static void CreateScene(PanelSettings panel)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // URP renders nothing without a camera, so the Game view would be empty even though the panel
            // itself draws in the overlay pass.
            var camera = new GameObject("Main Camera", typeof(Camera)) { tag = "MainCamera" };
            camera.GetComponent<Camera>().clearFlags = CameraClearFlags.SolidColor;
            camera.GetComponent<Camera>().backgroundColor = new Color(0.02f, 0.03f, 0.05f, 1f);

            var host = new GameObject("Velvet UI", typeof(UIDocument), typeof(StarterAppHost));
            host.GetComponent<UIDocument>().panelSettings = panel;

            EditorSceneManager.SaveScene(scene, ScenePath);
        }

        private static void EnsureInBuildSettings()
        {
            if (EditorBuildSettings.scenes.Any(entry => entry.path == ScenePath))
            {
                return;
            }

            EditorBuildSettings.scenes = EditorBuildSettings.scenes
                .Append(new EditorBuildSettingsScene(ScenePath, enabled: true))
                .ToArray();
        }
    }
}
#endif
