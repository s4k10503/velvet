using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace Velvet.Tests
{
    [TestFixture]
    [UnityPlatform(RuntimePlatform.OSXPlayer)]
    [PrebuildSetup("Velvet.Tests.ImportedStyleSheetPlayerSetup")]
    [PostBuildCleanup("Velvet.Tests.ImportedStyleSheetPlayerSetup")]
    internal sealed class ImportedStyleSheetPlayerTests
    {
        private readonly List<string> _warnings = new();
        private readonly List<GameObject> _documents = new();
        private readonly List<PanelSettings> _settings = new();
        private readonly List<MountedTree> _mounts = new();
        private readonly List<AssetBundle> _bundles = new();

        [Serializable]
        internal sealed class Observation
        {
            public string ShaderNames;
            public string UnavailableShaders;
            public string OriginalName;
            public string BundleName;
            public string OriginalImports;
            public string BundleImports;
            public bool DistinctBundle;
            public bool CompleteOriginalImports;
            public bool CompleteBundleImports;
            public int ImportedWarnings;
            public int BundleWarnings;
            public int BareWarnings;
            public string ImportedDirection;
            public string BundleDirection;
            public string BareDirection;
        }

        internal Observation Snapshot;

        private static StyleSheet[] Imports(StyleSheet sheet)
            => (EngineMember.StyleSheetImports.ResolveField()?.GetValue(sheet) as Array ?? Array.Empty<object>())
                .Cast<object>().Select(import => EngineMember.ImportedStyleSheet.ResolveField()?.GetValue(import) as StyleSheet)
                .ToArray();

        [SetUp]
        public void SetUp() => Application.logMessageReceived += Record;

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            Application.logMessageReceived -= Record;
            foreach (var mount in _mounts) mount.Dispose();
            foreach (var document in _documents) Object.Destroy(document);
            foreach (var settings in _settings) Object.Destroy(settings);
            _mounts.Clear();
            _documents.Clear();
            _settings.Clear();
            _warnings.Clear();
            yield return null;
            foreach (var bundle in _bundles) bundle.Unload(true);
            _bundles.Clear();
        }

        private void Record(string message, string stackTrace, LogType type)
        {
            if (type == LogType.Warning && message.Contains("V.Mount or V.Portal target '")
                && message.Contains("is on a panel that does not carry Velvet's utility stylesheet"))
                _warnings.Add(message);
        }

        private VisualElement Target(string name, PanelSettings template)
        {
            var gameObject = new GameObject(name);
            gameObject.SetActive(false);
            _documents.Add(gameObject);
            var settings = Object.Instantiate(template);
            _settings.Add(settings);
            var document = gameObject.AddComponent<UIDocument>();
            document.panelSettings = settings;
            gameObject.SetActive(true);
            var target = new VisualElement { name = name };
            document.rootVisualElement.Add(target);
            return target;
        }

        [UnityTest]
        public IEnumerator Given_TheBundledAssets_When_AStrippedIl2CppPlayerUsesThem_Then_ShadersAndCopiedSheetsRemainUsable()
        {
            // Arrange
            var carriers = Resources.FindObjectsOfTypeAll<ImportedStyleSheetPlayerAssets>();
            var assets = carriers.FirstOrDefault();
            VisualElement imported = null;
            VisualElement bare = null;
            VisualElement bundled = null;
            StyleSheet bundleSheet = null;
            StyleSheet original = null;
            var originalImports = Array.Empty<StyleSheet>();
            var bundleImports = Array.Empty<StyleSheet>();
            var bundleWarnings = -1;
            var unavailableShaders = "<no shader manifest>";
            var importedWarnings = -1;
            var bareWarnings = -1;
#if ENABLE_IL2CPP
            const bool il2Cpp = true;
#else
            const bool il2Cpp = false;
#endif

            // Act
            if (assets != null && assets.PanelSettings != null && assets.ImportingSheet != null)
            {
                unavailableShaders = string.Join(", ", (assets.ShaderNames ?? Array.Empty<string>()).Where(name =>
                {
                    var shader = Shader.Find(name);
                    return shader == null || !shader.isSupported;
                }));
                original = VelvetStyleUtilities.Sheet;
                originalImports = Imports(original);
                var bundle = AssetBundle.LoadFromFile(assets.BundlePath);
                if (bundle != null)
                {
                    _bundles.Add(bundle);
                    bundleSheet = bundle.LoadAsset<StyleSheet>(VelvetStyleUtilities.StyleSheetAssetPath);
                    if (bundleSheet != null) bundleImports = Imports(bundleSheet);
                }
                imported = Target("imported", assets.PanelSettings);
                imported.styleSheets.Add(assets.ImportingSheet);
                _mounts.Add(V.Mount(imported, V.Div(name: "probe", className: "flex-row")));
                yield return new WaitForSecondsRealtime(0.5f);
                importedWarnings = _warnings.Count;
                bundled = Target("bundled", assets.PanelSettings);
                if (bundleSheet != null) bundled.styleSheets.Add(bundleSheet);
                _mounts.Add(V.Mount(bundled, V.Div(name: "probe", className: "flex-row")));
                yield return new WaitForSecondsRealtime(0.5f);
                bundleWarnings = _warnings.Count - importedWarnings;
                bare = Target("bare", assets.PanelSettings);
                _mounts.Add(V.Mount(bare, V.Div(name: "probe", className: "flex-row")));
                yield return new WaitForSecondsRealtime(0.5f);
                bareWarnings = _warnings.Count - importedWarnings - bundleWarnings;
            }

            // Assert
            Snapshot = new Observation
            {
                ShaderNames = string.Join(", ", assets?.ShaderNames ?? Array.Empty<string>()),
                UnavailableShaders = unavailableShaders,
                OriginalName = original?.name,
                BundleName = bundleSheet?.name,
                OriginalImports = string.Join(", ", originalImports.Select(sheet => sheet?.name ?? "<null>")),
                BundleImports = string.Join(", ", bundleImports.Select(sheet => sheet?.name ?? "<null>")),
                DistinctBundle = bundleSheet != null && !ReferenceEquals(bundleSheet, original),
                CompleteOriginalImports = originalImports.Length > 0 && originalImports.All(sheet => sheet != null),
                CompleteBundleImports = bundleImports.Length > 0 && bundleImports.All(sheet => sheet != null),
                ImportedWarnings = importedWarnings,
                BundleWarnings = bundleWarnings,
                BareWarnings = bareWarnings,
                ImportedDirection = imported?.Q<VisualElement>("probe")?.resolvedStyle.flexDirection.ToString(),
                BundleDirection = bundled?.Q<VisualElement>("probe")?.resolvedStyle.flexDirection.ToString(),
                BareDirection = bare?.Q<VisualElement>("probe")?.resolvedStyle.flexDirection.ToString(),
            };
            Assert.That((Application.platform, il2Cpp, carriers.Length, assets?.ScriptingBackend, assets?.StrippingLevel,
                    assets?.ImportingSheet?.name, imported?.styleSheets.count, imported?.parent.styleSheets.count,
                    bare?.styleSheets.count, EngineMember.StyleSheetImports.ResolveField() != null,
                    EngineMember.ImportedStyleSheet.ResolveField() != null, importedWarnings, bareWarnings,
                    imported?.Q<VisualElement>("probe")?.resolvedStyle.flexDirection,
                    bare?.Q<VisualElement>("probe")?.resolvedStyle.flexDirection,
                    assets?.ShaderNames?.Length > 0, unavailableShaders, Snapshot.DistinctBundle,
                    Snapshot.CompleteOriginalImports, Snapshot.CompleteBundleImports, bundleSheet?.name,
                    Snapshot.BundleImports, bundled?.styleSheets.count, bundled?.parent.styleSheets.count,
                    bundleWarnings, bundled?.Q<VisualElement>("probe")?.resolvedStyle.flexDirection),
                Is.EqualTo((RuntimePlatform.OSXPlayer, true, 1, "IL2CPP", "High", "ImportsVelvetUtilities",
                    (int?)1, (int?)0, (int?)0, true, true, 0, 1, (FlexDirection?)FlexDirection.Row,
                    (FlexDirection?)FlexDirection.Column, true, "", true, true, true, original?.name,
                    Snapshot.OriginalImports, (int?)1, (int?)0, 0, (FlexDirection?)FlexDirection.Row)));
        }
    }
}
