using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins that carrying a delayed field's uncommitted edit across a <c>maxLength:</c> change does not
    /// REPORT it: <c>onValueChanged:</c> hears nothing until Enter or blur commits.
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

        [Test]
        public void Given_AnEditADelayedFieldIsHolding_When_ALaterRenderChangesMaxLength_Then_OnValueChangedHearsNothing()
        {
            // Arrange
            var reported = new List<string>();
            var oldTree = new VNode[] { V.TextField(onValueChanged: reported.Add, isDelayed: true, maxLength: 10) };
            var newTree = new VNode[] { V.TextField(onValueChanged: reported.Add, isDelayed: true, maxLength: 3) };
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
    }
}
