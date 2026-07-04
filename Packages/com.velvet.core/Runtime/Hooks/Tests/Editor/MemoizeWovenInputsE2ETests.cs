using System;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies how the default-on inner auto-memoization weaver keys its dependency array on a component's
    /// captured inputs — props and the value read by <c>UseContext</c> — compared with Velvet's <c>ObjectIs</c>
    /// equality.
    /// <list type="bullet">
    /// <item>The mount always misses (cold cache) and builds the body once.</item>
    /// <item>A captured prop is held by reference identity: re-rendering with the same record instance is a
    /// cache hit (no rebuild); a changed prop value or a fresh-but-equal record instance is a miss (rebuild),
    /// because <c>ObjectIs</c> compares reference-type inputs by reference identity, not structural equality.</item>
    /// <item>A captured context value is keyed the same way: re-rendering under an unchanged provided value is a
    /// hit; a changed provided value is a miss.</item>
    /// <item>On every hit and miss the rendered output reflects the captured input.</item>
    /// </list>
    /// </summary>
    /// <remarks>
    /// The components are woven at build time (the IL-shape is asserted in <c>CompilerILPostProcessorE2ETests</c>).
    /// A re-render of the parent / Provider host reconciles the child fiber, which reuses its committed VNode while
    /// every captured input stays <c>ObjectIs</c>-equal. The rebuild counters sit in the build region, so they
    /// advance only on a miss.
    /// </remarks>
    [TestFixture]
    internal sealed class MemoizeWovenInputsE2ETests
    {
        private VisualElement _root = null!;

        [SetUp]
        public void SetUp()
        {
            _root = new VisualElement();
            s_childRebuildCount = 0;
            s_parentSetTick = null;
            s_childProps = null;
            s_providerBumpTick = null;
            s_themeForTick = null;
            s_contextRebuildCount = 0;
        }

        private static int s_childRebuildCount;
        private static Action<int> s_parentSetTick;
        private static Func<int, ChildProps> s_childProps;

        private sealed record ChildProps(string Label);

        // Props-driven woven child. UseState supplies the hook boundary the weaver keys on; the prop record is
        // prepended to the deps array. The rebuild counter sits in the build region, so it advances only on a miss.
        [Component]
        private static VNode WovenPropsChild(ChildProps p)
        {
            var (suffix, _) = Hooks.UseState("");
            s_childRebuildCount++;
            return V.Label(name: "child", text: p.Label + suffix);
        }

        [Component]
        private static VNode PropsParent()
        {
            var (tick, setTick) = Hooks.UseState(0);
            s_parentSetTick = setTick;
            return V.Component(WovenPropsChild, s_childProps(tick), key: "child");
        }

        [Test]
        public void Given_SamePropInstance_When_ParentReRenders_Then_ChildDoesNotRebuild()
        {
            // Arrange — the parent hands the child the SAME record instance on every render.
            var stable = new ChildProps("a");
            s_childProps = _ => stable;
            using var mounted = V.Mount(_root, V.Component(PropsParent, key: "parent"));
            Assume.That(s_childRebuildCount, Is.EqualTo(1), "Precondition: mount misses once and builds the child");

            // Act
            s_parentSetTick(1);
            mounted.FlushStateForTest();

            // Assert — same instance + unchanged hook value is ObjectIs-equal -> cache hit.
            Assert.That(s_childRebuildCount, Is.EqualTo(1),
                "A reference-identical prop and unchanged hook value is a cache hit, so the body does not rebuild");
        }

        [Test]
        public void Given_ChangedPropValue_When_ParentReRenders_Then_ChildRebuilds()
        {
            // Arrange — the child's prop value tracks the parent tick, so each re-render hands a different Label.
            s_childProps = tick => new ChildProps($"a{tick}");
            using var mounted = V.Mount(_root, V.Component(PropsParent, key: "parent"));
            Assume.That(s_childRebuildCount, Is.EqualTo(1), "Precondition: mount misses once and builds the child");

            // Act
            s_parentSetTick(1);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(s_childRebuildCount, Is.EqualTo(2),
                "A changed prop value is not ObjectIs-equal -> cache miss, so the body rebuilds");
        }

        [Test]
        public void Given_ChangedPropValue_When_ParentReRenders_Then_ChildDisplaysNewLabel()
        {
            // Arrange
            s_childProps = tick => new ChildProps($"a{tick}");
            using var mounted = V.Mount(_root, V.Component(PropsParent, key: "parent"));
            Assume.That(_root.Q<Label>(name: "child")?.text, Is.EqualTo("a0"), "Precondition: initial label is a0");

            // Act
            s_parentSetTick(1);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(_root.Q<Label>(name: "child")?.text, Is.EqualTo("a1"),
                "The rebuilt child renders the new prop value");
        }

        [Test]
        public void Given_FreshPropInstanceWithEqualValues_When_ParentReRenders_Then_ChildRebuilds()
        {
            // Arrange — a fresh record instance with identical members on every render. The inner memo keys on
            // reference identity, so a fresh-but-equal instance is a miss (sound: the framework reconciles the
            // child on each parent re-render and only reuses a VNode when the captured reference is identical).
            s_childProps = _ => new ChildProps("a");
            using var mounted = V.Mount(_root, V.Component(PropsParent, key: "parent"));
            Assume.That(s_childRebuildCount, Is.EqualTo(1), "Precondition: mount misses once and builds the child");

            // Act
            s_parentSetTick(1);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(s_childRebuildCount, Is.EqualTo(2),
                "A distinct record instance is not reference-equal -> ObjectIs miss -> rebuild");
        }

        private static readonly ComponentContext<string> ThemeContext =
            ComponentContext<string>.Create("default");

        private static Action<int> s_providerBumpTick;
        private static Func<int, string> s_themeForTick;
        private static int s_contextRebuildCount;

        // Context-driven woven consumer. UseContext is the hook boundary AND the captured input: the live value
        // is keyed into the deps array. The rebuild counter advances only on a miss.
        [Component]
        private static VNode WovenContextConsumer()
        {
            var theme = Hooks.UseContext(ThemeContext);
            s_contextRebuildCount++;
            return V.Label(name: "ctx", text: theme);
        }

        // The host owns an unrelated tick. Bumping it forces a host re-render (reconciling the Provider and the
        // consumer) while s_themeForTick controls whether the provided value actually changes.
        [Component]
        private static VNode ThemeProviderHost()
        {
            var (tick, bump) = Hooks.UseState(0);
            s_providerBumpTick = bump;
            return V.Provider(ThemeContext, s_themeForTick(tick), new VNode[]
            {
                V.Component(WovenContextConsumer, key: "consumer"),
            });
        }

        [Test]
        public void Given_UnchangedContextValue_When_HostReRenders_Then_ConsumerDoesNotRebuild()
        {
            // Arrange — the provided value is the same string regardless of the host tick.
            s_themeForTick = _ => "light";
            using var mounted = V.Mount(_root, V.Component(ThemeProviderHost, key: "host"));
            Assume.That(s_contextRebuildCount, Is.EqualTo(1), "Precondition: mount misses once and builds the consumer");

            // Act — the host re-renders on the bump, but the captured context dep is unchanged.
            s_providerBumpTick(1);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(s_contextRebuildCount, Is.EqualTo(1),
                "An unchanged context value is ObjectIs-equal -> cache hit, so the consumer does not rebuild");
        }

        [Test]
        public void Given_ChangedContextValue_When_HostReRenders_Then_ConsumerRebuilds()
        {
            // Arrange — the provided value tracks the host tick, so the bump hands a different context value.
            s_themeForTick = tick => tick == 0 ? "light" : "dark";
            using var mounted = V.Mount(_root, V.Component(ThemeProviderHost, key: "host"));
            Assume.That(s_contextRebuildCount, Is.EqualTo(1), "Precondition: mount misses once and builds the consumer");

            // Act
            s_providerBumpTick(1);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(s_contextRebuildCount, Is.EqualTo(2),
                "A changed context value is not ObjectIs-equal -> cache miss, so the consumer rebuilds");
        }

        [Test]
        public void Given_ChangedContextValue_When_HostReRenders_Then_ConsumerDisplaysNewValue()
        {
            // Arrange
            s_themeForTick = tick => tick == 0 ? "light" : "dark";
            using var mounted = V.Mount(_root, V.Component(ThemeProviderHost, key: "host"));
            Assume.That(_root.Q<Label>(name: "ctx")?.text, Is.EqualTo("light"), "Precondition: initial value is light");

            // Act
            s_providerBumpTick(1);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(_root.Q<Label>(name: "ctx")?.text, Is.EqualTo("dark"),
                "The rebuilt consumer renders the new context value");
        }
    }
}
