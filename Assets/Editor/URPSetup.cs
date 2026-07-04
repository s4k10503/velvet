#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Velvet.EditorTools
{
    /// <summary>
    /// One-shot project conversion to the Universal Render Pipeline (URP). Velvet's core drop-shadow
    /// shader (<c>Velvet/DropShadow</c>) targets <c>"RenderPipeline" = "UniversalPipeline"</c>, so the
    /// showcase project must run under URP. Creates a URP asset + Universal Renderer data under
    /// <c>Assets/Settings/</c> and wires them into <see cref="GraphicsSettings"/> /
    /// <see cref="QualitySettings"/> so Unity writes the correct asset GUIDs into the
    /// ProjectSettings YAML (hand-editing those GUIDs is error-prone).
    /// <para>
    /// Run headless via <c>-executeMethod Velvet.EditorTools.URPSetup.CreateAndAssign</c>.
    /// Idempotent: re-running reuses the existing assets and re-asserts the assignment.
    /// </para>
    /// </summary>
    public static class URPSetup
    {
        private const string SettingsDir = "Assets/Settings";
        private const string PipelinePath = SettingsDir + "/VelvetURPAsset.asset";
        private const string RendererPath = SettingsDir + "/VelvetURPAsset_Renderer.asset";
        private const string UrpPackagePath = "Packages/com.unity.render-pipelines.universal";

        public static void CreateAndAssign()
        {
            if (!Directory.Exists(SettingsDir))
            {
                Directory.CreateDirectory(SettingsDir);
            }

            var pipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(PipelinePath);
            if (pipeline == null)
            {
                // Mirror UniversalRenderPipelineAsset.CreateUniversalPipelineAsset: create the Universal
                // Renderer data first, then a pipeline asset referencing it. ResourceReloader populates
                // the null shader/resource references the menu-driven path would otherwise fill in.
                var rendererData = ScriptableObject.CreateInstance<UniversalRendererData>();
                AssetDatabase.CreateAsset(rendererData, RendererPath);
                ResourceReloader.ReloadAllNullIn(rendererData, UrpPackagePath);

                pipeline = UniversalRenderPipelineAsset.Create(rendererData);
                AssetDatabase.CreateAsset(pipeline, PipelinePath);
                AssetDatabase.SaveAssets();
            }

            GraphicsSettings.defaultRenderPipeline = pipeline;

            // Point every quality level at the URP asset so no level falls back to the built-in pipeline.
            var levels = QualitySettings.names.Length;
            var current = QualitySettings.GetQualityLevel();
            for (var i = 0; i < levels; i++)
            {
                QualitySettings.SetQualityLevel(i, applyExpensiveChanges: false);
                QualitySettings.renderPipeline = pipeline;
            }
            QualitySettings.SetQualityLevel(current, applyExpensiveChanges: false);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"VELVET_URP_SETUP_OK pipeline={AssetDatabase.GetAssetPath(pipeline)} levels={levels}");
        }
    }
}
#endif
