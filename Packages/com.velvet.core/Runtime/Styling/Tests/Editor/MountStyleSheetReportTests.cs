using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Velvet.TestUtilities;
using Object = UnityEngine.Object;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins the warning <see cref="V.Mount(VisualElement, VNode)"/> and a portal into an element the app owns
    /// raise when their target's panel is about to resolve styles without the bundled utility stylesheet, and
    /// that it stays quiet wherever the sheet does reach by then.
    /// </summary>
    /// <remarks>
    /// The warning is raised once per run, so a case that expects none mounts a sheetless target last and
    /// asserts that one alone was named: a false report on the case's own target would have taken the only
    /// slot, which is what makes an absence observable here. <see cref="Tick"/> runs the panels in the order
    /// they were made, so the case's own target is looked at before that control.
    /// </remarks>
    [TestFixture]
    internal sealed class MountStyleSheetReportTests
    {
        private const string ImportingSheetPath =
            "Packages/com.velvet.core/Runtime/Styling/Tests/Editor/ImportsVelvetUtilities.uss";

        private const string PartialSheetPath = "Packages/com.velvet.core/Runtime/Styles/_palette.uss";

        private static readonly Regex ReportedTarget = new(@"target '([^']*)' is on a panel that does not carry");

        private static readonly FieldInfo MissingReported = typeof(VelvetStyleUtilities)
            .GetField("s_missingReported", BindingFlags.NonPublic | BindingFlags.Static);

        private static readonly MethodInfo RearmMissingReport = typeof(VelvetStyleUtilities)
            .GetMethod("RearmMissingReport", BindingFlags.NonPublic | BindingFlags.Static);

        private readonly List<string> _reported = new();
        private readonly List<HeadlessEditorPanelHost> _hosts = new();
        private readonly List<MountedTree> _mounts = new();
        private object _reportedBefore;

        [SetUp]
        public void SetUp()
        {
            _reportedBefore = MissingReported?.GetValue(null);
            MissingReported?.SetValue(null, false);
            Application.logMessageReceived += Record;
        }

        [TearDown]
        public void TearDown()
        {
            Application.logMessageReceived -= Record;
            foreach (var mount in _mounts) mount.Dispose();
            foreach (var host in _hosts) host.Dispose();
            _mounts.Clear();
            _hosts.Clear();
            _reported.Clear();
            MissingReported?.SetValue(null, _reportedBefore);
        }

        private void Record(string condition, string stackTrace, LogType type)
        {
            var match = ReportedTarget.Match(condition);
            if (type == LogType.Warning && match.Success) _reported.Add(match.Groups[1].Value);
        }

        private VisualElement PanelRoot()
        {
            var host = new HeadlessEditorPanelHost();
            _hosts.Add(host);
            return host.Root;
        }

        private static VisualElement Named(string name) => new() { name = name };

        private void Mount(VisualElement target) => Mount(target, V.Div());

        private void Mount(VisualElement target, VNode tree) => _mounts.Add(V.Mount(target, tree));

        // The scheduler tick a live panel runs ahead of its style pass.
        private void Tick()
        {
            foreach (var host in _hosts) EditorPanelTestHelpers.DriveSchedulerOnce(host.Panel);
        }

        private void MountBare()
        {
            var root = PanelRoot();
            var bare = Named("bare");
            root.Add(bare);
            Mount(bare);
        }

        // GREEN_ON_BASE(characterization): the base reports this target at the mount, ahead of the tick.
        [Test]
        public void Given_APanelWithoutTheSheet_When_ATreeIsMountedOnIt_Then_TheTargetIsReported()
        {
            // Arrange
            var bare = Named("bare");
            PanelRoot().Add(bare);

            // Act
            Mount(bare);
            Tick();

            // Assert
            Assert.That(string.Join(", ", _reported), Is.EqualTo("bare"));
        }

        // GREEN_ON_BASE(characterization): the base already reports once per run.
        [Test]
        public void Given_TwoPanelsWithoutTheSheet_When_ATreeIsMountedOnEach_Then_OnlyTheFirstIsReported()
        {
            // Arrange
            var first = Named("first");
            PanelRoot().Add(first);

            // Act
            Mount(first);
            MountBare();
            Tick();

            // Assert
            Assert.That(string.Join(", ", _reported), Is.EqualTo("first"));
        }

        // GREEN_ON_BASE(characterization): the base already finds the sheet on an ancestor.
        [Test]
        public void Given_TheSheetOnThePanelRoot_When_ATreeIsMountedBelowIt_Then_OnlyTheBareControlIsReported()
        {
            // Arrange — the sheet sits on an ancestor rather than on the target, where AttachTo at the panel root
            // puts it.
            var root = PanelRoot();
            root.styleSheets.Add(VelvetStyleUtilities.Sheet);
            var carried = Named("carried");
            root.Add(carried);

            // Act
            Mount(carried);
            MountBare();
            Tick();

            // Assert
            Assert.That(string.Join(", ", _reported), Is.EqualTo("bare"));
        }

        // GREEN_ON_BASE(characterization): the base already follows an @import.
        [Test]
        public void Given_ASheetThatImportsTheUtilities_When_ATreeIsMountedOnIt_Then_OnlyTheBareControlIsReported()
        {
            // Arrange — the importing sheet sits on the target itself, so the utilities reach it through the
            // @import alone and not through any ancestor.
            var imported = Named("imported");
            imported.styleSheets.Add(AssetDatabase.LoadAssetAtPath<StyleSheet>(ImportingSheetPath));
            PanelRoot().Add(imported);

            // Act
            Mount(imported);
            MountBare();
            Tick();

            // Assert
            Assert.That(string.Join(", ", _reported), Is.EqualTo("bare"));
        }

        // GREEN_ON_BASE(characterization): the base already declines a partial for the whole sheet.
        [Test]
        public void Given_OnlyASheetTheUtilitiesImport_When_ATreeIsMountedOnIt_Then_TheTargetIsReported()
        {
            // Arrange — one of the partials the utility sheet imports, which is a sheet on the chain that is not
            // the utilities and imports nothing that is.
            var partial = Named("partial");
            partial.styleSheets.Add(AssetDatabase.LoadAssetAtPath<StyleSheet>(PartialSheetPath));
            PanelRoot().Add(partial);

            // Act
            Mount(partial);
            Tick();

            // Assert
            Assert.That(string.Join(", ", _reported), Is.EqualTo("partial"));
        }

        // GREEN_ON_BASE(characterization): the base already re-arms the report for a play session.
        [Test]
        public void Given_AReportAlreadyMade_When_APlaySessionStarts_Then_TheNextBareMountIsReportedAgain()
        {
            // Arrange
            MissingReported?.SetValue(null, true);

            // Act — what entering play mode runs.
            RearmMissingReport?.Invoke(null, null);
            MountBare();
            Tick();

            // Assert
            Assert.That(string.Join(", ", _reported), Is.EqualTo("bare"));
        }

        // GREEN_ON_BASE(characterization): the base already matches a copy by its name.
        [Test]
        public void Given_ACopyOfTheSheetOnThePanelRoot_When_ATreeIsMountedBelowIt_Then_OnlyTheBareControlIsReported()
        {
            // Arrange — a copy carrying the asset's name, as an asset bundle holds one beside the sheet the holder
            // resolves.
            var copy = Object.Instantiate(VelvetStyleUtilities.Sheet);
            copy.name = VelvetStyleUtilities.Sheet.name;
            var root = PanelRoot();
            root.styleSheets.Add(copy);
            var copied = Named("copied");
            root.Add(copied);

            // Act
            try
            {
                Mount(copied);
                MountBare();
                Tick();
            }
            finally
            {
                Object.DestroyImmediate(copy);
            }

            // Assert
            Assert.That(string.Join(", ", _reported), Is.EqualTo("bare"));
        }

        // GREEN_ON_BASE(characterization): the base already stops watching a disposed mount's target.
        [Test]
        public void Given_AMountDisposedOffAnyPanel_When_ItsTargetIsAddedToOneWithoutTheSheet_Then_OnlyTheBareControlIsReported()
        {
            // Arrange
            var disposed = Named("disposed");
            V.Mount(disposed, V.Div()).Dispose();

            // Act
            PanelRoot().Add(disposed);
            MountBare();
            Tick();

            // Assert
            Assert.That(string.Join(", ", _reported), Is.EqualTo("bare"));
        }

        // GREEN_ON_BASE(characterization): the base already checks a target when it arrives on a panel.
        [Test]
        public void Given_ATargetMountedOffAnyPanel_When_ItIsAddedToOneWithoutTheSheet_Then_ItIsReported()
        {
            // Arrange
            var late = Named("late");
            Mount(late);

            // Act
            PanelRoot().Add(late);
            Tick();

            // Assert
            Assert.That(string.Join(", ", _reported), Is.EqualTo("late"));
        }

        // GREEN_ON_BASE(characterization): the base already checks a target again on each arrival.
        [Test]
        public void Given_ATargetCheckedOnAPanelWithTheSheet_When_ItMovesToOneWithout_Then_ItIsReported()
        {
            // Arrange — the first panel's tick consumes the check the mount scheduled.
            var carrying = PanelRoot();
            carrying.styleSheets.Add(VelvetStyleUtilities.Sheet);
            var moved = Named("moved");
            carrying.Add(moved);
            Mount(moved);
            Tick();

            // Act
            PanelRoot().Add(moved);
            Tick();

            // Assert
            Assert.That(string.Join(", ", _reported), Is.EqualTo("moved"));
        }

        [Test]
        public void Given_TheSheetAttachedAfterTheMount_When_ThePanelTicks_Then_OnlyTheBareControlIsReported()
        {
            // Arrange
            var root = PanelRoot();
            var attachedLate = Named("attached-late");
            root.Add(attachedLate);
            Mount(attachedLate);

            // Act
            root.styleSheets.Add(VelvetStyleUtilities.Sheet);
            MountBare();
            Tick();

            // Assert
            Assert.That(string.Join(", ", _reported), Is.EqualTo("bare"));
        }

        [Test]
        public void Given_TwoMountsOnATargetOffAnyPanel_When_OneIsDisposedAndTheTargetIsAddedToABarePanel_Then_ItIsReported()
        {
            // Arrange
            var shared = Named("shared");
            Mount(shared);
            V.Mount(shared, V.Div()).Dispose();

            // Act
            PanelRoot().Add(shared);
            Tick();

            // Assert
            Assert.That(string.Join(", ", _reported), Is.EqualTo("shared"));
        }

        [Test]
        public void Given_APortalIntoAnElementOnAPanelWithoutTheSheet_When_ItMounts_Then_ThatElementIsReported()
        {
            // Arrange — the declaring panel carries the sheet, so only the portal's target can be named.
            var declaring = PanelRoot();
            declaring.styleSheets.Add(VelvetStyleUtilities.Sheet);
            var elsewhere = Named("elsewhere");
            PanelRoot().Add(elsewhere);

            // Act
            Mount(declaring, V.Portal(elsewhere, new VNode[] { V.Div() }));
            Tick();

            // Assert
            Assert.That(string.Join(", ", _reported), Is.EqualTo("elsewhere"));
        }

        // GREEN_ON_BASE(characterization): the base looks at no portal target at all.
        [Test]
        public void Given_APortalIntoABareElementUnmountedBeforeItsPanelTicks_When_ItTicks_Then_OnlyTheBareControlIsReported()
        {
            // Arrange
            var declaring = PanelRoot();
            declaring.styleSheets.Add(VelvetStyleUtilities.Sheet);
            var elsewhere = Named("elsewhere");
            PanelRoot().Add(elsewhere);
            V.Mount(declaring, V.Portal(elsewhere, new VNode[] { V.Div() })).Dispose();

            // Act
            MountBare();
            Tick();

            // Assert
            Assert.That(string.Join(", ", _reported), Is.EqualTo("bare"));
        }
    }
}
