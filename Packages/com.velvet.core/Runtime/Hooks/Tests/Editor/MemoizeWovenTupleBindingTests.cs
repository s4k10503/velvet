using System;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies that the default-on auto-memoization weaver handles the idiomatic TWO-ELEMENT UseState
    /// binding <c>var (value, setValue) = Hooks.UseState(...)</c> the same way it handles the value-only discard
    /// form <c>var (value, _) = ...</c>: it captures Item1 (the value) as a dependency and treats Item2 (the
    /// reference-stable setter) as a non-dependency, so the component is woven and memoizes its VNode build.
    /// </summary>
    /// <remarks>
    /// Mirrors <see cref="MemoizeWovenInputsE2ETests"/> (which deliberately uses the discard form, the only shape
    /// the weaver originally accepted). Roslyn emits a <c>dup</c> right after the hook call for the two-element
    /// deconstruction; before the fix that <c>dup</c> matched none of the weaver's post-hook branches and bailed
    /// the whole method, leaving every such component (e.g. one with many UseState sites) un-memoized. The rebuild
    /// counter sits in the build region, so it advances only on a memo MISS (an un-woven component rebuilds on
    /// every parent re-render).
    /// </remarks>
    [TestFixture]
    internal sealed class MemoizeWovenTupleBindingTests
    {
        private VisualElement _root = null!;

        [SetUp]
        public void SetUp()
        {
            _root = new VisualElement();
            s_childRebuildCount = 0;
            s_parentSetTick = null;
            s_childSetSuffix = null;
            s_stableProps = null;
        }

        private static int s_childRebuildCount;
        private static Action<int> s_parentSetTick;
        private static Action<string> s_childSetSuffix;
        private static ChildProps s_stableProps;

        private sealed record ChildProps(string Label);

        // TWO-ELEMENT binding (the idiomatic form). The setter is captured into a static so it is genuinely
        // USED — that makes Roslyn emit the full `call -> dup -> ldfld Item1 -> stloc -> ldfld Item2 -> stXxx`
        // deconstruction (not the elided unused-setter form). Item1 (suffix) is the sound dep; the setter is
        // reference-stable. The rebuild counter sits in the build region, advancing only on a memo miss.
        [Component]
        private static VNode WovenTupleChild(ChildProps p)
        {
            var (suffix, setSuffix) = Hooks.UseState("");
            s_childSetSuffix = setSuffix;
            s_childRebuildCount++;
            return V.Label(name: "child", text: p.Label + suffix);
        }

        [Component]
        private static VNode TupleParent()
        {
            var (tick, setTick) = Hooks.UseState(0);
            s_parentSetTick = setTick;
            return V.Component(WovenTupleChild, s_stableProps, key: "child");
        }

        [Test]
        public void Given_ATwoElementUseStateBinding_When_ParentReRendersWithSameProps_Then_TheChildIsMemoizedAndDoesNotRebuild()
        {
            // Arrange — the parent hands the child the SAME record instance on every render, so the only thing that
            // decides hit vs miss is whether the two-element-binding child is actually woven.
            s_stableProps = new ChildProps("a");
            using var mounted = V.Mount(_root, V.Component(TupleParent, key: "parent"));
            Assume.That(s_childRebuildCount, Is.EqualTo(1), "Precondition: mount misses once and builds the child");

            // Act — re-render the parent without changing the child's prop or hook value.
            s_parentSetTick(1);
            mounted.FlushStateForTest();

            // Assert — a two-element `var (x, setX) = UseState(...)` component must be woven (auto-memoized) just like
            // the discard form, so the unchanged-input re-render is a cache hit and the body does not rebuild.
            Assert.That(s_childRebuildCount, Is.EqualTo(1),
                "Two-element UseState binding must be auto-memoized: unchanged inputs -> cache hit -> no rebuild");
        }
    }
}
