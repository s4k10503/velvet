using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// CSS <c>clip-path</c> changes nothing about layout, so a clipped element has to sit exactly where the same
    /// element without the clip does. Each case mounts two identical hosts — a sibling before the card, the card,
    /// a sibling after it — clips the card in the second, and compares the card and the trailing sibling across
    /// the two, host-relative. The unclipped twin is the reference, laid out by the same engine in the same
    /// panel, so no case carries a hand-computed position. GWT, one assert per case.
    /// </summary>
    [TestFixture]
    internal sealed class ClipPathWrapperFlowParityPanelTests : PanelTestBase
    {
        private const string Triangle = "clip-path-[polygon(50%_0%,100%_100%,0%_100%)]";
        private const string FixedCard = "w-[100px] h-[50px]";

        protected override void LoadStyleSheets() => VelvetStyleUtilities.AttachTo(_window.rootVisualElement);

        private static VNode Host(string suffix, string hostClass, string cardClass) =>
            V.Div(name: "host" + suffix, className: "w-[400px] h-[300px] " + hostClass, children: new VNode[]
            {
                V.Div(className: "w-[20px] h-[20px]"),
                V.Div(name: "card" + suffix, className: cardClass),
                V.Div(name: "after" + suffix, className: "w-[20px] h-[20px]"),
            });

        private VisualElement Named(string name) => _window.rootVisualElement.Q<VisualElement>(name);

        private Rect RelativeToHost(string host, string element)
        {
            var h = Named(host).worldBound;
            var e = Named(element).worldBound;
            return new Rect(e.x - h.x, e.y - h.y, e.width, e.height);
        }

        private static bool IsClipWrapped(VisualElement element)
            => element.parent != null
                && element.parent.ClassListContains(FiberWrapperElementAppliers.ClipPathWrapperClass);

        // Returns (reference, measured): the unclipped twin's card and trailing sibling beside the clipped twin's,
        // with the clipped card's wrap state leading each so a clip that never wrapped cannot pass as parity.
        private ((bool, Rect, Rect) Reference, (bool, Rect, Rect) Clipped) MountTwins(string hostClass, string cardClass)
        {
            _mounted = V.Mount(_window.rootVisualElement, V.Div(children: new VNode[]
            {
                Host("0", hostClass, cardClass),
                Host("1", hostClass, cardClass + " " + Triangle),
            }));
            ForcePanelUpdate(_window.rootVisualElement.panel);
            return (
                (true, RelativeToHost("host0", "card0"), RelativeToHost("host0", "after0")),
                (IsClipWrapped(Named("card1")), RelativeToHost("host1", "card1"), RelativeToHost("host1", "after1")));
        }

        private static StateUpdater<int> s_setStep;
        private static System.Func<int, string> s_hostClassFor;
        private static System.Func<int, string> s_cardClassFor;
        private static System.Func<int, string> s_clipFor;

        [SetUp]
        public override void SetUp()
        {
            base.SetUp();
            s_setStep = default;
            s_hostClassFor = _ => "";
            s_cardClassFor = _ => FixedCard;
            s_clipFor = _ => Triangle;
        }

        // Both hosts and both cards take their classes from the current step, so advancing it patches the clipped
        // twin and the unclipped one alike.
        [Component]
        private static VNode RenderTwins()
        {
            var (step, setStep) = Hooks.UseState(0);
            s_setStep = setStep;
            return V.Div(children: new VNode[]
            {
                Host("0", s_hostClassFor(step), s_cardClassFor(step)),
                Host("1", s_hostClassFor(step), s_cardClassFor(step) + " " + s_clipFor(step)),
            });
        }

        private void MountSteppedTwins()
        {
            _mounted = V.Mount(_window.rootVisualElement, V.Component(RenderTwins));
            ForcePanelUpdate(_window.rootVisualElement.panel);
        }

        private void AdvanceStep()
        {
            s_setStep.Invoke(1);
            _mounted.GetSchedulerForTest().DrainImmediateForTest();
            SettlePanel();
        }

        // A layout pass, then one panel scheduler tick, then another layout pass: the panel's own frame runs its
        // scheduler before its style pass, so a change the scheduler applies lands in the pass after it.
        private void SettlePanel()
        {
            var panel = _window.rootVisualElement.panel;
            ForcePanelUpdate(panel);
            EditorPanelTestHelpers.DriveSchedulerOnce(panel);
            ForcePanelUpdate(panel);
        }

        private ((bool, Rect, Rect) Reference, (bool, Rect, Rect) Clipped) ReadTwins() => (
            (true, RelativeToHost("host0", "card0"), RelativeToHost("host0", "after0")),
            (IsClipWrapped(Named("card1")), RelativeToHost("host1", "card1"), RelativeToHost("host1", "after1")));

        [Test]
        public void Given_AFixedCardInAColumn_When_Clipped_Then_ItSitsWhereTheUnclippedCardDoes()
        {
            // Arrange
            var hostClass = "";
            var cardClass = FixedCard;

            // Act
            var (reference, clipped) = MountTwins(hostClass, cardClass);

            // Assert
            Assert.That(clipped, Is.EqualTo(reference));
        }

        [Test]
        public void Given_AFixedCardInAPaddedColumn_When_Clipped_Then_ItSitsWhereTheUnclippedCardDoes()
        {
            // Arrange
            var hostClass = "p-[12px]";
            var cardClass = FixedCard;

            // Act
            var (reference, clipped) = MountTwins(hostClass, cardClass);

            // Assert
            Assert.That(clipped, Is.EqualTo(reference));
        }

        [Test]
        public void Given_AFixedCardWithAMarginInAColumn_When_Clipped_Then_ItSitsWhereTheUnclippedCardDoes()
        {
            // Arrange
            var hostClass = "";
            var cardClass = "m-[6px] " + FixedCard;

            // Act
            var (reference, clipped) = MountTwins(hostClass, cardClass);

            // Assert
            Assert.That(clipped, Is.EqualTo(reference));
        }

        [Test]
        public void Given_AFixedCardInARowWithAGap_When_Clipped_Then_ItSitsWhereTheUnclippedCardDoes()
        {
            // Arrange
            var hostClass = "flex-row gap-[10px]";
            var cardClass = FixedCard;

            // Act
            var (reference, clipped) = MountTwins(hostClass, cardClass);

            // Assert
            Assert.That(clipped, Is.EqualTo(reference));
        }

        [Test]
        public void Given_AFixedCardInAReversedRow_When_Clipped_Then_ItSitsWhereTheUnclippedCardDoes()
        {
            // Arrange
            var hostClass = "flex-row-reverse";
            var cardClass = FixedCard;

            // Act
            var (reference, clipped) = MountTwins(hostClass, cardClass);

            // Assert
            Assert.That(clipped, Is.EqualTo(reference));
        }

        [Test]
        public void Given_ACardAlignedToTheEndBySelfInAStartAlignedColumn_When_Clipped_Then_ItSitsWhereTheUnclippedCardDoes()
        {
            // Arrange
            var hostClass = "items-start";
            var cardClass = "self-end " + FixedCard;

            // Act
            var (reference, clipped) = MountTwins(hostClass, cardClass);

            // Assert
            Assert.That(clipped, Is.EqualTo(reference));
        }

        [Test]
        public void Given_ACardWithAPercentageCrossSize_When_Clipped_Then_ItSitsWhereTheUnclippedCardDoes()
        {
            // Arrange
            var hostClass = "";
            var cardClass = "w-[50%] h-[40px]";

            // Act
            var (reference, clipped) = MountTwins(hostClass, cardClass);

            // Assert
            Assert.That(clipped, Is.EqualTo(reference));
        }

        [Test]
        public void Given_ACardStretchedAcrossAColumn_When_Clipped_Then_ItSitsWhereTheUnclippedCardDoes()
        {
            // Arrange
            var hostClass = "";
            var cardClass = "h-[40px]";

            // Act
            var (reference, clipped) = MountTwins(hostClass, cardClass);

            // Assert
            Assert.That(clipped, Is.EqualTo(reference));
        }

        [Test]
        public void Given_AGrowingCardInARow_When_Clipped_Then_ItSitsWhereTheUnclippedCardDoes()
        {
            // Arrange
            var hostClass = "flex-row";
            var cardClass = "grow h-[40px]";

            // Act
            var (reference, clipped) = MountTwins(hostClass, cardClass);

            // Assert
            Assert.That(clipped, Is.EqualTo(reference));
        }

        [Test]
        public void Given_AClippedCardInARow_When_AVariantTurnsTheParentIntoAColumn_Then_ItSitsWhereTheUnclippedCardDoes()
        {
            // Arrange: a card with no width is stretched across a column and not across a row, so it is sized by
            // the direction the parent has after the toggle. The toggle lands on the parent, so the clipped card
            // is neither patched nor handed a variant payload of its own.
            s_hostClassFor = _ => "flex-row hover:flex-col";
            s_cardClassFor = _ => "h-[40px]";
            MountSteppedTwins();

            // Act
            foreach (var name in new[] { "host0", "host1" })
            {
                using var over = PointerOverEvent.GetPooled();
                Named(name).SimulateEvent(over);
            }
            SettlePanel();
            var (reference, clipped) = ReadTwins();

            // Assert
            Assert.That(clipped, Is.EqualTo(reference));
        }

        [Test]
        public void Given_ACardInAColumn_When_SelfEndReplacesSelfStartByPatch_Then_ItSitsWhereTheUnclippedCardDoes()
        {
            // Arrange
            s_cardClassFor = step => (step == 0 ? "self-start " : "self-end ") + FixedCard;
            MountSteppedTwins();

            // Act
            AdvanceStep();
            var (reference, clipped) = ReadTwins();

            // Assert
            Assert.That(clipped, Is.EqualTo(reference));
        }

        [Test]
        public void Given_ASelfStartCard_When_TheClipAndSelfEndAreAddedByOnePatch_Then_ItSitsWhereTheUnclippedCardDoes()
        {
            // Arrange
            s_cardClassFor = step => (step == 0 ? "self-start " : "self-end ") + FixedCard;
            s_clipFor = step => step == 0 ? "" : Triangle;
            MountSteppedTwins();

            // Act
            s_setStep.Invoke(1);
            _mounted.GetSchedulerForTest().DrainImmediateForTest();
            ForcePanelUpdate(_window.rootVisualElement.panel);
            var (reference, clipped) = ReadTwins();

            // Assert
            Assert.That(clipped, Is.EqualTo(reference));
        }

        [Test]
        public void Given_ACardInARow_When_GrowIsAddedByPatch_Then_ItSitsWhereTheUnclippedCardDoes()
        {
            // Arrange
            s_hostClassFor = _ => "flex-row";
            s_cardClassFor = step => (step == 0 ? "" : "grow ") + "h-[40px]";
            MountSteppedTwins();

            // Act
            AdvanceStep();
            var (reference, clipped) = ReadTwins();

            // Assert
            Assert.That(clipped, Is.EqualTo(reference));
        }

        [Test]
        public void Given_ASelfStartCardWithAHoverSelfEnd_When_BothTwinsAreHovered_Then_ItSitsWhereTheUnclippedCardDoes()
        {
            // Arrange
            s_cardClassFor = _ => "self-start hover:self-end " + FixedCard;
            MountSteppedTwins();

            // Act
            foreach (var name in new[] { "card0", "card1" })
            {
                using var over = PointerOverEvent.GetPooled();
                Named(name).SimulateEvent(over);
            }
            SettlePanel();
            var (reference, clipped) = ReadTwins();

            // Assert
            Assert.That(clipped, Is.EqualTo(reference));
        }

        [Test]
        public void Given_AnAbsoluteCardWithNoOffsetsInAnEndAlignedHost_When_Clipped_Then_ItSitsWhereTheUnclippedCardDoes()
        {
            // Arrange
            var hostClass = "justify-end items-end";
            var cardClass = "absolute w-[50px] h-[40px]";

            // Act
            var (reference, clipped) = MountTwins(hostClass, cardClass);

            // Assert
            Assert.That(clipped, Is.EqualTo(reference));
        }
    }
}
