using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies that a component rendering on its own state, under a Provider placed inside an
    /// <see cref="V.AnimatePresence"/> its parent renders, reads that Provider's value rather than the context
    /// default.
    /// </summary>
    [TestFixture]
    internal sealed class ContextSpineThroughPresenceTests
    {
        private static readonly ComponentContext<string> ThemeContext = ComponentContext<string>.Create("default");

        private static string s_observed;
        private static StateUpdater<int> s_setTick;

        [Component(Compiler = false)]
        private static VNode HostRender()
            => V.AnimatePresence(children: new VNode[]
            {
                V.Provider(ThemeContext, "provided", new VNode[]
                {
                    V.Component(ReaderRender, key: "reader"),
                }),
            });

        [Component(Compiler = false)]
        private static VNode ReaderRender()
        {
            var (tick, setTick) = Hooks.UseState(0);
            s_setTick = setTick;
            s_observed = Hooks.UseContext(ThemeContext);
            return V.Label(text: s_observed + tick);
        }

        [Test]
        public void Given_AReaderUnderAProviderInsideAnAnimatePresence_When_ItRendersOnItsOwnState_Then_ItReadsTheProvidersValue()
        {
            // Arrange
            using var mounted = V.Mount(new VisualElement(), V.Component(HostRender, key: "host"));
            mounted.DrainForTest();
            s_observed = null;

            // Act
            s_setTick.Invoke(1);
            mounted.DrainForTest();

            // Assert
            Assert.That(s_observed, Is.EqualTo("provided"));
        }
    }
}
