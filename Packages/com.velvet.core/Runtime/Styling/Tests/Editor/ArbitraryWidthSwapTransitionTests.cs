using NUnit.Framework;
using UnityEditor.UIElements.TestFramework;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.UIElements.TestFramework;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies how a bracketed width swapped for another bracketed width on an element carrying a transition
    /// lands on a simulated panel: it animates from the width the element had, as a CSS width change does,
    /// and a width swapped for nothing, or for another property, still leaves the element.
    /// </summary>
    internal sealed class ArbitraryWidthSwapTransitionTests
    {
        // The widths the swaps move between, and the margin by which a width still on its way differs from the
        // one it is heading for.
        private const float Narrow = 10f;
        private const float Wide = 300f;
        private const float Margin = 0.5f;

        private const string Transition = "transition-all duration-[1000ms] h-[10px] ";

        private readonly record struct ClassNameState(string ClassName);

        private sealed class ClassNameStore : Store<ClassNameState>
        {
            public ClassNameStore(string initial) : base(new ClassNameState(initial)) { }
            public void Set(string className) => SetState(_ => new ClassNameState(className));
            protected override void ResetCore() => SetState(_ => new ClassNameState(string.Empty));
        }

        private static ClassNameStore s_childClass;

        private EditorPanelSimulator _sim;
        private MountedTree _mounted;

        [SetUp]
        public void SetUp()
        {
            PanelSimulator.ResetCurrentTime();
            _sim = new EditorPanelSimulator { panelSize = new Vector2(400, 300) };
            _sim.ResetTimePerSimulatedFrameToDefault();
            s_childClass = null;
        }

        [TearDown]
        public void TearDown()
        {
            _mounted?.Dispose();
            _mounted = null;
            _sim?.Dispose();
            _sim = null;
            s_childClass?.Dispose();
            s_childClass = null;
        }

        // The frame keeps the child off the panel root, whose size the panel decides.
        [Component]
        private static VNode Host()
            => V.Div(name: "frame", className: "w-[600px] h-[600px]", children: new VNode[]
            {
                V.Div(name: "child", className: Hooks.UseStore(s_childClass, s => s.ClassName)),
            });

        private VisualElement Child => _sim.rootVisualElement.Q<VisualElement>("child");

        private void Frames(int count, long ms)
        {
            for (var i = 0; i < count; i++)
            {
                _sim.FrameUpdateMs(ms);
            }
        }

        // Mounts with the bundled sheet attached, since it carries transition-all, and lets the width settle.
        private void MountSettled(string className)
        {
            VelvetStyleUtilities.AttachTo(_sim.rootVisualElement);
            s_childClass = new ClassNameStore(className);
            _mounted = V.Mount(_sim.rootVisualElement, V.Component(Host, key: "host"));
            Frames(3, 16);
        }

        private void Swap(string className)
        {
            s_childClass.Set(className);
            _mounted.FlushStateForTest();
        }

        [TestCase("w-[300px]", "w-[10px]")]
        [TestCase("w-[50vw]", "w-[10px]")]
        [TestCase("w-[300px]", "!w-[10px]")]
        [TestCase("!w-[300px]", "w-[10px]")]
        public void Given_AWidthUnderATransition_When_SwappedForANarrowerOne_Then_ItIsStillMostOfTheWayFromTheOldWidthThreeFramesLater(
            string oldWidth, string newWidth)
        {
            // Arrange
            MountSettled(Transition + oldWidth);

            var before = Child.layout.width;

            // Act
            Swap(Transition + newWidth);
            Frames(3, 16);

            // Assert — NaN unless the old width had settled above the new one, so a row whose old width never
            // resolved cannot pass.
            Assert.That(before > Narrow + Margin ? Child.layout.width : float.NaN, Is.GreaterThan(Narrow + Margin));
        }

        [Test]
        public void Given_AWidthUnderATransition_When_SwappedForAWiderOne_Then_ItIsStillNearTheOldWidthThreeFramesLater()
        {
            // Arrange
            MountSettled(Transition + "w-[10px]");

            var before = Child.layout.width;

            // Act
            Swap(Transition + "w-[300px]");
            Frames(3, 16);

            // Assert — NaN unless the old width had settled below the new one.
            Assert.That(before < Wide - Margin ? Child.layout.width : float.NaN, Is.LessThan(Wide - Margin));
        }

        [Test]
        public void Given_AWidthAmongTenClassesUnderATransition_When_SwappedForANarrowerOne_Then_ItIsStillMostOfTheWayFromTheOldWidthThreeFramesLater()
        {
            // Arrange — ten classes either side, past the count DiffClassList compares by a linear scan.
            const string pad = "p-[0px] m-[0px] pt-[0px] pb-[0px] pl-[0px] pr-[0px] ";
            MountSettled(Transition + pad + "w-[300px]");

            var before = Child.layout.width;

            // Act
            Swap(Transition + pad + "w-[10px]");
            Frames(3, 16);

            // Assert — NaN unless the old width had settled above the new one.
            Assert.That(before > Narrow + Margin ? Child.layout.width : float.NaN, Is.GreaterThan(Narrow + Margin));
        }

        // GREEN_ON_BASE(characterization): the base landed the new width at once, which is also where a finished transition ends.
        [TestCase("w-[300px]", "w-[10px]")]
        [TestCase("!w-[300px]", "w-[10px]")]
        [TestCase("w-[300px]", "!w-[10px]")]
        public void Given_AWidthUnderATransition_When_SwappedAndTheTransitionFinishes_Then_ItIsTheNewWidth(
            string oldWidth, string newWidth)
        {
            // Arrange
            MountSettled(Transition + oldWidth);

            // Act
            Swap(Transition + newWidth);
            Frames(12, 100);

            // Assert
            Assert.That(Child.layout.width, Is.EqualTo(10f).Within(0.01f));
        }

        // GREEN_ON_BASE(characterization): the base cleared the inline width of any outgoing width class.
        [TestCase("")]
        [TestCase("max-w-[20px]")]
        [TestCase("p-[2px]")]
        public void Given_AnInlineWidth_When_SwappedForNoWidth_Then_NoInlineWidthRemains(string incoming)
        {
            // Arrange
            MountSettled(Transition + "w-[300px]");

            // Act
            Swap(Transition + incoming);

            // Assert
            Assert.That(Child.style.width.keyword, Is.EqualTo(StyleKeyword.Null));
        }

        // GREEN_ON_BASE(characterization): the base cleared the inline width of any outgoing width class.
        [Test]
        public void Given_AnInlineWidthAmongTenClasses_When_SwappedForNoWidth_Then_NoInlineWidthRemains()
        {
            // Arrange — ten classes either side, past the count DiffClassList compares by a linear scan.
            const string pad = "p-[0px] m-[0px] pt-[0px] pb-[0px] pl-[0px] pr-[0px] ";
            MountSettled(Transition + pad + "w-[300px]");

            // Act
            Swap(Transition + pad);

            // Assert
            Assert.That(Child.style.width.keyword, Is.EqualTo(StyleKeyword.Null));
        }
    }
}
