using System;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies what the weaver's gate does to a <c>[Component]</c> that takes parameters and calls no hook: the
    /// parameters are the whole deps array, compared under the rule <see cref="MemoNode.Dependencies"/> states.
    /// <list type="bullet">
    /// <item>A parent render handing the child a parameter equal under that rule reuses the tree the last miss
    /// built, so the body does not run; a changed parameter runs it and the new value reaches the element.</item>
    /// <item>That holds for a null parameter, a value-type one, a <c>record struct</c> compared by its
    /// content, and a <c>record class</c> compared by its instance — a fresh one of equal content is a
    /// change.</item>
    /// <item>A body with early returns, one whose conditional or coalescing expression is the return value,
    /// one opening with a loop and one opening with a try/catch each reuse their tree on an equal
    /// parameter.</item>
    /// <item>A body making a call the weaver cannot see through, one opting out with
    /// <c>[Component(Compiler = false)]</c>, and one setting <c>Memoize = true</c> are left unwoven, so each
    /// runs whenever a parent render reaches it.</item>
    /// <item>Invoked as a plain method outside any render, a woven body throws.</item>
    /// </list>
    /// </summary>
    /// <remarks>
    /// The host owns a tick. Bumping it re-renders the host, which returns whatever <c>s_child</c> builds for the
    /// new tick, so a case decides exactly which parameter each child render receives. Each child's body bumps
    /// the build counter, and a woven child's entry gate runs ahead of that, so the counter says how often a body
    /// ran.
    /// </remarks>
    [TestFixture]
    internal sealed class HooklessWovenBehaviorE2ETests
    {
        private VisualElement _root = null!;

        private static int s_builds;
        private static Func<int, VNode> s_child = null!;
        private static Action<int> s_setTick = null!;

        [SetUp]
        public void SetUp()
        {
            _root = new VisualElement();
            s_builds = 0;
            s_child = null!;
            s_setTick = null!;
        }

        // Opted out so that the host's own gate plays no part in what a case reads.
        [Component(Compiler = false)]
        private static VNode Host()
        {
            var (tick, setTick) = Hooks.UseState(0);
            s_setTick = setTick;
            return s_child(tick);
        }

        private sealed record LeafProps(string Text);

        private readonly record struct PointProps(int X, int Y);

        private readonly record struct RatioProps(float Value);

        [Component]
        private static VNode Leaf(LeafProps p)
        {
            s_builds++;
            return V.Label(name: "leaf", text: p?.Text ?? "none");
        }

        [Component]
        private static VNode CountLeaf(int count)
        {
            s_builds++;
            return V.Label(name: "leaf", text: count.ToString());
        }

        [Component]
        private static VNode PointLeaf(PointProps p)
        {
            s_builds++;
            return V.Label(name: "leaf", text: $"{p.X},{p.Y}");
        }

        [Component]
        private static VNode EarlyReturnLeaf(LeafProps p)
        {
            s_builds++;
            if (p == null)
            {
                return V.Label(name: "leaf", text: "none");
            }
            if (p.Text.Length == 0)
            {
                return V.Label(name: "leaf", text: "empty");
            }
            return V.Label(name: "leaf", text: p.Text);
        }

        private static VNode CountedLabel(string text)
        {
            s_builds++;
            return V.Label(name: "leaf", text: text);
        }

        // Expression-bodied so the conditional is the return value itself: one arm reaches the return by a
        // branch rather than by falling into it.
        [Component]
        private static VNode ConditionalLeaf(LeafProps p)
            => p.Text.Length > 3 ? CountedLabel("long") : CountedLabel("short");

        private static VNode CountedLabelUnlessEmpty(LeafProps p)
        {
            s_builds++;
            return p.Text.Length == 0 ? null : V.Label(name: "leaf", text: p.Text);
        }

        // The coalescing operator's left operand, when it is not null, is the return value carried straight to
        // the return by a branch.
        [Component]
        private static VNode CoalescingLeaf(LeafProps p)
            => CountedLabelUnlessEmpty(p) ?? V.Label(name: "leaf", text: "empty");

        [Component]
        private static VNode HalvingLeaf(int count)
        {
            do
            {
                count /= 2;
            } while (count > 5);
            s_builds++;
            return V.Label(name: "leaf", text: count.ToString());
        }

        [Component]
        private static VNode GuardedLeaf(LeafProps p)
        {
            try
            {
                s_builds++;
                return V.Label(name: "leaf", text: p.Text.Substring(1));
            }
            catch (ArgumentOutOfRangeException)
            {
                return V.Label(name: "leaf", text: "too short");
            }
        }

        private interface ILeafFormatter
        {
            string Format(string text);
        }

        private sealed class UpperFormatter : ILeafFormatter
        {
            public string Format(string text) => text.ToUpperInvariant();
        }

        private static readonly ILeafFormatter s_formatter = new UpperFormatter();

        [Component]
        private static VNode FormattedLeaf(LeafProps p)
        {
            s_builds++;
            return V.Label(name: "leaf", text: s_formatter.Format(p.Text));
        }

        [Component(Compiler = false)]
        private static VNode OptedOutLeaf(LeafProps p)
        {
            s_builds++;
            return V.Label(name: "leaf", text: p.Text);
        }

        [Component(Memoize = true)]
        private static VNode MemoizedRatioLeaf(RatioProps p)
        {
            s_builds++;
            return V.Label(name: "leaf", text: float.IsNegative(p.Value) ? "negative" : "positive");
        }

        private MountedTree MountHost() => V.Mount(_root, V.Component(Host, key: "host"));

        private static void ReRenderHost(MountedTree mounted)
        {
            s_setTick(1);
            mounted.FlushStateForTest();
        }

        private string LeafText() => _root.Q<Label>(name: "leaf")?.text;

        #region Reuse on an equal parameter

        [Test]
        public void Given_TheSamePropsInstance_When_TheHostReRenders_Then_TheBodyDoesNotRun()
        {
            // Arrange
            var props = new LeafProps("a");
            s_child = _ => V.Component(Leaf, props, key: "leaf");
            using var mounted = MountHost();

            // Act
            ReRenderHost(mounted);

            // Assert
            Assert.That(s_builds, Is.EqualTo(1), "The mount builds once and the equal re-render reuses that tree");
        }

        [Test]
        public void Given_ANullPropTwice_When_TheHostReRenders_Then_TheBodyDoesNotRun()
        {
            // Arrange
            s_child = _ => V.Component(Leaf, (LeafProps)null, key: "leaf");
            using var mounted = MountHost();

            // Act
            ReRenderHost(mounted);

            // Assert
            Assert.That(s_builds, Is.EqualTo(1), "Two nulls are equal, so the second render reuses the first tree");
        }

        [Test]
        public void Given_AnEqualValueTypeProp_When_TheHostReRenders_Then_TheBodyDoesNotRun()
        {
            // Arrange — the int is boxed afresh into each render's deps array
            s_child = _ => V.Component(CountLeaf, 7, key: "leaf");
            using var mounted = MountHost();

            // Act
            ReRenderHost(mounted);

            // Assert
            Assert.That(s_builds, Is.EqualTo(1), "A value-type parameter compares by value, not by its box");
        }

        [Test]
        public void Given_AFreshRecordStructOfEqualContent_When_TheHostReRenders_Then_TheBodyDoesNotRun()
        {
            // Arrange
            s_child = _ => V.Component(PointLeaf, new PointProps(1, 2), key: "leaf");
            using var mounted = MountHost();

            // Act
            ReRenderHost(mounted);

            // Assert
            Assert.That(s_builds, Is.EqualTo(1), "A record struct parameter compares by its content");
        }

        #endregion

        #region A changed parameter runs the body

        // GREEN_ON_BASE(characterization): an unwoven body shows every render's text, so the base does too.
        // What this pins is the gate missing on a changed parameter; `ObjectIs.AreEqualDeps` answering true
        // for every pair reddens it.
        [Test]
        public void Given_AChangedProp_When_TheHostReRenders_Then_TheNewTextIsShown()
        {
            // Arrange
            s_child = tick => V.Component(Leaf, new LeafProps(tick == 0 ? "a" : "b"), key: "leaf");
            using var mounted = MountHost();

            // Act
            ReRenderHost(mounted);

            // Assert
            Assert.That(LeafText(), Is.EqualTo("b"));
        }

        // GREEN_ON_BASE(characterization): an unwoven body runs on every render, so the base runs it here too.
        // What this pins is a null compared against a value; `ObjectIs.AreEqualObjects` answering true when
        // either operand is null reddens it.
        [Test]
        public void Given_ANullPropFollowedByAValue_When_TheHostReRenders_Then_TheBodyRuns()
        {
            // Arrange — the value is one instance, so only the null in front of it makes the second render a miss
            var props = new LeafProps("b");
            s_child = tick => V.Component(Leaf, tick == 0 ? null : props, key: "leaf");
            using var mounted = MountHost();

            // Act
            ReRenderHost(mounted);

            // Assert
            Assert.That(s_builds, Is.EqualTo(2));
        }

        // GREEN_ON_BASE(characterization): an unwoven body runs on every render, so the base runs it here too.
        // What this pins is the gate keying a record class on its instance; `ObjectIs.AreEqualObjects`
        // answering `a.Equals(b)` in its reference branch reddens it.
        [Test]
        public void Given_AFreshRecordClassOfEqualContent_When_TheHostReRenders_Then_TheBodyRuns()
        {
            // Arrange
            s_child = _ => V.Component(Leaf, new LeafProps("a"), key: "leaf");
            using var mounted = MountHost();

            // Act
            ReRenderHost(mounted);

            // Assert
            Assert.That(s_builds, Is.EqualTo(2), "A fresh record class instance is a changed parameter");
        }

        #endregion

        #region Each return path reuses its tree

        [Test]
        public void Given_TheFirstEarlyReturn_When_TheHostReRendersWithTheSameProp_Then_TheBodyDoesNotRun()
        {
            // Arrange
            s_child = _ => V.Component(EarlyReturnLeaf, (LeafProps)null, key: "leaf");
            using var mounted = MountHost();

            // Act
            ReRenderHost(mounted);

            // Assert
            Assert.That(s_builds, Is.EqualTo(1));
        }

        [Test]
        public void Given_TheSecondEarlyReturn_When_TheHostReRendersWithTheSameProp_Then_TheBodyDoesNotRun()
        {
            // Arrange
            var props = new LeafProps(string.Empty);
            s_child = _ => V.Component(EarlyReturnLeaf, props, key: "leaf");
            using var mounted = MountHost();

            // Act
            ReRenderHost(mounted);

            // Assert
            Assert.That(s_builds, Is.EqualTo(1));
        }

        [Test]
        public void Given_TheFinalReturn_When_TheHostReRendersWithTheSameProp_Then_TheBodyDoesNotRun()
        {
            // Arrange
            var props = new LeafProps("text");
            s_child = _ => V.Component(EarlyReturnLeaf, props, key: "leaf");
            using var mounted = MountHost();

            // Act
            ReRenderHost(mounted);

            // Assert
            Assert.That(s_builds, Is.EqualTo(1));
        }

        [Test]
        public void Given_TheConditionalsFirstArm_When_TheHostReRendersWithTheSameProp_Then_TheBodyDoesNotRun()
        {
            // Arrange
            var props = new LeafProps("longer");
            s_child = _ => V.Component(ConditionalLeaf, props, key: "leaf");
            using var mounted = MountHost();

            // Act
            ReRenderHost(mounted);

            // Assert
            Assert.That(s_builds, Is.EqualTo(1));
        }

        [Test]
        public void Given_TheConditionalsSecondArm_When_TheHostReRendersWithTheSameProp_Then_TheBodyDoesNotRun()
        {
            // Arrange
            var props = new LeafProps("ab");
            s_child = _ => V.Component(ConditionalLeaf, props, key: "leaf");
            using var mounted = MountHost();

            // Act
            ReRenderHost(mounted);

            // Assert
            Assert.That(s_builds, Is.EqualTo(1));
        }

        [Test]
        public void Given_TheCoalescingLeftOperand_When_TheHostReRendersWithTheSameProp_Then_TheBodyDoesNotRun()
        {
            // Arrange
            var props = new LeafProps("text");
            s_child = _ => V.Component(CoalescingLeaf, props, key: "leaf");
            using var mounted = MountHost();

            // Act
            ReRenderHost(mounted);

            // Assert
            Assert.That(s_builds, Is.EqualTo(1));
        }

        [Test]
        public void Given_ABodyOpeningWithALoop_When_TheHostReRendersWithTheSameProp_Then_TheBodyDoesNotRun()
        {
            // Arrange — 40 halves three times before the loop exits
            s_child = _ => V.Component(HalvingLeaf, 40, key: "leaf");
            using var mounted = MountHost();

            // Act
            ReRenderHost(mounted);

            // Assert
            Assert.That(s_builds, Is.EqualTo(1));
        }

        [Test]
        public void Given_ABodyOpeningWithATryCatch_When_TheHostReRendersWithTheSameProp_Then_TheBodyDoesNotRun()
        {
            // Arrange
            var props = new LeafProps("abc");
            s_child = _ => V.Component(GuardedLeaf, props, key: "leaf");
            using var mounted = MountHost();

            // Act
            ReRenderHost(mounted);

            // Assert
            Assert.That(s_builds, Is.EqualTo(1));
        }

        #endregion

        #region Left unwoven

        // GREEN_ON_BASE(characterization): the base leaves every body without a hook unwoven, this one included.
        // Two readings bail it: the safety gate, and the hook scan taking the dispatch for a hook call whose
        // value nothing captures. Measured, cutting either leaves it green and cutting both reddens it: the
        // `ReachesAnyNonSafeHook` call disabled, and the open-dispatch arm of `CallsHookTransitively` answering false.
        [Test]
        public void Given_ABodyMakingAnInterfaceDispatch_When_TheHostReRendersWithTheSameProp_Then_TheBodyRuns()
        {
            // Arrange
            var props = new LeafProps("a");
            s_child = _ => V.Component(FormattedLeaf, props, key: "leaf");
            using var mounted = MountHost();

            // Act
            ReRenderHost(mounted);

            // Assert
            Assert.That(s_builds, Is.EqualTo(2),
                "The dispatch's runtime target could call a hook, so the body is unwoven");
        }

        // GREEN_ON_BASE(characterization): the base runs this body on every render as well.
        // It pins the opt-out the upgrade note names; `NamedFlag`'s `named.Name == propertyName` flipped to `!=`
        // reddens it.
        [Test]
        public void Given_ABodyOptedOutOfTheCompiler_When_TheHostReRendersWithTheSameProp_Then_TheBodyRuns()
        {
            // Arrange
            var props = new LeafProps("a");
            s_child = _ => V.Component(OptedOutLeaf, props, key: "leaf");
            using var mounted = MountHost();

            // Act
            ReRenderHost(mounted);

            // Assert
            Assert.That(s_builds, Is.EqualTo(2));
        }

        // GREEN_ON_BASE(characterization): the base leaves this body unwoven, and so does this change.
        // It pins why a Memoize = true body stays unwoven: the props bail calls the zero's sign a change, the
        // dependency comparison does not, and a woven gate would hand back the positive zero's tree. The
        // hookless branch's `return !RequestsPropsBail(method)` turned into `return true` reddens it.
        [Test]
        public void Given_AMemoizedBodyWhoseFloatFieldChangesOnlyItsSign_When_TheHostReRenders_Then_TheNewSignIsShown()
        {
            // Arrange
            s_child = tick => V.Component(MemoizedRatioLeaf, new RatioProps(tick == 0 ? 0f : -0f), key: "leaf");
            using var mounted = MountHost();

            // Act
            ReRenderHost(mounted);

            // Assert
            Assert.That(LeafText(), Is.EqualTo("negative"));
        }

        #endregion

        [Test]
        public void Given_AWovenBody_When_InvokedOutsideAnyRender_Then_ItThrows()
        {
            // Act + Assert
            Assert.Throws<InvalidOperationException>(() => Leaf(new LeafProps("a")));
        }
    }
}
