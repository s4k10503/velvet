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
        public IEnumerator Given_AUtilitySheetReachedOnlyByImport_When_A_StrippedIl2CppPlayerMounts_Then_OnlyTheBarePhaseWarns()
        {
            // Arrange
            var carriers = Resources.FindObjectsOfTypeAll<ImportedStyleSheetPlayerAssets>();
            var assets = carriers.FirstOrDefault();
            VisualElement imported = null;
            VisualElement bare = null;
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
                imported = Target("imported", assets.PanelSettings);
                imported.styleSheets.Add(assets.ImportingSheet);
                _mounts.Add(V.Mount(imported, V.Div(name: "probe", className: "flex-row")));
                yield return new WaitForSecondsRealtime(0.5f);
                importedWarnings = _warnings.Count;
                bare = Target("bare", assets.PanelSettings);
                _mounts.Add(V.Mount(bare, V.Div(name: "probe", className: "flex-row")));
                yield return new WaitForSecondsRealtime(0.5f);
                bareWarnings = _warnings.Count - importedWarnings;
            }

            // Assert
            Assert.That((Application.platform, il2Cpp, carriers.Length, assets?.ScriptingBackend, assets?.StrippingLevel,
                    assets?.ImportingSheet?.name, imported?.styleSheets.count, imported?.parent.styleSheets.count,
                    bare?.styleSheets.count, EngineMember.StyleSheetImports.ResolveField() != null,
                    EngineMember.ImportedStyleSheet.ResolveField() != null, importedWarnings, bareWarnings,
                    imported?.Q<VisualElement>("probe")?.resolvedStyle.flexDirection,
                    bare?.Q<VisualElement>("probe")?.resolvedStyle.flexDirection),
                Is.EqualTo((RuntimePlatform.OSXPlayer, true, 1, "IL2CPP", "High", "ImportsVelvetUtilities",
                    (int?)1, (int?)0, (int?)0, true, true, 0, 1, (FlexDirection?)FlexDirection.Row,
                    (FlexDirection?)FlexDirection.Column)));
        }
    }
}
