using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;
using UnityEngine.Rendering;
using Velvet.Editor;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins the mechanism that puts the package's shaders into a player build: that it reaches every shader
    /// the package ships, and that the consumer's Graphics Settings read afterwards exactly as before.
    /// </summary>
    /// <remarks>
    /// Whether a shader listed in Always Included Shaders then survives into a player is not something an
    /// editor run can answer, because <c>Shader.Find</c> resolves a package shader here either way.
    /// <c>BundledShaderPlayerInclusionTests</c> is the case that answers it, and only when the suite runs with
    /// <c>-testPlatform StandaloneOSX</c>.
    /// </remarks>
    [TestFixture]
    internal sealed class BundledShaderInclusionTests
    {
        private const string RuntimeRoot = "Packages/com.velvet.core/Runtime";
        private const string GraphicsSettingsAsset = "ProjectSettings/GraphicsSettings.asset";
        private const string AlwaysIncludedShaders = "m_AlwaysIncludedShaders";

        private static readonly Regex DeclaredNamePattern =
            new(@"^\s*Shader\s+""([^""]+)""", RegexOptions.Multiline | RegexOptions.Compiled);

        // Revert leaves the record in place when a recorded name no longer resolves, which is the behaviour
        // one case here arranges. Deleting it afterwards is what stops that case handing the next one a record
        // it cannot act on.
        [TearDown]
        public void TearDown()
        {
            BundledShaderBuildInclusion.Revert();
            File.Delete(RecordFilePath());
        }

        private static SerializedProperty IncludedShaders(out SerializedObject settings)
        {
            settings = new SerializedObject(
                AssetDatabase.LoadAssetAtPath<GraphicsSettings>(GraphicsSettingsAsset));
            return settings.FindProperty(AlwaysIncludedShaders);
        }

        private static string Unreached() => string.Join(", ", BundledShaderBuildInclusion.Unreached());

        [Test]
        public void Given_ThePackagesRuntimeTree_When_ItsShaderFilesAreRead_Then_VelvetShadersNamesEveryOne()
        {
            // Arrange — the walk's size is folded in below, because an empty walk would otherwise match an
            // empty list of names and pass having read nothing.
            var files = Directory.GetFiles(RuntimeRoot, "*.shader", SearchOption.AllDirectories);

            // Act
            var declared = files
                .Select(path => DeclaredNamePattern.Match(File.ReadAllText(path)))
                .Select(match => match.Success ? match.Groups[1].Value : "<none>")
                .OrderBy(name => name, StringComparer.Ordinal);

            // Assert
            Assert.That(
                (files.Length > 0, string.Join(", ", declared)),
                Is.EqualTo((true, string.Join(", ", VelvetShaders.Names.OrderBy(n => n, StringComparer.Ordinal)))));
        }

        [Test]
        public void Given_ABuildAboutToRun_When_ThePreprocessInjects_Then_EveryBundledShaderIsAlwaysIncluded()
        {
            // Arrange — the reading taken before the injection is folded into the assertion, so a reader that
            // reported nothing unreached whatever the list held could not satisfy it.
            var injector = new BundledShaderBuildInclusion();
            var before = Unreached();

            // Act
            injector.OnPreprocessBuild(null);

            // Assert
            Assert.That((before, Unreached()), Is.EqualTo((string.Join(", ", VelvetShaders.Names), string.Empty)));
        }

        [Test]
        public void Given_AShaderTheProjectAlreadyListed_When_ABuildInjectsAndReverts_Then_ThatEntrySurvives()
        {
            // Arrange — a consumer's own entry, which reads exactly like an injected one and is told apart
            // only by the record the injection keeps of what it added.
            var included = IncludedShaders(out var settings);
            var owned = Shader.Find(VelvetShaders.DropShadow);
            included.InsertArrayElementAtIndex(included.arraySize);
            included.GetArrayElementAtIndex(included.arraySize - 1).objectReferenceValue = owned;
            settings.ApplyModifiedProperties();
            var injector = new BundledShaderBuildInclusion();

            // Act
            string survivors;
            try
            {
                injector.OnPreprocessBuild(null);
                injector.OnPostprocessBuild(null);
                survivors = Unreached();
            }
            finally
            {
                RemoveFirst(owned);
            }

            // Assert — every bundled shader but the pre-existing one is gone again, which is what separates a
            // revert that removed what it added from one that removed everything it recognised.
            Assert.That(
                survivors,
                Is.EqualTo(string.Join(", ", VelvetShaders.Names.Where(n => n != VelvetShaders.DropShadow))));
        }

        [Test]
        public void Given_ABuildThatInjectedAndReverted_When_TheSettingsFileIsReadBack_Then_ItIsWhatItWas()
        {
            // Arrange
            var injector = new BundledShaderBuildInclusion();
            var before = File.ReadAllText(GraphicsSettingsAsset);

            // Act — the save is what a build does to project settings while it runs, arranged here rather
            // than waited for, so the revert is asked to undo a change that is already on disk.
            injector.OnPreprocessBuild(null);
            AssetDatabase.SaveAssets();
            var whileInjected = File.ReadAllText(GraphicsSettingsAsset);
            injector.OnPostprocessBuild(null);

            // Assert — the injected reading is folded in because a revert that changed only the loaded
            // object would leave the diff sitting in the file.
            Assert.That(
                (whileInjected == before, File.ReadAllText(GraphicsSettingsAsset)),
                Is.EqualTo((false, before)));
        }

        [Test]
        public void Given_AReadOnlySettingsFile_When_ABuildWouldInject_Then_ItFailsInsteadOfWritingNothing()
        {
            // Arrange — an exclusive handle rather than the read-only attribute. The refusal no longer reads
            // that attribute, and on a checkout the process does not own the attribute answers for a permission
            // triad that is not the one in force: setting it changes nothing the build can see.
            var injector = new BundledShaderBuildInclusion();
            bool writableBefore;
            try
            {
                using var probe = File.Open(
                    GraphicsSettingsAsset, FileMode.Open, FileAccess.Write, FileShare.ReadWrite);
                writableBefore = true;
            }
            catch (Exception)
            {
                writableBefore = false;
            }

            // Act
            Exception refused = null;
            using (File.Open(GraphicsSettingsAsset, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
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
            // refusal proves nothing about the handle, and that is the state this case sat in unnoticed.
            Assert.That(
                (writableBefore, refused?.GetType()),
                Is.EqualTo((true, typeof(BuildFailedException))));
        }

        [Test]
        public void Given_ABuildThatDiedBeforeItsPostprocess_When_TheEditorLoadsAgain_Then_TheEntriesAreGone()
        {
            // Arrange — the record on disk outlives the editor; erasing the session marker is what a new
            // process looks like to the repair.
            var injector = new BundledShaderBuildInclusion();
            injector.OnPreprocessBuild(null);
            var injected = Unreached();
            SessionState.EraseString(LiveSessionKey());

            // Act
            BundledShaderBuildInclusion.RevertWhatAnEndedSessionLeft();

            // Assert — the injected reading is folded in, so a repair that ran against an empty list would
            // not satisfy it.
            Assert.That(
                (injected, Unreached()),
                Is.EqualTo((string.Empty, string.Join(", ", VelvetShaders.Names))));
        }

        [Test]
        public void Given_ABuildStillRunningInThisSession_When_ADomainReloadRepairs_Then_TheEntriesStay()
        {
            // Arrange
            var injector = new BundledShaderBuildInclusion();
            var before = Unreached();
            injector.OnPreprocessBuild(null);

            // Act — the reload a build can take between its two callbacks, which must not undo the injection.
            BundledShaderBuildInclusion.RevertWhatAnEndedSessionLeft();

            // Assert
            Assert.That((before, Unreached()), Is.EqualTo((string.Join(", ", VelvetShaders.Names), string.Empty)));
        }

        [Test]
        public void Given_ARecordedNameWhoseEntryIsAlreadyGone_When_TheRevertRuns_Then_TheRecordIsGone()
        {
            // Arrange — a record naming shaders that resolve, with the list holding none of them: the
            // consumer opened Graphics Settings and removed the entries themselves while the record sat in
            // Library. The session marker is erased here rather than inherited from whatever ran before,
            // because the repair only reverts when it is empty.
            File.WriteAllLines(RecordFilePath(), VelvetShaders.Names);
            SessionState.EraseString(LiveSessionKey());
            var entriesPresent = Unreached().Length < VelvetShaders.Names.Length;

            // Act — what an editor load runs.
            BundledShaderBuildInclusion.RevertWhatAnEndedSessionLeft();

            // Assert — the arranged state rides along: none of the recorded names was in the list, so the
            // record named nothing to remove. Kept, it would be kept forever, and every reload until a
            // build completes would rewrite and save the consumer's project settings for nothing.
            Assert.That((entriesPresent, File.Exists(RecordFilePath())), Is.EqualTo((false, false)));
        }

        [Test]
        public void Given_AnUnwritableSettingsFile_When_TheRecordNamesNothingToRemove_Then_TheRecordIsStillGone()
        {
            // Arrange — the same finished record as the case above, on a project whose settings file cannot
            // be written. A Perforce checkout is the ordinary way to get here, and it is where a consumer
            // is likeliest to have removed the entries by hand in the first place.
            File.WriteAllLines(RecordFilePath(), VelvetShaders.Names);
            SessionState.EraseString(LiveSessionKey());

            // Act
            using (File.Open(GraphicsSettingsAsset, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                BundledShaderBuildInclusion.RevertWhatAnEndedSessionLeft();
            }

            // Assert — a pass that removes nothing writes nothing, so it has no reason to ask whether it
            // could. Asking anyway is what left the record here forever.
            Assert.That(File.Exists(RecordFilePath()), Is.False);
        }

        [Test]
        public void Given_ARecordedNameThatNoLongerResolves_When_TheRevertRuns_Then_ItKeepsTheRecord()
        {
            // Arrange — a build died leaving a record, and by the time it is read one recorded name resolves
            // to nothing: the package renamed or dropped that shader in the meantime. Deleting the record then
            // is what makes the leftover entry permanent, because injection is additive and would read it as
            // the consumer's own from that point on.
            var injector = new BundledShaderBuildInclusion();
            injector.OnPreprocessBuild(null);
            File.AppendAllLines(RecordFilePath(), new[] { "Velvet/NoSuchShader" });

            // Act
            BundledShaderBuildInclusion.Revert();

            // Assert — the record survives carrying exactly what could not be removed, so a later pass can
            // finish. Both terms in one comparison: a revert that deleted the file and one that left it
            // holding the whole original list are different failures.
            var kept = File.Exists(RecordFilePath()) ? File.ReadAllLines(RecordFilePath()) : null;
            Assert.That(
                (kept != null, string.Join(", ", kept ?? System.Array.Empty<string>())),
                Is.EqualTo((true, "Velvet/NoSuchShader")));
        }

        private static string RecordFilePath()
            => (string)typeof(BundledShaderBuildInclusion)
                .GetField("RecordFile", BindingFlags.NonPublic | BindingFlags.Static)!
                .GetRawConstantValue()!;

        private static string LiveSessionKey()
            => (string)typeof(BundledShaderBuildInclusion)
                .GetField("LiveSessionKey", BindingFlags.NonPublic | BindingFlags.Static)!
                .GetRawConstantValue()!;

        private static void RemoveFirst(Shader shader)
        {
            var included = IncludedShaders(out var settings);
            for (var i = 0; i < included.arraySize; i++)
            {
                if (included.GetArrayElementAtIndex(i).objectReferenceValue == shader)
                {
                    included.GetArrayElementAtIndex(i).objectReferenceValue = null;
                    included.DeleteArrayElementAtIndex(i);
                    break;
                }
            }
            settings.ApplyModifiedProperties();
        }
    }
}
