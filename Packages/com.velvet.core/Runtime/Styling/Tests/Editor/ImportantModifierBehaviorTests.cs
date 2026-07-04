using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Behavioural coverage for the important modifier (<c>!utility</c> / <c>utility!</c>).
    /// An important utility is inline-resolved on the highest (Important) layer, the inline-style stand-in
    /// for CSS <c>!important</c> (UI Toolkit inline styles already beat USS class rules), so it wins over
    /// a later base utility and over a state layer such as hover. A class-only utility has no inline form,
    /// so its bang is accepted (stripped) but inert. GWT, one assert each.
    /// </summary>
    [TestFixture]
    internal sealed class ImportantModifierBehaviorTests
    {
        private VisualElement _root;
        private MountedTree _mounted;

        [SetUp]
        public void SetUp() => _root = new VisualElement();

        [TearDown]
        public void TearDown()
        {
            _mounted?.Dispose();
            _mounted = null;
        }

        private VisualElement MountLeaf(string className)
        {
            _mounted = V.Mount(_root, V.Div(name: "leaf", className: className));
            return _root.Q<VisualElement>("leaf");
        }

        [Test]
        public void Given_ImportantWidthBeforeBaseWidth_When_Mounted_Then_ImportantWins()
        {
            // Arrange/Act — !w-[50px] precedes w-[100px]; without elevation the later base would win (100px),
            // but the important utility sits on the top layer and wins regardless of order.
            var leaf = MountLeaf("!w-[50px] w-[100px]");

            // Assert
            Assert.That(leaf.style.width.value.value, Is.EqualTo(50f));
        }

        [Test]
        public void Given_TrailingBangV4Syntax_When_Mounted_Then_ImportantWins()
        {
            // Arrange/Act — the v4 trailing-bang form (w-[50px]!) is equally important; it beats the base
            // w-[100px] that comes before it.
            var leaf = MountLeaf("w-[100px] w-[50px]!");

            // Assert
            Assert.That(leaf.style.width.value.value, Is.EqualTo(50f));
        }

        [Test]
        public void Given_ImportantClassOnlyUtility_When_Mounted_Then_BangStrippedAndClassApplied()
        {
            // Arrange/Act — !flex has no inline form to elevate, so the bang is simply stripped and the
            // utility applies as the plain `flex` class (not a dead "!flex" literal).
            var leaf = MountLeaf("!flex");

            // Assert
            Assert.IsTrue(leaf.ClassListContains("flex"));
        }

        [Test]
        public void Given_ImportantWidthWithHoverWidth_When_Hovered_Then_ImportantBeatsHover()
        {
            // Arrange — !w-[50px] (Important layer) alongside hover:w-[200px] (Hover layer).
            var leaf = MountLeaf("hover:w-[200px] !w-[50px]");
            Assume.That(leaf.style.width.value.value, Is.EqualTo(50f), "Precondition: important applies at rest");

            // Act — hover turns on, which would otherwise raise width to 200px.
            using (var evt = PointerOverEvent.GetPooled()) leaf.SimulateEvent(evt);

            // Assert — important outranks the hover layer, so width stays 50px.
            Assert.That(leaf.style.width.value.value, Is.EqualTo(50f));
        }
    }
}
