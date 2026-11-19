using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies that a plain component the compiler's cache hands back as the node it last rendered from is not
    /// rendered again by its parent's render, as React bails an element it is handed again, and that its own state
    /// and a context it reads still render it.
    /// </summary>
    /// <remarks>
    /// A parent either reads a state in one scope and builds the child in another that reads nothing, so its render
    /// builds the first again and returns the child's node from the scope's cache, or is rendered by its own parent
    /// with its keys unchanged, so its body gate returns the whole tree. The children opt out of the compiler so a
    /// count says how often their bodies ran.
    /// </remarks>
    [TestFixture]
    internal sealed class CachedChildNodeBailTests
    {
        private static readonly ComponentContext<int> s_tickContext = ComponentContext<int>.Create(-1);

        private static StateUpdater<int> s_setTick;
        private static StateUpdater<int> s_setOwn;
        private static int s_childRenders;

        private VisualElement _root = null!;

        [SetUp]
        public void SetUp()
        {
            _root = new VisualElement();
            s_setTick = default;
            s_setOwn = default;
            s_childRenders = 0;
        }

        [Component(Compiler = false)]
        private static VNode Plain()
        {
            s_childRenders++;
            return V.Label(name: "plain");
        }

        [Component(Compiler = false)]
        private static VNode Consumer()
        {
            var tick = Hooks.UseContext(s_tickContext);
            return V.Label(name: "consumer", text: tick.ToString());
        }

        [Component(Compiler = false)]
        private static VNode Stateful()
        {
            var (own, setOwn) = Hooks.UseState(0);
            s_setOwn = setOwn;
            return V.Label(name: "stateful", text: own.ToString());
        }

        [Component]
        private static VNode PlainParent()
        {
            var (tick, setTick) = Hooks.UseState(0);
            s_setTick = setTick;
            return V.Div("parent", V.Label(text: tick.ToString()), V.Component(Plain));
        }

        [Component]
        private static VNode GatedParent()
        {
            var (shown, _) = Hooks.UseState(7);
            return V.Div("parent", V.Label(text: shown.ToString()), V.Component(Plain));
        }

        [Component(Compiler = false)]
        private static VNode GrandParent()
        {
            var (_, setTick) = Hooks.UseState(0);
            s_setTick = setTick;
            return V.Component(GatedParent);
        }

        [Component]
        private static VNode ProviderParent()
        {
            var (tick, setTick) = Hooks.UseState(0);
            s_setTick = setTick;
            return V.Provider(s_tickContext, tick, new VNode[] { V.Component(Consumer) });
        }

        [Component]
        private static VNode StatefulParent()
        {
            var (tick, setTick) = Hooks.UseState(0);
            s_setTick = setTick;
            return V.Div("parent", V.Label(text: tick.ToString()), V.Component(Stateful));
        }

        [Test]
        public void Given_APlainChildBuiltInACachedScope_When_TheParentRendersAgain_Then_TheChildDoesNotRenderAgain()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(PlainParent));

            // Act
            s_setTick.Invoke(t => t + 1);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(s_childRenders, Is.EqualTo(1));
        }

        [Test]
        public void Given_APlainChildInATreeTheParentsBodyGateReturns_When_TheGrandparentRendersAgain_Then_TheChildDoesNotRenderAgain()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(GrandParent));

            // Act
            s_setTick.Invoke(t => t + 1);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(s_childRenders, Is.EqualTo(1));
        }

        // GREEN_ON_BASE(characterization): the base renders every plain child again with its parent, which renders a consumer too.
        [Test]
        public void Given_AContextConsumerBuiltInACachedScope_When_TheProvidedValueChanges_Then_TheConsumerShowsTheNewValue()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(ProviderParent));

            // Act
            s_setTick.Invoke(t => t + 1);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(_root.Q<Label>("consumer").text, Is.EqualTo("1"));
        }

        // GREEN_ON_BASE(characterization): the base renders every plain child again with its parent, which renders a dirty one too.
        [Test]
        public void Given_AStatefulChildBuiltInACachedScope_When_ItAndTheParentUpdateTogether_Then_TheChildShowsItsNewState()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(StatefulParent));

            // Act
            s_setTick.Invoke(t => t + 1);
            s_setOwn.Invoke(o => o + 5);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(_root.Q<Label>("stateful").text, Is.EqualTo("5"));
        }
    }
}
