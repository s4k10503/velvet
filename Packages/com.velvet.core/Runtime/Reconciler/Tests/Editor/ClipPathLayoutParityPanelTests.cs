using System;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// CSS <c>clip-path</c> changes painting only, so a clipped element has to sit and size exactly as the same
    /// element without the clip does. The layout cases mount the two side by side in identical hosts and compare
    /// the clipped element's box, and the box of the sibling after it, with its unclipped twin's; the rest ask
    /// what the wrapper paints and animates. GWT, one assert per case.
    /// </summary>
    [TestFixture]
    internal sealed class ClipPathLayoutParityPanelTests : PanelTestBase
    {
        private const string Triangle = "clip-path-[polygon(50%_0%,100%_100%,0%_100%)]";

        private static StateUpdater<int> s_setStep;
        private static Func<int, string> s_cardClass;
        private static string s_hostClass;

        protected override void LoadStyleSheets() => VelvetStyleUtilities.AttachTo(_window.rootVisualElement);

        [SetUp]
        public override void SetUp()
        {
            base.SetUp();
            s_setStep = default;
            s_cardClass = _ => "";
            s_hostClass = "";
        }

        [Component]
        private static VNode RenderTwins()
        {
            var (step, setStep) = Hooks.UseState(0);
            s_setStep = setStep;
            var cardClass = s_cardClass(step);
            return V.Div(className: "flex-col", children: new VNode[]
            {
                Host("clip", cardClass + " " + Triangle),
                Host("plain", cardClass),
            });
        }

        private static VNode Host(string prefix, string cardClass)
            => V.Div(name: prefix + "-host", className: "relative shrink-0 w-[400px] h-[300px] " + s_hostClass,
                children: new VNode[]
                {
                    V.Div(name: prefix + "-card", className: cardClass),
                    V.Div(name: prefix + "-next", className: "w-[10px] h-[10px]"),
                });

        private VisualElement Named(string name) => _window.rootVisualElement.Q<VisualElement>(name);

        private void Mount(string hostClass, Func<int, string> cardClass)
        {
            s_hostClass = hostClass;
            s_cardClass = cardClass;
            _mounted = V.Mount(_window.rootVisualElement, V.Component(RenderTwins));
            ForcePanelUpdate(_window.rootVisualElement.panel);
        }

        private void Step(int n)
        {
            s_setStep.Invoke(n);
            _mounted.GetSchedulerForTest().DrainImmediateForTest();
            ForcePanelUpdate(_window.rootVisualElement.panel);
        }

        private static bool IsClipWrapped(VisualElement element)
            => element.parent != null
                && element.parent.ClassListContains(FiberWrapperElementAppliers.ClipPathWrapperClass);

        // Read from world bounds so the wrapper between host and element does not change the frame.
        private Rect RelativeToHost(string prefix, string part)
        {
            var host = Named(prefix + "-host").worldBound;
            var box = Named(prefix + "-" + part).worldBound;
            return new Rect(box.x - host.x, box.y - host.y, box.width, box.height);
        }

        private (bool Wrapped, Rect Card, Rect Next) Clipped()
            => (IsClipWrapped(Named("clip-card")), RelativeToHost("clip", "card"), RelativeToHost("clip", "next"));

        private (bool Wrapped, Rect Card, Rect Next) Twin()
            => (true, RelativeToHost("plain", "card"), RelativeToHost("plain", "next"));

        [TestCase("", "w-[100px] h-[50px]")]
        [TestCase("", "h-[50px]")]
        [TestCase("flex-row", "w-[100px] h-[50px]")]
        [TestCase("flex-row", "w-[100px]")]
        [TestCase("items-start", "w-[100px] h-[50px] self-end")]
        [TestCase("", "w-[50%] h-[50px]")]
        [TestCase("flex-row", "w-[50%] h-[50px]")]
        [TestCase("flex-row", "w-full h-[50px]")]
        [TestCase("flex-row", "basis-[50%] h-[50px]")]
        [TestCase("flex-row items-start", "w-[100px] h-[50%]")]
        [TestCase("items-start", "w-[100px] h-[50px] mx-auto")]
        [TestCase("", "w-[100px] h-[50px] m-[10px]")]
        [TestCase("flex-row", "grow h-[50px]")]
        [TestCase("", "max-w-[50%] h-[50px]")]
        [TestCase("", "w-[100px] aspect-[2/1]")]
        [TestCase("", "w-[100px] h-[50px] p-[8px] border-[2px]")]
        [TestCase("grid grid-cols-2", "h-[50px]")]
        [TestCase("", "flex flex-row flex-wrap gap-4 w-[200px] h-[100px]")]
        [TestCase("divide-y", "w-[100px] h-[50px] border-4 border-red-500")]
        public void Given_AClippedCard_When_LaidOut_Then_ItSitsWhereItsUnclippedTwinSits(string hostClass, string cardClass)
        {
            // Arrange
            Mount(hostClass, _ => cardClass);

            // Act
            var clipped = Clipped();

            // Assert
            Assert.That(clipped, Is.EqualTo(Twin()));
        }

        [Test]
        public void Given_AClippedCard_When_ItsWidthIsPatchedToAPercentageAndItsSelfEndDropped_Then_ItSitsWhereItsUnclippedTwinSits()
        {
            // Arrange
            Mount("flex-row", s => (s == 0 ? "w-[100px] self-end" : "w-[50%]") + " h-[50px]");

            // Act
            Step(1);

            // Assert
            Assert.That(Clipped(), Is.EqualTo(Twin()));
        }

        [Test]
        public void Given_ACard_When_AClipIsAddedByPatch_Then_ItSitsWhereItsUnclippedTwinSits()
        {
            // Arrange: the clip arrives with step 1, and the twin never carries it.
            s_hostClass = "items-start";
            _mounted = V.Mount(_window.rootVisualElement, V.Component(RenderPatchedClip));
            ForcePanelUpdate(_window.rootVisualElement.panel);

            // Act
            Step(1);

            // Assert
            Assert.That(Clipped(), Is.EqualTo(Twin()));
        }

        // GREEN_ON_BASE(characterization): the base restores an unwrapped element's layout, since it never moved it.
        // Dropping the hand-back in ClipPathLayoutBox.Release, or the parent check in ClipPathLayoutBox.Of that
        // keeps the later width off the dead wrapper, reddens this.
        [Test]
        public void Given_AClippedCard_When_TheClipIsRemovedAndTheWidthThenPatched_Then_ItSitsWhereItsUnclippedTwinSits()
        {
            // Arrange: the clip is on step 0 only.
            s_hostClass = "items-start";
            _mounted = V.Mount(_window.rootVisualElement, V.Component(RenderRemovedClip));
            ForcePanelUpdate(_window.rootVisualElement.panel);

            // Act
            Step(1);
            Step(2);

            // Assert
            Assert.That((Named("clip-card").parent == Named("clip-host"), RelativeToHost("clip", "card"),
                    RelativeToHost("clip", "next")),
                Is.EqualTo(Twin()));
        }

        [Component]
        private static VNode RenderPatchedClip()
        {
            var (step, setStep) = Hooks.UseState(0);
            s_setStep = setStep;
            const string card = "w-[50%] h-[50px] ml-[20px] self-end";
            return V.Div(className: "flex-col", children: new VNode[]
            {
                Host("clip", card + (step == 0 ? "" : " " + Triangle)),
                Host("plain", card),
            });
        }

        [Component]
        private static VNode RenderRemovedClip()
        {
            var (step, setStep) = Hooks.UseState(0);
            s_setStep = setStep;
            var card = (step < 2 ? "w-[50%]" : "w-[25%]") + " h-[50px] ml-[20px] self-end";
            return V.Div(className: "flex-col", children: new VNode[]
            {
                Host("clip", card + (step == 0 ? " " + Triangle : "")),
                Host("plain", card),
            });
        }

        [Component]
        private static VNode RenderWhileHover()
        {
            return V.Div(className: "flex-col", children: new VNode[]
            {
                HoverHost("clip", Triangle),
                HoverHost("plain", ""),
            });
        }

        private static VNode HoverHost(string prefix, string clip)
            => V.Div(name: prefix + "-host", className: "relative shrink-0 w-[400px] h-[300px] items-start",
                children: new VNode[]
                {
                    V.Div(name: prefix + "-card", className: "w-[100px] h-[50px] " + clip, whileHoverClass: "self-end"),
                    V.Div(name: prefix + "-next", className: "w-[10px] h-[10px]"),
                });

        private void Hover<TEvent>() where TEvent : EventBase<TEvent>, new()
        {
            foreach (var name in new[] { "clip-card", "plain-card" })
            {
                using var evt = EventBase<TEvent>.GetPooled();
                Named(name).SimulateEvent(evt);
            }
            ForcePanelUpdate(_window.rootVisualElement.panel);
        }

        [Test]
        public void Given_AClippedCardWithAWhileHoverSelfEnd_When_Hovered_Then_ItSitsWhereItsHoveredTwinSits()
        {
            // Arrange
            _mounted = V.Mount(_window.rootVisualElement, V.Component(RenderWhileHover));
            ForcePanelUpdate(_window.rootVisualElement.panel);

            // Act
            Hover<PointerOverEvent>();

            // Assert
            Assert.That(Clipped(), Is.EqualTo(Twin()));
        }

        [Test]
        public void Given_AHoveredClippedCardWithAWhileHoverSelfEnd_When_ThePointerLeaves_Then_ItSitsWhereItsTwinSits()
        {
            // Arrange
            _mounted = V.Mount(_window.rootVisualElement, V.Component(RenderWhileHover));
            ForcePanelUpdate(_window.rootVisualElement.panel);
            Hover<PointerOverEvent>();

            // Act
            Hover<PointerOutEvent>();

            // Assert
            Assert.That(Clipped(), Is.EqualTo(Twin()));
        }

        [Test]
        public void Given_AClippedCardWithAHoverWidth_When_Hovered_Then_ItSitsWhereItsHoveredTwinSits()
        {
            // Arrange
            Mount("flex-row", _ => "w-[100px] h-[50px] hover:w-full");

            // Act
            foreach (var name in new[] { "clip-card", "plain-card" })
            {
                using var over = PointerOverEvent.GetPooled();
                Named(name).SimulateEvent(over);
            }
            ForcePanelUpdate(_window.rootVisualElement.panel);

            // Assert
            Assert.That(Clipped(), Is.EqualTo(Twin()));
        }

        [Test]
        public void Given_AClippedCardWithAHoverSelfEnd_When_Hovered_Then_ItSitsWhereItsHoveredTwinSits()
        {
            // Arrange: self-end is a class rather than an inline value, so it reaches the wrapper by the class mirror.
            Mount("items-start", _ => "w-[100px] h-[50px] hover:self-end");

            // Act
            foreach (var name in new[] { "clip-card", "plain-card" })
            {
                using var over = PointerOverEvent.GetPooled();
                Named(name).SimulateEvent(over);
            }
            ForcePanelUpdate(_window.rootVisualElement.panel);

            // Assert
            Assert.That(Clipped(), Is.EqualTo(Twin()));
        }

        [Test]
        public void Given_AParentChildVariantPercentWidth_When_ItsChildIsClipped_Then_TheChildSitsWhereItsTwinSits()
        {
            // Arrange
            Mount("flex-row [&>*]:w-[50%]", _ => "h-[50px]");

            // Act
            var clipped = Clipped();

            // Assert
            Assert.That(clipped, Is.EqualTo(Twin()));
        }

        [Test]
        public void Given_AParentChildVariantBackground_When_ItsChildIsClipped_Then_TheChildCarriesIt()
        {
            // Arrange: a payload that paints belongs on the clipped child, inside the mask.
            Mount("[&>*]:bg-[#ff0000]", _ => "w-[100px] h-[50px]");

            // Act
            var card = Named("clip-card");

            // Assert
            Assert.That((IsClipWrapped(card), card.resolvedStyle.backgroundColor), Is.EqualTo((true, Color.red)));
        }

        [Test]
        public void Given_AClippedVirtualListRow_When_LaidOut_Then_ItIsAsTallAsItsUnclippedTwin()
        {
            // Arrange
            _mounted = V.Mount(_window.rootVisualElement, V.Div(className: "flex-row", children: new VNode[]
            {
                List("clip", Triangle),
                List("plain", ""),
            }));
            ForcePanelUpdate(_window.rootVisualElement.panel);
            ForcePanelUpdate(_window.rootVisualElement.panel);

            // Act
            var clipped = Named("clip-row-0");

            // Assert
            Assert.That((IsClipWrapped(clipped), clipped.worldBound.size),
                Is.EqualTo((true, Named("plain-row-0").worldBound.size)));
        }

        [Test]
        public void Given_AClippedCardWithTransitionAll_When_LaidOut_Then_ItsWrapperShowsTheBakedMask()
        {
            // Arrange
            Mount("", _ => "w-[80px] h-[80px] bg-red-500 transition-all");

            // Act
            var wrapper = Named("clip-card").parent;

            // Assert
            var baked = wrapper.style.backgroundImage.value.vectorImage;
            Assert.That((baked != null, wrapper.resolvedStyle.backgroundImage.vectorImage == baked),
                Is.EqualTo((true, true)));
        }

        [Test]
        public void Given_AClippedCardWithAnArbitraryDuration_When_LaidOut_Then_ItsWrapperTransitionsForAsLongAsItsTwin()
        {
            // Arrange
            Mount("", _ => "w-[80px] h-[50px] transition-all duration-[400ms]");

            // Act
            var wrapper = Named("clip-card").parent;

            // Assert
            Assert.That(string.Join(",", wrapper.resolvedStyle.transitionDuration),
                Is.EqualTo(string.Join(",", Named("plain-card").resolvedStyle.transitionDuration)));
        }

        private static VNode List(string prefix, string rowClass)
            => V.Div(className: "shrink-0 w-[200px] h-[200px]", children: new VNode[]
            {
                V.VirtualList(
                    items: new[] { 0, 1, 2 },
                    keySelector: i => i.ToString(),
                    itemHeight: 50f,
                    renderer: i => V.Div(name: prefix + "-row-" + i, className: rowClass),
                    className: "grow"),
            });
    }
}
