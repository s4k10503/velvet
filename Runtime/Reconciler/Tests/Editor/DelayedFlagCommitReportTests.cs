using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins that the edit a delayed field is holding is not merely written to <c>value</c> when a render
    /// takes <c>isDelayed:</c> off, but REPORTED — the write goes through the notifying setter, so
    /// <c>onValueChanged:</c> receives the pending text.
    /// </summary>
    /// <remarks>
    /// A real panel is what separates the two: the setter dispatches only for an element with one, so a
    /// detached root — which is what <c>TextFieldInputPropTests</c> reconciles into — cannot tell a
    /// notifying write from a silent one. A panel also lets the ARRANGEMENT report, so each assertion
    /// carries the count taken before the render as well as what arrived after it; without that term a
    /// report the arrangement caused passes for the one the render owes, and the fixture then reads
    /// backwards — green with the commit silenced and red with it working.
    /// </remarks>
    [TestFixture]
    internal sealed class DelayedFlagCommitReportTests : PanelTestBase
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
        public void Given_AnEditADelayedFieldIsHolding_When_ALaterRenderDeclaresTheFlagFalse_Then_OnValueChangedReceivesIt()
        {
            // Arrange
            var reported = new List<string>();
            MountHoldingAnEdit(reported, out var oldTree);
            var newTree = new VNode[] { V.TextField(onValueChanged: reported.Add, isDelayed: false) };
            var whileHeld = reported.Count;

            // Act
            _reconciler.Reconcile(_root, oldTree, newTree);

            // Assert
            Assert.That((whileHeld, string.Join("|", reported)), Is.EqualTo((0, "typed")));
        }

        [Test]
        public void Given_AnEditADelayedFieldIsHolding_When_ALaterRenderDropsTheFlag_Then_OnValueChangedReceivesIt()
        {
            // Arrange
            var reported = new List<string>();
            MountHoldingAnEdit(reported, out var oldTree);
            var newTree = new VNode[] { V.TextField(onValueChanged: reported.Add) };
            var whileHeld = reported.Count;

            // Act
            _reconciler.Reconcile(_root, oldTree, newTree);

            // Assert
            Assert.That((whileHeld, string.Join("|", reported)), Is.EqualTo((0, "typed")));
        }

        // The state a delayed field is in mid-edit: text on the inner element, a value that has not
        // received it. Written without notifying, which is what typing does — ITextEdition.UpdateText
        // sends an InputEvent and sets the text silently. The notifying setter TextFieldInputPropTests
        // can use on a detached root would fire onValueChanged here, from the arrangement, and this
        // fixture would then measure a report no render made.
        private void MountHoldingAnEdit(List<string> reported, out VNode[] oldTree)
        {
            oldTree = new VNode[] { V.TextField(onValueChanged: reported.Add, isDelayed: true) };
            _reconciler.Reconcile(_root, Array.Empty<VNode>(), oldTree);
            var element = (TextField)_root.ElementAt(0);
            ((INotifyValueChanged<string>)(TextElement)element.textEdition).SetValueWithoutNotify("typed");
        }
    }
}
