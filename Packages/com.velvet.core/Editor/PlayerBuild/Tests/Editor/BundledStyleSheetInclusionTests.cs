using System;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Build;
using Velvet.Editor;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins the mechanism that carries the bundled utility stylesheet into a player build, and that the
    /// consumer's PlayerSettings read afterwards exactly as before.
    /// </summary>
    /// <remarks>
    /// Whether the sheet then resolves in a player is not something an editor run can answer, because the
    /// editor reads it from the asset database either way.
    /// <c>BundledStyleSheetPlayerInclusionTests</c> is the case that answers it, and only when the suite runs
    /// with <c>-testPlatform StandaloneOSX</c>.
    /// </remarks>
    [TestFixture]
    internal sealed class BundledStyleSheetInclusionTests
    {
        private const string SettingsAsset = "ProjectSettings/ProjectSettings.asset";

        [TearDown]
        public void TearDown()
        {
            BundledStyleSheetBuildInclusion.Revert();
            File.Delete(RecordFilePath());
        }

        [Test]
        public void Given_ABuildAboutToRun_When_ThePreprocessInjects_Then_TheHolderIsPreloaded()
        {
            // Arrange — the reading taken before the injection is folded into the assertion, so a reader that
            // reported the holder reached whatever the list held could not satisfy it.
            var injector = new BundledStyleSheetBuildInclusion();
            var before = BundledStyleSheetBuildInclusion.Unreached();

            // Act
            injector.OnPreprocessBuild(null);

            // Assert
            Assert.That((before, BundledStyleSheetBuildInclusion.Unreached()), Is.EqualTo((true, false)));
        }

        [Test]
        public void Given_AHolderTheProjectAlreadyPreloaded_When_ABuildInjectsAndReverts_Then_ThatEntrySurvives()
        {
            // Arrange — a consumer's own entry, which reads exactly like an injected one and is told apart
            // only by the record the injection keeps of what it added.
            var holder = BundledStyleSheetBuildInclusion.Holder();
            var owned = PlayerSettings.GetPreloadedAssets().Where(asset => asset != null).ToList();
            owned.Add(holder);
            PlayerSettings.SetPreloadedAssets(owned.ToArray());
            var injector = new BundledStyleSheetBuildInclusion();

            // Act
            bool survives;
            try
            {
                injector.OnPreprocessBuild(null);
                injector.OnPostprocessBuild(null);
                survives = !BundledStyleSheetBuildInclusion.Unreached();
            }
            finally
            {
                PlayerSettings.SetPreloadedAssets(
                    PlayerSettings.GetPreloadedAssets().Where(a => a != null && a != holder).ToArray());
            }

            // Assert — what separates a revert that removed what it added from one that removed every entry
            // it recognised.
            Assert.That(survives, Is.True);
        }

        [Test]
        public void Given_ABuildThatInjectedAndReverted_When_TheSettingsFileIsReadBack_Then_ItIsWhatItWas()
        {
            // Arrange
            var injector = new BundledStyleSheetBuildInclusion();
            var before = File.ReadAllText(SettingsAsset);

            // Act — the save is what a build does to project settings while it runs, arranged here rather
            // than waited for, so the revert is asked to undo a change that is already on disk.
            injector.OnPreprocessBuild(null);
            AssetDatabase.SaveAssets();
            var whileInjected = File.ReadAllText(SettingsAsset);
            injector.OnPostprocessBuild(null);

            // Assert — the injected reading is folded in because a revert that changed only the loaded
            // object would leave the diff sitting in the file.
            Assert.That(
                (whileInjected == before, File.ReadAllText(SettingsAsset)),
                Is.EqualTo((false, before)));
        }

        [Test]
        public void Given_AnUnwritableSettingsFile_When_ABuildWouldInject_Then_ItFailsInsteadOfWritingNothing()
        {
            // Arrange — an exclusive handle rather than the read-only attribute, which on a checkout the
            // process does not own answers for a permission triad that is not the one in force.
            var injector = new BundledStyleSheetBuildInclusion();
            bool writableBefore;
            try
            {
                using var probe = File.Open(SettingsAsset, FileMode.Open, FileAccess.Write, FileShare.ReadWrite);
                writableBefore = true;
            }
            catch (Exception)
            {
                writableBefore = false;
            }

            // Act
            Exception refused = null;
            using (File.Open(SettingsAsset, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                try
                {
                    injector.OnPreprocessBuild(null);
                }
                catch (Exception exception)
                {
                    refused = exception;
                }
            }

            // Assert — the arranged precondition rides along: where the file was already unwritable the
            // refusal proves nothing about the handle.
            Assert.That((writableBefore, refused?.GetType()), Is.EqualTo((true, typeof(BuildFailedException))));
        }

        [Test]
        public void Given_ABuildThatDiedBeforeItsPostprocess_When_TheEditorLoadsAgain_Then_TheEntryIsGone()
        {
            // Arrange — the record on disk outlives the editor; erasing the session marker is what a new
            // process looks like to the repair.
            var injector = new BundledStyleSheetBuildInclusion();
            injector.OnPreprocessBuild(null);
            var injected = BundledStyleSheetBuildInclusion.Unreached();
            SessionState.EraseString(LiveSessionKey());

            // Act
            BundledStyleSheetBuildInclusion.RevertWhatAnEndedSessionLeft();

            // Assert — the injected reading is folded in, so a repair that ran against an empty list would
            // not satisfy it.
            Assert.That(
                (injected, BundledStyleSheetBuildInclusion.Unreached()),
                Is.EqualTo((false, true)));
        }

        [Test]
        public void Given_ABuildStillRunningInThisSession_When_ADomainReloadRepairs_Then_TheEntryStays()
        {
            // Arrange
            var injector = new BundledStyleSheetBuildInclusion();
            var before = BundledStyleSheetBuildInclusion.Unreached();
            injector.OnPreprocessBuild(null);

            // Act — the reload a build can take between its two callbacks, which must not undo the injection.
            BundledStyleSheetBuildInclusion.RevertWhatAnEndedSessionLeft();

            // Assert
            Assert.That((before, BundledStyleSheetBuildInclusion.Unreached()), Is.EqualTo((true, false)));
        }

        private static string RecordFilePath()
            => (string)typeof(BundledStyleSheetBuildInclusion)
                .GetField("RecordFile", BindingFlags.NonPublic | BindingFlags.Static)!
                .GetRawConstantValue()!;

        private static string LiveSessionKey()
            => (string)typeof(BundledStyleSheetBuildInclusion)
                .GetField("LiveSessionKey", BindingFlags.NonPublic | BindingFlags.Static)!
                .GetRawConstantValue()!;
    }
}
