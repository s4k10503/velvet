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
    /// Whether the sheet then resolves in a player is not something an editor run can answer, because an
    /// editor that cannot resolve it through the holder falls back to the asset path.
    /// <c>BundledStyleUtilitiesRuntimeTests</c> is the fixture that answers it, and only when the suite runs
    /// with <c>-testPlatform StandaloneOSX</c>. What an editor run can answer is whether the holder still
    /// points at the sheet, which is the one link that fails in a player alone.
    /// </remarks>
    [TestFixture]
    internal sealed class BundledStyleSheetInclusionTests
    {
        private const string SettingsAsset = "ProjectSettings/ProjectSettings.asset";

        // Revert leaves the record in place when a recorded path no longer resolves, which is the behaviour
        // one case here arranges, and returns before erasing the session marker. Both are cleared here so
        // that case cannot decide whether a later one's repair runs at all.
        [TearDown]
        public void TearDown()
        {
            BundledStyleSheetBuildInclusion.Revert();
            File.Delete(RecordFilePath());
            SessionState.EraseString(LiveSessionKey());
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
            // reference alone, while an editor falls back to the asset path when that reference is broken.
            // So a broken reference leaves every editor run working and the shipped player resolving every
            // utility the sheet declares to nothing.
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
                // Saved as well as set, because the revert this case drives saves: leaving the list restored
                // in memory alone would hand the next case a settings file it never wrote.
                PlayerSettings.SetPreloadedAssets(owned);
                AssetDatabase.SaveAssets();
            }

            // Assert — a delta on what the project already held, so a checkout whose preloaded assets carry
            // an empty slot of their own reads the same as one that does not.
            Assert.That(slots, Is.EqualTo(owned.Count(asset => asset == null) + 1));
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
            // finish. The lines are read before the comparison so that a revert which deleted the file
            // reports as a missing record rather than throwing out of the tuple's second element.
            var kept = File.Exists(RecordFilePath()) ? File.ReadAllLines(RecordFilePath()) : null;
            Assert.That(
                (kept != null, string.Join(", ", kept ?? System.Array.Empty<string>())),
                Is.EqualTo((true, "Packages/com.velvet.core/Runtime/Assets/NoSuchHolder.asset")));
        }

        [Test]
        public void Given_ARecordFromABuildThatNeverAddedTheEntry_When_TheRevertRuns_Then_TheRecordIsGone()
        {
            // Arrange — a record naming a holder that resolves, with no matching entry in the list: the
            // consumer deleted the entry themselves while the record was on disk. The session marker is
            // erased here rather than inherited from whatever ran before, because the repair only reverts
            // when it is empty and this case would otherwise pass or fail on execution order.
            File.WriteAllLines(RecordFilePath(), new[] { RuntimeAssetsPath() });
            SessionState.EraseString(LiveSessionKey());
            var entryPresent = !BundledStyleSheetBuildInclusion.Unreached();

            // Act — what an editor load runs.
            BundledStyleSheetBuildInclusion.RevertWhatAnEndedSessionLeft();

            // Assert — the arranged state rides along: the entry really was absent, so the record named
            // nothing to remove. Kept, it would be kept forever — nothing but a completed build clears it.
            Assert.That((entryPresent, File.Exists(RecordFilePath())), Is.EqualTo((false, false)));
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
