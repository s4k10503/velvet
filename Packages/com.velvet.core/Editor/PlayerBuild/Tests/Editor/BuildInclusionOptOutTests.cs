using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UIElements;
using Velvet.Editor;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins the per-project opt-outs from what the build steps carry into a player, the Project Settings page
    /// that records them, and the missing-sheet report the holder's opt-out silences.
    /// </summary>
    [TestFixture]
    internal sealed class BuildInclusionOptOutTests
    {
        private const string GraphicsSettingsAsset = "ProjectSettings/GraphicsSettings.asset";
        private const string PlayerSettingsAsset = "ProjectSettings/ProjectSettings.asset";

        private static readonly Regex ReportedTarget = new(@"target '([^']*)' is on a panel that does not carry");

        private static readonly FieldInfo MissingReported = typeof(VelvetStyleUtilities)
            .GetField("s_missingReported", BindingFlags.NonPublic | BindingFlags.Static);

        private readonly List<string> _reported = new();
        private readonly List<IDisposable> _disposables = new();
        private string _settingsFileBefore;
        private object _reportedBefore;

        [SetUp]
        public void SetUp()
        {
            _settingsFileBefore = File.Exists(VelvetBuildSettings.SettingsFile)
                ? File.ReadAllText(VelvetBuildSettings.SettingsFile)
                : null;
            File.Delete(VelvetBuildSettings.SettingsFile);
            _reportedBefore = MissingReported.GetValue(null);
            MissingReported.SetValue(null, false);
            Application.logMessageReceived += Record;
        }

        [TearDown]
        public void TearDown()
        {
            Application.logMessageReceived -= Record;
            foreach (var disposable in _disposables) disposable.Dispose();
            _disposables.Clear();
            _reported.Clear();
            MissingReported.SetValue(null, _reportedBefore);

            BundledShaderBuildInclusion.Revert();
            BundledStyleSheetBuildInclusion.Revert();
            File.Delete(RecordFile(typeof(BundledShaderBuildInclusion)));
            File.Delete(RecordFile(typeof(BundledStyleSheetBuildInclusion)));

            if (_settingsFileBefore == null) File.Delete(VelvetBuildSettings.SettingsFile);
            else File.WriteAllText(VelvetBuildSettings.SettingsFile, _settingsFileBefore);
        }

        private void Record(string condition, string stackTrace, LogType type)
        {
            var match = ReportedTarget.Match(condition);
            if (type == LogType.Warning && match.Success) _reported.Add(match.Groups[1].Value);
        }

        [Test]
        public void Given_AShaderTheProjectExcluded_When_ThePreprocessInjects_Then_EveryOtherBundledShaderIsIncluded()
        {
            // Arrange
            VelvetBuildSettings.Change(settings => settings.SetExcluded(VelvetShaders.DropShadow, true));

            // Act
            new BundledShaderBuildInclusion().OnPreprocessBuild(null);

            // Assert — the listed set rather than the unreached one, which reads the same exclusion it is
            // asked about.
            Assert.That(
                string.Join(", ", VelvetShaders.Names.Where(AlwaysIncluded)),
                Is.EqualTo(string.Join(", ", VelvetShaders.Names.Where(n => n != VelvetShaders.DropShadow))));
        }

        [Test]
        public void Given_TheSettingsFileChangedAfterABuild_When_TheNextBuildRuns_Then_ItFollowsTheFile()
        {
            // Arrange — a build that read the file before the change, as a pull between two builds leaves it.
            var injector = new BundledShaderBuildInclusion();
            injector.OnPreprocessBuild(null);
            injector.OnPostprocessBuild(null);
            VelvetBuildSettings.Change(settings => settings.SetExcluded(VelvetShaders.FilterBrightness, true));

            // Act
            injector.OnPreprocessBuild(null);

            // Assert
            Assert.That(AlwaysIncluded(VelvetShaders.FilterBrightness), Is.False);
        }

        [Test]
        public void Given_TheStyleSheetExcluded_When_ThePreprocessRuns_Then_TheHolderIsNotPreloaded()
        {
            // Arrange — the reading before the build is folded in, so a project that preloaded the holder
            // itself could not satisfy this.
            VelvetBuildSettings.Change(settings => settings.ExcludeStyleSheet = true);
            var before = BundledStyleSheetBuildInclusion.Unreached();

            // Act
            new BundledStyleSheetBuildInclusion().OnPreprocessBuild(null);

            // Assert
            Assert.That((before, BundledStyleSheetBuildInclusion.Unreached()), Is.EqualTo((true, true)));
        }

        [Test]
        public void Given_TheStyleSheetExcludedAndItsSettingsFileLocked_When_ThePreprocessRuns_Then_ItDoesNotRefuse()
        {
            // Arrange — an exclusive handle, as BundledStyleSheetInclusionTests arranges its unwritable file.
            VelvetBuildSettings.Change(settings => settings.ExcludeStyleSheet = true);
            var injector = new BundledStyleSheetBuildInclusion();

            // Act
            bool writableUnderLock;
            Exception refused = null;
            using (File.Open(PlayerSettingsAsset, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                writableUnderLock = CanOpenForWriting(PlayerSettingsAsset);
                try
                {
                    injector.OnPreprocessBuild(null);
                }
                catch (Exception exception)
                {
                    refused = exception;
                }
            }

            // Assert — the lock's own reading rides along: where it did not make the file unwritable, the
            // absence of a refusal says nothing about the exclusion.
            Assert.That((writableUnderLock, refused?.GetType()), Is.EqualTo((false, (Type)null)));
        }

        [Test]
        public void Given_TheSettingsPage_When_TheStyleSheetIsUnticked_Then_TheSavedSettingsExcludeIt()
        {
            // Arrange
            var page = Page();

            // Act
            PageToggle(page, VelvetStyleUtilities.RuntimeAssetsPath).SimulateChange(false);

            // Assert
            Assert.That(VelvetBuildSettings.Read().ExcludeStyleSheet, Is.True);
        }

        [Test]
        public void Given_TheSettingsPage_When_AShaderIsUnticked_Then_TheSavedSettingsExcludeIt()
        {
            // Arrange
            var page = Page();

            // Act
            PageToggle(page, VelvetShaders.GradientSilhouette).SimulateChange(false);

            // Assert
            Assert.That(VelvetBuildSettings.Read().Excludes(VelvetShaders.GradientSilhouette), Is.True);
        }

        [Test]
        public void Given_AnExcludedShader_When_ItIsTickedAgain_Then_ItIsIncludedAgain()
        {
            // Arrange
            VelvetBuildSettings.Change(settings => settings.SetExcluded(VelvetShaders.FilterSaturate, true));
            var toggle = PageToggle(Page(), VelvetShaders.FilterSaturate);

            // Act
            toggle.SimulateChange(true);

            // Assert
            Assert.That(VelvetBuildSettings.Read().Excludes(VelvetShaders.FilterSaturate), Is.False);
        }

        [Test]
        public void Given_AnOpenPageAndAChangeLandingOnDisk_When_AnotherToggleIsChanged_Then_BothChoicesAreSaved()
        {
            // Arrange — the page reads the file when it opens; the change after it is a teammate's, arriving
            // with a pull.
            var page = Page();
            VelvetBuildSettings.Change(settings => settings.SetExcluded(VelvetShaders.DropShadow, true));

            // Act
            PageToggle(page, VelvetStyleUtilities.RuntimeAssetsPath).SimulateChange(false);

            // Assert
            var saved = VelvetBuildSettings.Read();
            Assert.That((saved.ExcludeStyleSheet, saved.Excludes(VelvetShaders.DropShadow)), Is.EqualTo((true, true)));
        }

        [Test]
        public void Given_TheHolderExcluded_When_ATreeIsMountedOnAPanelWithoutTheSheet_Then_OnlyALaterMountIsReported()
        {
            // Arrange
            VelvetBuildSettings.Change(settings => settings.ExcludeStyleSheet = true);
            var silenced = OnBarePanel("silenced");
            var bare = OnBarePanel("bare");

            // Act — the second mount follows lifting the exclusion, so a report the first one made would have
            // taken the run's only one.
            Mount(silenced);
            VelvetBuildSettings.Change(settings => settings.ExcludeStyleSheet = false);
            Mount(bare);

            // Assert
            Assert.That(string.Join(", ", _reported), Is.EqualTo("bare"));
        }

        private VisualElement OnBarePanel(string name)
        {
            var host = new HeadlessEditorPanelHost();
            _disposables.Add(host);
            var target = new VisualElement { name = name };
            host.Root.Add(target);
            return target;
        }

        private void Mount(VisualElement target) => _disposables.Insert(0, V.Mount(target, V.Div()));

        private static VisualElement Page()
        {
            var root = new VisualElement();
            VelvetBuildSettings.CreateProvider().OnActivate(string.Empty, root);
            return root;
        }

        private static Toggle PageToggle(VisualElement page, string label)
            => page.Query<Toggle>().Where(t => t.label == label).First();

        private static bool AlwaysIncluded(string shaderName)
        {
            var included = new SerializedObject(AssetDatabase.LoadAssetAtPath<GraphicsSettings>(GraphicsSettingsAsset))
                .FindProperty("m_AlwaysIncludedShaders");
            var shader = Shader.Find(shaderName);
            for (var i = 0; i < included.arraySize; i++)
            {
                if (included.GetArrayElementAtIndex(i).objectReferenceValue == shader) return true;
            }
            return false;
        }

        private static bool CanOpenForWriting(string path)
        {
            try
            {
                using var probe = File.Open(path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static string RecordFile(Type inclusion)
            => (string)inclusion.GetField("RecordFile", BindingFlags.NonPublic | BindingFlags.Static)!
                .GetRawConstantValue()!;
    }
}
