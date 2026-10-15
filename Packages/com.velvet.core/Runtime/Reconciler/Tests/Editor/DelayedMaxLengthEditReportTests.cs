using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins that carrying a delayed field's uncommitted edit across a change of the engine's own limit does
    /// not REPORT it: <c>onValueChanged:</c> hears nothing until Enter or blur commits. A declared
    /// <c>maxLength:</c> never moves that limit on a field built without one, so the limit is moved here
    /// the way it still can be: by a render dropping a declaration over an element built with a narrower
    /// limit of its own.
    /// </summary>
    /// <remarks>
    /// A real panel is what separates a silent restore from a notifying one, for the reason
    /// <c>DelayedFlagCommitReportTests</c> gives; this reads the opposite verdict, since that fixture
    /// asserts a report the render owes and this one a report it must not make. The count taken before the
    /// render is part of the assertion for the same reason it is there.
    /// </remarks>
    [TestFixture]
    internal sealed class DelayedMaxLengthEditReportTests : PanelTestBase
    {
        internal sealed class BuiltWithThree : TextField
        {
            public BuiltWithThree()
            {
                isDelayed = true;
                maxLength = 3;
            }
        }

        internal sealed class BuiltWithZero : TextField
        {
            public BuiltWithZero()
            {
                isDelayed = true;
                maxLength = 0;
            }
        }

        private Reconciler _reconciler;
        private VisualElement _root;

        [SetUp]
        public override void SetUp()
        {
            base.SetUp();
            _reconciler = new Reconciler();
            _root = new VisualElement();
            _window.rootVisualElement.Add(_root);
        }

        [TearDown]
        public override void TearDown()
        {
            _reconciler?.Dispose();
            _reconciler = null;
            _root = null;
            base.TearDown();
        }

        private static FiberEventBinding[] Reporting(List<string> reported)
            => new FiberEventBinding[] { new ChangeEventBinding<string> { Handler = reported.Add } };

        private static FiberElementProps Declaring(int maxLength)
            => new() { TextField = new TextFieldSettings(MaxLength: maxLength) };

        // GREEN_ON_BASE(characterization): the base carries the edit across the same restore.
        // It pins that the branch, which keeps that carry for a limit the element was built with, reports
        // nothing either.
        [Test]
        public void Given_AnEditADelayedFieldIsHolding_When_ALaterRenderRestoresANarrowerBuiltLimit_Then_OnValueChangedHearsNothing()
        {
            // Arrange
            var reported = new List<string>();
            var oldTree = new VNode[] { V.Custom<BuiltWithThree>(props: Declaring(10), events: Reporting(reported)) };
            var newTree = new VNode[] { V.Custom<BuiltWithThree>(events: Reporting(reported)) };
            _reconciler.Reconcile(_root, Array.Empty<VNode>(), oldTree);
            var element = (TextField)_root.ElementAt(0);
            // Written without notifying, which is what typing does; see DelayedFlagCommitReportTests.
            ((INotifyValueChanged<string>)(TextElement)element.textEdition).SetValueWithoutNotify("abcd");
            var whileHeld = reported.Count;

            // Act
            _reconciler.Reconcile(_root, oldTree, newTree);

            // Assert — the shown text is folded in: silence alone is also what a restore that never
            // happened produces.
            Assert.That((whileHeld, string.Join("|", reported), element.text), Is.EqualTo((0, string.Empty, "abc")));
        }

        // GREEN_ON_BASE(characterization): the base cuts the held edit to a zero limit the same way, so it
        // reports nothing there either. It pins that the branch's restore treats zero as a bound.
        // Zero is a bound: the cut leaves nothing, and a cut skipped for it would leave the engine's own
        // cull to report the edit.
        [Test]
        public void Given_AnEditADelayedFieldIsHolding_When_ALaterRenderRestoresABuiltLimitOfZero_Then_OnValueChangedHearsNothing()
        {
            // Arrange
            var reported = new List<string>();
            var oldTree = new VNode[] { V.Custom<BuiltWithZero>(props: Declaring(10), events: Reporting(reported)) };
            var newTree = new VNode[] { V.Custom<BuiltWithZero>(events: Reporting(reported)) };
            _reconciler.Reconcile(_root, Array.Empty<VNode>(), oldTree);
            var element = (TextField)_root.ElementAt(0);
            ((INotifyValueChanged<string>)(TextElement)element.textEdition).SetValueWithoutNotify("ab");
            var whileHeld = reported.Count;

            // Act
            _reconciler.Reconcile(_root, oldTree, newTree);

            // Assert
            Assert.That((whileHeld, string.Join("|", reported), element.text), Is.EqualTo((0, string.Empty, string.Empty)));
        }
    }
}
