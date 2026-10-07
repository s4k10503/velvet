using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
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
        private const float NativeTolerance = 0.02f;

        private static FieldInfo MissingReported => typeof(VelvetStyleUtilities)
            .GetField("s_missingReported", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new MissingFieldException(typeof(VelvetStyleUtilities).FullName, "s_missingReported");

        private bool _reportedBefore;
        private bool _hasSavedReport;
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

        [Serializable]
        internal sealed class NativeObservation
        {
            public string TemplateScaleMode;
            public float TemplateScale;
            public float TemplatePixelsPerPoint;
            public string PanelScaleMode;
            public float PanelScale;
            public float PanelPixelsPerPoint;
            public int ScreenWidth;
            public int ScreenHeight;
            public Vector2 ReferenceResolution;
            public bool MembersResolved;
            public bool IdleRunning;
            public bool NativeRunning;
            public bool UnrelatedRunning;
            public float InitialWidth;
            public float NativePaint;
            public bool CompletedRunning;
            public float CompletedWidth;
            public bool RestartedRunning;
            public float TakeoverPaint;
            public float InlineTarget;
            public float CustomStart;
            public float CustomInline;
            public float CustomPaint;
            public bool CustomNativeRunning;
        }

        internal NativeObservation NativeSnapshot;

        private static bool NativeRunning(VisualElement element, MethodInfo method, object id)
            => method.Invoke(element, new[] { id }) is true;

        private static void ConfigureNativeWidth(VisualElement element, float duration)
        {
            element.style.transitionProperty = new List<StylePropertyName> { new("width") };
            element.style.transitionDuration = new List<TimeValue> { new(duration, TimeUnit.Second) };
            element.style.transitionTimingFunction = new List<EasingFunction> { new(EasingMode.Linear) };
        }

        [UnityTest]
        public IEnumerator Given_ANativeTransition_When_AStrippedIl2CppPlayerTakesOver_Then_TheCustomStartMatchesTheDisplayedValue()
        {
            // Arrange
            var assets = Resources.FindObjectsOfTypeAll<ImportedStyleSheetPlayerAssets>().Single();
            var method = EngineMember.HasRunningStyleAnimation.ResolveMethod();
            var property = EngineMember.StylePropertyNameId.ResolveProperty();
            NativeSnapshot = new NativeObservation { MembersResolved = method != null && property != null };
            var width = property.GetValue(new StylePropertyName("width"));
            var height = property.GetValue(new StylePropertyName("height"));
            var element = Target("native-start", assets.PanelSettings);
            var settings = _settings.Single();
            yield return null;
            yield return null;
            NativeSnapshot.TemplateScaleMode = settings.scaleMode.ToString();
            NativeSnapshot.TemplateScale = settings.scale;
            NativeSnapshot.TemplatePixelsPerPoint = element.panel.scaledPixelsPerPoint;
            NativeSnapshot.ScreenWidth = Screen.width;
            NativeSnapshot.ScreenHeight = Screen.height;
            NativeSnapshot.ReferenceResolution = settings.referenceResolution;
            settings.scaleMode = PanelScaleMode.ConstantPixelSize;
            settings.scale = 1f;
            element.style.width = 200f;
            element.style.height = 20f;
            yield return null;
            yield return null;
            NativeSnapshot.PanelScaleMode = settings.scaleMode.ToString();
            NativeSnapshot.PanelScale = settings.scale;
            NativeSnapshot.PanelPixelsPerPoint = element.panel.scaledPixelsPerPoint;
            NativeSnapshot.InitialWidth = element.resolvedStyle.width;
            NativeSnapshot.IdleRunning = NativeRunning(element, method, width);

            // Act
            ConfigureNativeWidth(element, 2f);
            yield return null;
            element.style.width = 40f;
            yield return new WaitForSecondsRealtime(0.5f);
            NativeSnapshot.NativeRunning = NativeRunning(element, method, width);
            NativeSnapshot.UnrelatedRunning = NativeRunning(element, method, height);
            NativeSnapshot.NativePaint = element.resolvedStyle.width;
            yield return new WaitForSecondsRealtime(2.1f);
            NativeSnapshot.CompletedRunning = NativeRunning(element, method, width);
            NativeSnapshot.CompletedWidth = element.resolvedStyle.width;
            ConfigureNativeWidth(element, 0f);
            element.style.width = 200f;
            yield return null;
            yield return null;
            ConfigureNativeWidth(element, 2f);
            yield return null;
            element.style.width = 40f;
            yield return new WaitForSecondsRealtime(0.5f);
            NativeSnapshot.RestartedRunning = NativeRunning(element, method, width);
            NativeSnapshot.TakeoverPaint = element.resolvedStyle.width;
            NativeSnapshot.InlineTarget = element.style.width.value.value;
            var from = Array.Empty<string>();
            var to = new[] { "w-[100px]" };
            var context = MotionSlotContext.Read(element, from, to, _ => false);
            var plan = MotionSpringClassParser.Resolve(from, to, 0f, 0f, context);
            NativeSnapshot.CustomStart = plan.Lengths.Single(channel => channel.Property == ArbitraryProperty.Width).From;
            var state = BezierTweenDriver.Create(plan, 0f, 0f, 1f, 1f, 1f);
            try
            {
                BezierTweenDriver.ApplyCurrentValues(element, state);
                NativeSnapshot.CustomInline = element.style.width.value.value;
                yield return null;
                yield return null;
                NativeSnapshot.CustomPaint = element.resolvedStyle.width;
                NativeSnapshot.CustomNativeRunning = NativeRunning(element, method, width);
            }
            finally { BezierTweenDriver.ClearInlineOverrides(element, state); }

            // Assert
            Assert.That(NativeActual(assets), Is.EqualTo(new[]
            {
                1f, 1f, 0f, 1f, 0f, 200f, 1f, 0f, 40f, 1f, 1f, 40f,
                NativeSnapshot.TakeoverPaint, NativeSnapshot.TakeoverPaint, NativeSnapshot.TakeoverPaint, 0f,
            }).Within(NativeTolerance));
        }

        private float[] NativeActual(ImportedStyleSheetPlayerAssets assets)
        {
#if ENABLE_IL2CPP
            const bool il2Cpp = true;
#else
            const bool il2Cpp = false;
#endif
            var s = NativeSnapshot;
            return new[]
            {
                Application.platform == RuntimePlatform.OSXPlayer && il2Cpp
                    && assets.ScriptingBackend == "IL2CPP" && assets.StrippingLevel == "High" ? 1f : 0f,
                s.MembersResolved ? 1f : 0f, s.IdleRunning ? 1f : 0f, s.NativeRunning ? 1f : 0f,
                s.UnrelatedRunning ? 1f : 0f, s.InitialWidth, s.NativePaint > 40f && s.NativePaint < 200f ? 1f : 0f,
                s.CompletedRunning ? 1f : 0f, s.CompletedWidth, s.RestartedRunning ? 1f : 0f,
                s.TakeoverPaint > 40f && s.TakeoverPaint < 200f
                    && Mathf.Abs(s.TakeoverPaint - s.InlineTarget) > NativeTolerance ? 1f : 0f, s.InlineTarget,
                s.CustomStart, s.CustomInline, s.CustomPaint, s.CustomNativeRunning ? 1f : 0f,
            };
        }

        private static StyleSheet[] Imports(StyleSheet sheet)
            => (EngineMember.StyleSheetImports.ResolveField()?.GetValue(sheet) as Array ?? Array.Empty<object>())
                .Cast<object>().Select(import => EngineMember.ImportedStyleSheet.ResolveField()?.GetValue(import) as StyleSheet)
                .ToArray();

        [SetUp]
        public void SetUp()
        {
            _reportedBefore = (bool)MissingReported.GetValue(null);
            _hasSavedReport = true;
            try
            {
                MissingReported.SetValue(null, false);
                Application.logMessageReceived += Record;
            }
            catch
            {
                MissingReported.SetValue(null, _reportedBefore);
                _hasSavedReport = false;
                throw;
            }
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            try
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
            finally
            {
                if (_hasSavedReport)
                {
                    MissingReported.SetValue(null, _reportedBefore);
                    _hasSavedReport = false;
                }
            }
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
