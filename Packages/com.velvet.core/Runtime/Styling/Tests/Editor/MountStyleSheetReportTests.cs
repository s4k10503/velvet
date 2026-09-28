using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins the warning <see cref="V.Mount(VisualElement, VNode)"/> raises when its target reaches a panel that
    /// does not carry the bundled utility stylesheet, and that it stays quiet wherever the sheet does reach.
    /// </summary>
    /// <remarks>
    /// The warning is raised once per run, so a case that expects none mounts a sheetless target last and
    /// asserts that one alone was named: a false report on the case's own target would have taken the only
    /// slot, which is what makes an absence observable here.
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

        private void Mount(VisualElement target) => _mounts.Add(V.Mount(target, V.Div()));

        private void MountBare()
        {
            var root = PanelRoot();
            var bare = Named("bare");
            root.Add(bare);
            Mount(bare);
        }

        [Test]
        public void Given_APanelWithoutTheSheet_When_ATreeIsMountedOnIt_Then_TheTargetIsReported()
        {
            // Arrange
            var bare = Named("bare");
            PanelRoot().Add(bare);

            // Act
            Mount(bare);

            // Assert
            Assert.That(string.Join(", ", _reported), Is.EqualTo("bare"));
        }

        [Test]
        public void Given_TwoPanelsWithoutTheSheet_When_ATreeIsMountedOnEach_Then_OnlyTheFirstIsReported()
        {
            // Arrange
            var first = Named("first");
            PanelRoot().Add(first);

            // Act
            Mount(first);
            MountBare();

            // Assert
            Assert.That(string.Join(", ", _reported), Is.EqualTo("first"));
        }

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

            // Assert
            Assert.That(string.Join(", ", _reported), Is.EqualTo("bare"));
        }

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

            // Assert
            Assert.That(string.Join(", ", _reported), Is.EqualTo("bare"));
        }

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

            // Assert
            Assert.That(string.Join(", ", _reported), Is.EqualTo("partial"));
        }

        [Test]
        public void Given_AReportAlreadyMade_When_APlaySessionStarts_Then_TheNextBareMountIsReportedAgain()
        {
            // Arrange
            MissingReported?.SetValue(null, true);

            // Act — what entering play mode runs.
            RearmMissingReport?.Invoke(null, null);
            MountBare();

            // Assert
            Assert.That(string.Join(", ", _reported), Is.EqualTo("bare"));
        }

        [Test]
        public void Given_ATargetMountedOffAnyPanel_When_ItIsAddedToOneWithoutTheSheet_Then_ItIsReported()
        {
            // Arrange
            var late = Named("late");
            Mount(late);

            // Act
            PanelRoot().Add(late);

            // Assert
            Assert.That(string.Join(", ", _reported), Is.EqualTo("late"));
        }
    }
}
