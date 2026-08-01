using System;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine.UIElements;
using Velvet.Editor;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins the mechanism that carries the bundled utility stylesheet into a player build, and that the
    /// consumer's PlayerSettings read afterwards exactly as before.
    /// </summary>
    /// <remarks>
    /// Whether the sheet then resolves in a player is not something an editor run can answer, because the
    /// editor reads it from the asset database either way. <c>BundledStyleUtilitiesRuntimeTests</c> is the
    /// fixture that answers it, and only when the suite runs with <c>-testPlatform StandaloneOSX</c>. What
    /// an editor run can answer is whether the holder still points at the sheet, which is the one link that
    /// fails in a player alone.
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
            var owned = PlayerSettings.GetPreloadedAssets();
            PlayerSettings.SetPreloadedAssets(owned.Concat(new[] { holder }).ToArray());
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
                PlayerSettings.SetPreloadedAssets(owned);
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
        public void Given_TheHolderAsset_When_ItsSheetIsRead_Then_ItIsTheOneTheEditorLoadsFromTheAssetPath()
        {
            // Arrange — the one link no other run can see: a player reads the sheet through the holder's
            // reference, every editor path reads it from the asset database, and a broken reference leaves
            // both of those working while the shipped player resolves no plain utility class at all.
            var holder = AssetDatabase.LoadAssetAtPath<VelvetRuntimeAssets>(RuntimeAssetsPath());

            // Act
            var throughHolder = HolderSheet(holder);
            var throughAssetPath = AssetDatabase.LoadAssetAtPath<StyleSheet>(StyleSheetAssetPath());

            // Assert — object identity, not a path comparison: a reference pointing at the right file but
            // the wrong object inside it reads as the same path and loads as a different sheet. The
            // asset-path side rides along, so a case that resolved neither could not satisfy it.
            Assert.That(
                (throughAssetPath != null, ReferenceEquals(throughHolder, throughAssetPath)),
                Is.EqualTo((true, true)));
        }

        [Test]
        public void Given_PreloadedAssetsHoldingAnEmptySlot_When_ABuildInjectsAndReverts_Then_TheSlotSurvives()
        {
            // Arrange — an empty slot, which the inspector's Size field produces and which deleting a
            // referenced asset leaves behind. It is the consumer's, and it is not ours to tidy.
            var owned = PlayerSettings.GetPreloadedAssets();
            PlayerSettings.SetPreloadedAssets(owned.Concat(new UnityEngine.Object[] { null }).ToArray());
            var injector = new BundledStyleSheetBuildInclusion();

            // Act
            int slots;
            try
            {
                injector.OnPreprocessBuild(null);
                injector.OnPostprocessBuild(null);
                slots = PlayerSettings.GetPreloadedAssets().Count(asset => asset == null);
            }
            finally
            {
                PlayerSettings.SetPreloadedAssets(owned);
            }

            // Assert
            Assert.That(slots, Is.EqualTo(1));
        }

        [Test]
        public void Given_ARecordedPathThatNoLongerResolves_When_TheRevertRuns_Then_ItKeepsTheRecord()
        {
            // Arrange — a build died leaving a record, and by the time it is read the recorded path resolves
            // to nothing: a package update moved the holder in the meantime. Deleting the record then is
            // what makes the leftover entry permanent, because injection is additive and would read it as
            // the consumer's own from that point on.
            var injector = new BundledStyleSheetBuildInclusion();
            injector.OnPreprocessBuild(null);
            File.AppendAllLines(
                RecordFilePath(), new[] { "Packages/com.velvet.core/Runtime/Assets/NoSuchHolder.asset" });

            // Act
            BundledStyleSheetBuildInclusion.Revert();

            // Assert — the record survives carrying exactly what could not be removed, so a later pass can
            // finish. Both terms in one comparison: a revert that deleted the file and one that left it
            // holding the whole original list are different failures.
            Assert.That(
                (File.Exists(RecordFilePath()), string.Join(", ", File.ReadAllLines(RecordFilePath()))),
                Is.EqualTo((true, "Packages/com.velvet.core/Runtime/Assets/NoSuchHolder.asset")));
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

        private static StyleSheet HolderSheet(VelvetRuntimeAssets holder)
            => (StyleSheet)typeof(VelvetRuntimeAssets)
                .GetProperty("StyleUtilities", BindingFlags.NonPublic | BindingFlags.Instance)!
                .GetValue(holder);

        private static string RuntimeAssetsPath() => DeclaredPath("RuntimeAssetsPath");

        private static string StyleSheetAssetPath() => DeclaredPath("StyleSheetAssetPath");

        private static string DeclaredPath(string field)
            => (string)typeof(VelvetStyleUtilities)
                .GetField(field, BindingFlags.NonPublic | BindingFlags.Static)!
                .GetRawConstantValue()!;

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
