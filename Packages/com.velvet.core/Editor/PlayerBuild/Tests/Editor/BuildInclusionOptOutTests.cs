using System;
using System.IO;
using System.Linq;
using System.Reflection;
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
    /// Pins the per-project opt-outs from what the build steps carry into a player, and the Project Settings
    /// page that records them.
    /// </summary>
    [TestFixture]
    internal sealed class BuildInclusionOptOutTests
    {
        private const string GraphicsSettingsAsset = "ProjectSettings/GraphicsSettings.asset";
        private const string PlayerSettingsAsset = "ProjectSettings/ProjectSettings.asset";
        private const string SettingsFile = "ProjectSettings/VelvetBuildSettings.asset";

        private string _settingsFileBefore;

        [SetUp]
        public void SetUp()
        {
            _settingsFileBefore = File.Exists(SettingsFile) ? File.ReadAllText(SettingsFile) : null;
        }

        [TearDown]
        public void TearDown()
        {
            var settings = VelvetBuildSettings.instance;
            settings.ExcludeStyleSheet = false;
            foreach (var name in VelvetShaders.Names) settings.SetExcluded(name, false);

            BundledShaderBuildInclusion.Revert();
            BundledStyleSheetBuildInclusion.Revert();
            File.Delete(RecordFile(typeof(BundledShaderBuildInclusion)));
            File.Delete(RecordFile(typeof(BundledStyleSheetBuildInclusion)));

            if (_settingsFileBefore == null) File.Delete(SettingsFile);
            else File.WriteAllText(SettingsFile, _settingsFileBefore);
        }

        [Test]
        public void Given_AShaderTheProjectExcluded_When_ThePreprocessInjects_Then_EveryOtherBundledShaderIsIncluded()
        {
            // Arrange
            VelvetBuildSettings.instance.SetExcluded(VelvetShaders.DropShadow, true);

            // Act
            new BundledShaderBuildInclusion().OnPreprocessBuild(null);

            // Assert — the listed set rather than the unreached one, which reads the same exclusion it is
            // asked about.
            Assert.That(
                string.Join(", ", VelvetShaders.Names.Where(AlwaysIncluded)),
                Is.EqualTo(string.Join(", ", VelvetShaders.Names.Where(n => n != VelvetShaders.DropShadow))));
        }

        [Test]
        public void Given_TheStyleSheetExcluded_When_ThePreprocessRuns_Then_TheHolderIsNotPreloaded()
        {
            // Arrange — the reading before the build is folded in, so a project that preloaded the holder
            // itself could not satisfy this.
            VelvetBuildSettings.instance.ExcludeStyleSheet = true;
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
            VelvetBuildSettings.instance.ExcludeStyleSheet = true;
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
            var sheet = page.Query<Toggle>().Where(t => t.label == VelvetStyleUtilities.RuntimeAssetsPath).First();

            // Act
            sheet.SimulateChange(false);

            // Assert
            Assert.That(File.ReadAllText(SettingsFile), Does.Contain("_excludeStyleSheet: 1"));
        }

        [Test]
        public void Given_TheSettingsPage_When_AShaderIsUnticked_Then_TheSavedSettingsExcludeIt()
        {
            // Arrange
            var page = Page();

            // Act
            ShaderToggle(page, VelvetShaders.GradientSilhouette).SimulateChange(false);

            // Assert
            Assert.That(File.ReadAllText(SettingsFile), Does.Contain("- " + VelvetShaders.GradientSilhouette));
        }

        [Test]
        public void Given_AnExcludedShader_When_ItIsTickedAgain_Then_ItIsIncludedAgain()
        {
            // Arrange
            VelvetBuildSettings.instance.SetExcluded(VelvetShaders.FilterSaturate, true);
            var toggle = ShaderToggle(Page(), VelvetShaders.FilterSaturate);

            // Act
            toggle.SimulateChange(true);

            // Assert
            Assert.That(VelvetBuildSettings.instance.Excludes(VelvetShaders.FilterSaturate), Is.False);
        }

        private static VisualElement Page()
        {
            var root = new VisualElement();
            VelvetBuildSettings.CreateProvider().OnActivate(string.Empty, root);
            return root;
        }

        private static Toggle ShaderToggle(VisualElement page, string shaderName)
            => page.Query<Toggle>().Where(t => t.label == shaderName).First();

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
