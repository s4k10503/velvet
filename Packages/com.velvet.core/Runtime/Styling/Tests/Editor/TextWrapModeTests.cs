using System;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// text-balance and text-pretty set CSS's wrap mode and leave the collapse to what the text inherits:
    /// the leaf's inline white-space carries the wrapping counterpart of the nearest white-space class above
    /// the ask, and a white-space class nearer than the ask decides on its own. The text-balance manipulator
    /// runs when the later of the two classes is text-balance. The inline write is read directly, as
    /// <see cref="StyleTextEffectPanelTests"/> reads pre-line's, so no layout pass is needed.
    /// GWT, one assert per case.
    /// </summary>
    [TestFixture]
    internal sealed class TextWrapModeTests
    {
        [TearDown]
        public void TearDown() => VelvetTheme.IsDark = false;

        private static Label MountAndFindLabel(ReconcilerScope scope, VNode tree)
        {
            scope.Reconciler.Reconcile(scope.Root, Array.Empty<VNode>(), new[] { tree });
            return scope.Root.Q<Label>();
        }

        private static (StyleKeyword, WhiteSpace) InlineWhiteSpace(Label label) =>
            (label.style.whiteSpace.keyword, label.style.whiteSpace.value);

        [Test]
        public void Given_TextBalanceUnderPreWrap_When_Reconciled_Then_TheLabelKeepsPreservingAndWraps()
        {
            // Arrange
            using var scope = new ReconcilerScope();

            // Act
            var label = MountAndFindLabel(scope,
                V.Div(className: "whitespace-pre-wrap", V.Label(className: "text-balance", text: "a\n  b")));

            // Assert
            Assert.That(InlineWhiteSpace(label), Is.EqualTo((StyleKeyword.Undefined, WhiteSpace.PreWrap)));
        }

        [Test]
        public void Given_TextPrettyUnderPre_When_Reconciled_Then_TheLabelKeepsPreservingAndWraps()
        {
            // Arrange
            using var scope = new ReconcilerScope();

            // Act
            var label = MountAndFindLabel(scope,
                V.Div(className: "whitespace-pre", V.Label(className: "text-pretty", text: "a\n  b")));

            // Assert
            Assert.That(InlineWhiteSpace(label), Is.EqualTo((StyleKeyword.Undefined, WhiteSpace.PreWrap)));
        }

        [Test]
        public void Given_TextBalanceUnderNowrap_When_Reconciled_Then_TheLabelWraps()
        {
            // Arrange
            using var scope = new ReconcilerScope();

            // Act
            var label = MountAndFindLabel(scope,
                V.Div(className: "whitespace-nowrap", V.Label(className: "text-balance", text: "a b")));

            // Assert
            Assert.That(InlineWhiteSpace(label), Is.EqualTo((StyleKeyword.Undefined, WhiteSpace.Normal)));
        }

        [Test]
        public void Given_TextBalanceOnAContainer_When_Reconciled_Then_ItsTextLeafWraps()
        {
            // Arrange
            using var scope = new ReconcilerScope();

            // Act
            var label = MountAndFindLabel(scope, V.Div(className: "text-balance", V.Text("a b")));

            // Assert
            Assert.That(InlineWhiteSpace(label), Is.EqualTo((StyleKeyword.Undefined, WhiteSpace.Normal)));
        }

        [Test]
        public void Given_ImportantTextBalanceUnderPreWrap_When_Reconciled_Then_TheLabelKeepsPreservingAndWraps()
        {
            // Arrange
            using var scope = new ReconcilerScope();

            // Act
            var label = MountAndFindLabel(scope,
                V.Div(className: "whitespace-pre-wrap", V.Label(className: "!text-balance", text: "a\n  b")));

            // Assert
            Assert.That(InlineWhiteSpace(label), Is.EqualTo((StyleKeyword.Undefined, WhiteSpace.PreWrap)));
        }

        [Test]
        public void Given_TwoWhiteSpaceClassesAboveTheAsk_When_Reconciled_Then_TheLaterDeclaredRuleGivesTheCollapse()
        {
            // Arrange — _typography.uss declares whitespace-pre after whitespace-normal, so pre is the one
            // the container resolves to whatever order the class list names them in.
            using var scope = new ReconcilerScope();

            // Act
            var label = MountAndFindLabel(scope,
                V.Div(className: "whitespace-pre whitespace-normal", V.Label(className: "text-balance", text: "a  b")));

            // Assert
            Assert.That(InlineWhiteSpace(label), Is.EqualTo((StyleKeyword.Undefined, WhiteSpace.PreWrap)));
        }

        [Test]
        public void Given_TwoWhiteSpaceClassesAtDifferentDepthsAboveTheAsk_When_Reconciled_Then_TheNearerGivesTheCollapse()
        {
            // Arrange
            using var scope = new ReconcilerScope();

            // Act
            var label = MountAndFindLabel(scope,
                V.Div(className: "whitespace-pre",
                    V.Div(className: "text-nowrap", V.Label(className: "text-balance", text: "a  b"))));

            // Assert
            Assert.That(InlineWhiteSpace(label), Is.EqualTo((StyleKeyword.Undefined, WhiteSpace.Normal)));
        }

        // GREEN_ON_BASE(characterization): the base writes no white-space for text-balance at all.
        [Test]
        public void Given_TextBalanceBesideAWhiteSpaceClass_When_Reconciled_Then_NothingIsWritten()
        {
            // Arrange
            using var scope = new ReconcilerScope();

            // Act
            var label = MountAndFindLabel(scope, V.Label(className: "whitespace-pre text-balance", text: "a  b"));

            // Assert
            Assert.That(label.style.whiteSpace.keyword, Is.EqualTo(StyleKeyword.Null));
        }

        // GREEN_ON_BASE(characterization): the base writes no white-space for text-balance at all.
        [Test]
        public void Given_AWhiteSpaceClassNearerThanTheAsk_When_Reconciled_Then_NothingIsWritten()
        {
            // Arrange
            using var scope = new ReconcilerScope();

            // Act
            var label = MountAndFindLabel(scope,
                V.Div(className: "text-balance", V.Div(className: "whitespace-normal", V.Text("a b"))));

            // Assert
            Assert.That(label.style.whiteSpace.keyword, Is.EqualTo(StyleKeyword.Null));
        }

        [TestCase("!text-balance", TestName = "Given_ALeadingImportantTextBalance_When_Reconciled_Then_ItBalances")]
        [TestCase("text-balance!", TestName = "Given_ATrailingImportantTextBalance_When_Reconciled_Then_ItBalances")]
        public void Given_AnImportantTextBalance_When_Reconciled_Then_ItBalances(string cls)
        {
            // Arrange
            using var scope = new ReconcilerScope();

            // Act
            MountAndFindLabel(scope, V.Label(className: cls, text: "hello"));

            // Assert
            Assert.That(scope.Reconciler.Context.TextBalanceManipulators.Count, Is.EqualTo(1));
        }

        [Test]
        public void Given_TextPrettyAfterTextBalance_When_Reconciled_Then_NothingBalances()
        {
            // Arrange
            using var scope = new ReconcilerScope();

            // Act
            MountAndFindLabel(scope, V.Label(className: "text-balance text-pretty", text: "hello"));

            // Assert
            Assert.That(scope.Reconciler.Context.TextBalanceManipulators.Count, Is.EqualTo(0));
        }

        // GREEN_ON_BASE(characterization): the base balances whenever text-balance is present.
        [Test]
        public void Given_TextBalanceAfterTextPretty_When_Reconciled_Then_ItBalances()
        {
            // Arrange
            using var scope = new ReconcilerScope();

            // Act
            MountAndFindLabel(scope, V.Label(className: "text-pretty text-balance", text: "hello"));

            // Assert
            Assert.That(scope.Reconciler.Context.TextBalanceManipulators.Count, Is.EqualTo(1));
        }

        [Test]
        public void Given_ADarkTextPrettyOverTextBalance_When_TheThemeTurnsDark_Then_TheBalanceStops()
        {
            // Arrange
            using var scope = new ReconcilerScope();
            MountAndFindLabel(scope, V.Label(className: "text-balance dark:text-pretty", text: "hello"));
            var before = scope.Reconciler.Context.TextBalanceManipulators.Count;

            // Act
            VelvetTheme.IsDark = true;

            // Assert
            Assert.That((before, scope.Reconciler.Context.TextBalanceManipulators.Count), Is.EqualTo((1, 0)));
        }

        // GREEN_ON_BASE(characterization): the base's text-balance claims no USS longhand, so no payload can
        // take it off the class list.
        [Test]
        public void Given_AChildVariantWhiteSpaceOverATextBalanceChild_When_Reconciled_Then_TheChildStillBalances()
        {
            // Arrange
            using var scope = new ReconcilerScope();

            // Act
            var label = MountAndFindLabel(scope,
                V.Div(className: "[&>*]:whitespace-normal", V.Label(className: "text-balance", text: "hello")));

            // Assert
            Assert.That((label.ClassListContains("text-balance"), scope.Reconciler.Context.TextBalanceManipulators.Count),
                Is.EqualTo((true, 1)));
        }
    }
}
