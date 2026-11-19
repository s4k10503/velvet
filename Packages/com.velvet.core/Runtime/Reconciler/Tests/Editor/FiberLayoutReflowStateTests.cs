using System;
using System.Reflection;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins what <c>FiberLayoutReflow</c> answers about a panel it has laid out: whether the panel is as the last pass
    /// left it, that a caller can accept a later write as part of that pass, and that a layout is refused while the
    /// panel is inside its own layout computation or style traversal. The panel's own phase flags are stood in for by
    /// replacing the members the layout reads them through.
    /// </summary>
    [TestFixture]
    internal sealed class FiberLayoutReflowStateTests : PanelTestBase
    {
        private const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

        private sealed class StyleTraversalFlag
        {
            public bool Applying = true;
        }

        // GREEN_ON_BASE(characterization): the base answers a panel the layout just laid out as current.
        // Change `==` to `!=` in the `_version() == _laidOutVersion` term of IsCurrent and the answer is false.
        [Test]
        public void Given_APanelTheLayoutJustLaidOut_When_AskedWhetherItIsCurrent_Then_ItIs()
        {
            // Arrange
            var root = _window.rootVisualElement;
            var laidOut = FiberLayoutReflow.TryLayOut(root);

            // Act
            var current = FiberLayoutReflow.IsCurrent(root.panel);

            // Assert — the layout pass is carried by the value: where it did not run, the value is one true cannot equal.
            Assert.That(laidOut && current, Is.True);
        }

        // GREEN_ON_BASE(characterization): the base answers a panel whose version moved since the layout as not current.
        // Change `&&` to `||` in IsCurrent's `_laidOut && _version() == _laidOutVersion` and a laid-out panel reads
        // as current whatever its version.
        [Test]
        public void Given_APanelWhoseVersionMovedSinceItWasLaidOut_When_AskedWhetherItIsCurrent_Then_ItIsNot()
        {
            // Arrange
            var root = _window.rootVisualElement;
            var laidOut = FiberLayoutReflow.TryLayOut(root);
            var layout = LayoutOf(root.panel);
            SetVersionRecorded(layout, unchecked(GetVersionRecorded(layout) - 1u));

            // Act
            var current = FiberLayoutReflow.IsCurrent(root.panel);

            // Assert — the layout pass is carried by the value: where it did not run, the value is true, which false cannot equal.
            Assert.That(laidOut ? current : true, Is.False);
        }

        // GREEN_ON_BASE(characterization): the base reads a moved version as current once it is accepted.
        // Drop `_laidOutVersion = _version();` from Accept and the panel stays stale.
        [Test]
        public void Given_APanelWhoseVersionMovedSinceItWasLaidOut_When_TheMoveIsAccepted_Then_ItIsCurrent()
        {
            // Arrange
            var root = _window.rootVisualElement;
            var laidOut = FiberLayoutReflow.TryLayOut(root);
            var layout = LayoutOf(root.panel);
            SetVersionRecorded(layout, unchecked(GetVersionRecorded(layout) - 1u));
            var staleBefore = !FiberLayoutReflow.IsCurrent(root.panel);

            // Act
            FiberLayoutReflow.Accept(root.panel);

            // Assert — the layout and the stale reading before are carried by the value.
            Assert.That(laidOut && staleBefore && FiberLayoutReflow.IsCurrent(root.panel), Is.True);
        }

        // GREEN_ON_BASE(characterization): the base refuses a layout while the panel reports it is inside its own layout computation.
        // Drop `if (_duringLayout()) return false;` from EnsureLaidOut and the layout runs.
        [Test]
        public void Given_APanelInsideItsOwnLayoutComputation_When_ALayoutIsAskedFor_Then_ItIsRefused()
        {
            // Arrange
            var root = _window.rootVisualElement;
            var laidOut = FiberLayoutReflow.TryLayOut(root);
            var layout = LayoutOf(root.panel);
            Field(layout, "_laidOut").SetValue(layout, false);
            Field(layout, "_duringLayout").SetValue(layout, (Func<bool>)(() => true));

            // Act
            var refused = !FiberLayoutReflow.TryLayOut(root);

            // Assert — the earlier layout is carried by the value, as above.
            Assert.That(laidOut && refused, Is.True);
        }

        // GREEN_ON_BASE(characterization): the base refuses a layout while the panel is inside its style traversal.
        // Drop `if ((bool)_applyingStyles.GetValue(_styleUpdater)) return false;` from EnsureLaidOut and the layout
        // runs.
        [Test]
        public void Given_APanelInsideItsStyleTraversal_When_ALayoutIsAskedFor_Then_ItIsRefused()
        {
            // Arrange
            var root = _window.rootVisualElement;
            var laidOut = FiberLayoutReflow.TryLayOut(root);
            var layout = LayoutOf(root.panel);
            Field(layout, "_laidOut").SetValue(layout, false);
            var flag = new StyleTraversalFlag();
            Field(layout, "_styleUpdater").SetValue(layout, flag);
            Field(layout, "_applyingStyles").SetValue(layout, typeof(StyleTraversalFlag).GetField(nameof(StyleTraversalFlag.Applying)));

            // Act
            var refused = !FiberLayoutReflow.TryLayOut(root);

            // Assert — the earlier layout is carried by the value, as above.
            Assert.That(laidOut && refused, Is.True);
        }

        private static object LayoutOf(IPanel panel)
        {
            var table = typeof(FiberLayoutReflow).GetField("s_panels", Private)!.GetValue(null)!;
            var arguments = new object[] { panel, null };
            var found = (bool)table.GetType().GetMethod("TryGetValue")!.Invoke(table, arguments)!;
            return found ? arguments[1] : new object();
        }

        private static FieldInfo Field(object layout, string name)
            => layout.GetType().GetField(name, Private) ?? throw new MissingFieldException(layout.GetType().Name, name);

        private static uint GetVersionRecorded(object layout) => (uint)Field(layout, "_laidOutVersion").GetValue(layout)!;

        private static void SetVersionRecorded(object layout, uint value) => Field(layout, "_laidOutVersion").SetValue(layout, value);
    }
}
