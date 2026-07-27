using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Behavioural regression coverage for the layout manipulators (gap / grid) when the class that gates
    /// them arrives through a <b>variant</b> rather than as a literal token. A responsive payload is added
    /// straight to the element's live class list by the conditional manipulator when the breakpoint
    /// activates — it never reaches the reconciled class array the manipulators are otherwise configured
    /// from — so <c>gap-4 md:grid md:grid-cols-3</c> must still hand ownership of the child margins from
    /// the gap manipulator to the grid manipulator above <c>md</c>, and hand it back below.
    /// </summary>
    /// <remarks>
    /// The literal form of each case is already covered (see <c>GapParityTests</c> / <c>GridParityTests</c>)
    /// and cannot catch a regression here: only a real breakpoint crossing exercises the path that bypasses
    /// the reconciler. These therefore run inside a real <see cref="UnityEditor.EditorWindow"/> panel (via
    /// <see cref="PanelTestBase"/>) sized per test, force the layout pass so <c>resolvedStyle.width</c>
    /// resolves, then deliver a <see cref="GeometryChangedEvent"/> so the conditional manipulator
    /// re-evaluates. GWT, one assert per case.
    /// </remarks>
    [TestFixture]
    internal sealed class VariantGatedLayoutManipulatorPanelTests : PanelTestBase
    {
        private const float MdBreakpoint = 768f;
        private const float WidePanel = 1000f;   // >= md
        private const float NarrowPanel = 500f;  // < md

        // --space-4 == 16px (see _tokens.uss).
        private const float Space4 = 16f;

        // A gapped flex row whose grid is gated behind md:, so ONLY a breakpoint crossing can turn the
        // grid on — the class array the reconciler sees never carries a bare `grid`. The explicit
        // flex-row fixes the gap's axis so the margin edge it writes is deterministic.
        private const string ResponsiveGridClass = "flex flex-row gap-4 md:grid md:grid-cols-3";

        private ReconcilerContext Context => _mounted.Root.Reconciler.Context;

        // Mounts a container at the given panel width and resolves the breakpoint against it.
        private VisualElement MountAndResolveAt(float width, string className, int childCount = 3)
        {
            _window.position = new Rect(0, 0, width, 600);
            var children = new VNode[childCount];
            for (var i = 0; i < childCount; i++)
            {
                children[i] = V.Div(className: "child");
            }
            _mounted = V.Mount(_window.rootVisualElement,
                V.Div(name: "container", className: className, children: children));
            var container = _window.rootVisualElement.Q<VisualElement>("container");
            ResolveAt(width, container);
            return container;
        }

        // Sets the panel width, forces the layout pass, then fires a GeometryChangedEvent on the panel root
        // so the responsive manipulator re-reads the width source.
        private void ResolveAt(float width, VisualElement container)
        {
            _window.position = new Rect(0, 0, width, 600);
            ForcePanelUpdate(container.panel);
            using var evt = EventBase<GeometryChangedEvent>.GetPooled();
            container.panel.visualTree.SimulateEvent(evt);
        }

        [Test]
        public void Given_AGappedContainerWithAResponsiveGrid_When_TheRootIsWiderThanMd_Then_TheGapManipulatorIsSuppressed()
        {
            // Arrange / Act — the md: payload turns the container into a grid at 1000px.
            var container = MountAndResolveAt(WidePanel, ResponsiveGridClass);
            Assume.That(container.panel.visualTree.resolvedStyle.width, Is.GreaterThanOrEqualTo(MdBreakpoint),
                "Precondition: the panel root resolved at least the md breakpoint wide");
            Assume.That(container.ClassListContains("grid"), Is.True,
                "Precondition: the responsive payload put the grid class on the live class list");

            // Assert — the grid owns the child margins, so the gap manipulator must be gone.
            Assert.That(Context.GapManipulators.ContainsKey(container), Is.False);
        }

        [Test]
        public void Given_AGappedContainerWithAResponsiveGrid_When_TheRootIsWiderThanMd_Then_AGridManipulatorIsCreated()
        {
            // Arrange / Act
            var container = MountAndResolveAt(WidePanel, ResponsiveGridClass);
            Assume.That(container.ClassListContains("grid-cols-3"), Is.True,
                "Precondition: the responsive payload put the column spec on the live class list");

            // Assert — the column sizing is now owned by a grid manipulator.
            Assert.That(Context.GridManipulators.ContainsKey(container), Is.True);
        }

        [Test]
        public void Given_AGappedContainerWithAResponsiveGrid_When_TheRootIsNarrowerThanMd_Then_TheGapManipulatorStillOwnsTheMargins()
        {
            // Arrange / Act — below md the grid payload never applies, so the literal gap-4 stands.
            var container = MountAndResolveAt(NarrowPanel, ResponsiveGridClass);
            Assume.That(container.panel.visualTree.resolvedStyle.width, Is.LessThan(MdBreakpoint),
                "Precondition: the panel root resolved below the md breakpoint");

            // Assert
            Assert.That(Context.GapManipulators.ContainsKey(container), Is.True);
        }

        [Test]
        public void Given_AResponsiveGridActiveWide_When_ThePanelShrinksBelowMd_Then_TheGapManipulatorIsRestored()
        {
            // Arrange — wide, so the grid owns the margins and the gap manipulator is suppressed.
            var container = MountAndResolveAt(WidePanel, ResponsiveGridClass);
            Assume.That(container.ClassListContains("grid"), Is.True,
                "Precondition: the grid payload is on while wide");

            // Act — the panel shrinks below the md breakpoint and re-resolves.
            ResolveAt(NarrowPanel, container);

            // Assert — ownership hands back to the gap manipulator.
            Assert.That(Context.GapManipulators.ContainsKey(container), Is.True);
        }

        [Test]
        public void Given_AResponsiveGridActiveWide_When_ThePanelShrinksBelowMd_Then_TheGridManipulatorIsRemoved()
        {
            // Arrange
            var container = MountAndResolveAt(WidePanel, ResponsiveGridClass);
            Assume.That(container.ClassListContains("grid"), Is.True,
                "Precondition: the grid payload is on while wide");

            // Act
            ResolveAt(NarrowPanel, container);

            // Assert — no grid class left, so no grid manipulator may survive to size the children.
            Assert.That(Context.GridManipulators.ContainsKey(container), Is.False);
        }

        [Test]
        public void Given_AGappedContainerWithAResponsiveGrid_When_TheRootIsWiderThanMd_Then_TheChildrenAreLaidOutInRows()
        {
            // Arrange / Act — four children over three columns, so the last one starts the second row: the
            // grid gives it margin-left 0, while a gap row would give every child after the first
            // margin-left 16. The margins are what the user actually sees, so this pins the layout rather
            // than the manipulator bookkeeping.
            var container = MountAndResolveAt(WidePanel, ResponsiveGridClass, childCount: 4);
            Assume.That(container.panel.visualTree.resolvedStyle.width, Is.GreaterThanOrEqualTo(MdBreakpoint),
                "Precondition: the panel root resolved at least the md breakpoint wide");

            // Assert — the fourth child starts a row, so it carries no column gap.
            Assert.That(container[3].style.marginLeft.value.value, Is.EqualTo(0f));
        }

        [Test]
        public void Given_AResponsiveGridActiveWide_When_ThePanelShrinksBelowMd_Then_TheGapMarginsSurviveTheGridTeardown()
        {
            // Arrange — same four children, laid out as a grid while wide. The precondition is the wide
            // layout the test above owns; this one is about what happens to the margins on the way back.
            var container = MountAndResolveAt(WidePanel, ResponsiveGridClass, childCount: 4);
            Assume.That(container[3].style.marginLeft.value.value, Is.EqualTo(0f),
                "Precondition: the grid put the fourth child on a new row while wide");

            // Act — the departing grid manipulator clears every margin it wrote as it detaches, in the same
            // pass that creates the gap manipulator.
            ResolveAt(NarrowPanel, container);

            // Assert — the arriving gap manipulator's margins are not taken with it.
            Assert.That(container[3].style.marginLeft.value.value, Is.EqualTo(Space4));
        }

        [Test]
        public void Given_AContainerWhoseOnlyGapIsResponsive_When_TheRootIsWiderThanMd_Then_TheGapManipulatorIsCreated()
        {
            // Arrange / Act — the gap itself is the variant-gated class here (the create direction, where
            // the literal case has nothing at all for the reconciler to configure from).
            var container = MountAndResolveAt(WidePanel, "flex md:gap-4");
            Assume.That(container.ClassListContains("gap-4"), Is.True,
                "Precondition: the responsive payload put the gap class on the live class list");

            // Assert
            Assert.That(Context.GapManipulators.ContainsKey(container), Is.True);
        }
    }

    /// <summary>
    /// The same contract for the two remaining class-gated layout families — <c>divide-*</c> and
    /// <c>text-balance</c>, each of which has its own configure and teardown path — driven by a
    /// NON-responsive variant. <c>dark:</c> needs no panel: the conditional manipulator subscribes to
    /// <see cref="VelvetTheme"/>'s theme signal when it attaches to the element and evaluates it off-panel,
    /// so a bare reconciler is enough to cross the same side channel a breakpoint crossing uses.
    /// GWT, one assert per case.
    /// </summary>
    [TestFixture]
    internal sealed class VariantGatedLayoutManipulatorThemeTests
    {
        [TearDown]
        public void TearDown() => VelvetTheme.IsDark = false;

        private static VNode Container(string className) =>
            V.Div(className: className, children: new VNode[]
            {
                V.Div(className: "child"),
                V.Div(className: "child"),
            });

        [Test]
        public void Given_ADarkGatedDivide_When_TheThemeTurnsDark_Then_TheDivideManipulatorIsCreated()
        {
            // Arrange — no divide class in the reconciled array at all; only the dark: payload carries one.
            using var scope = new ReconcilerScope();
            var tree = new VNode[] { Container("flex flex-col dark:divide-y") };
            scope.Reconciler.Reconcile(scope.Root, System.Array.Empty<VNode>(), tree);
            Assume.That(scope.Reconciler.Context.DivideManipulators.Count, Is.EqualTo(0),
                "Precondition: nothing carries a divide while the theme is light");

            // Act
            VelvetTheme.IsDark = true;

            // Assert
            Assert.That(scope.Reconciler.Context.DivideManipulators.Count, Is.EqualTo(1));
        }

        [Test]
        public void Given_ADarkGatedTextBalance_When_TheThemeTurnsDark_Then_TheTextBalanceManipulatorIsCreated()
        {
            // Arrange
            using var scope = new ReconcilerScope();
            var tree = new VNode[] { V.Label(className: "dark:text-balance", text: "hello") };
            scope.Reconciler.Reconcile(scope.Root, System.Array.Empty<VNode>(), tree);
            Assume.That(scope.Reconciler.Context.TextBalanceManipulators.Count, Is.EqualTo(0),
                "Precondition: nothing balances text while the theme is light");

            // Act
            VelvetTheme.IsDark = true;

            // Assert
            Assert.That(scope.Reconciler.Context.TextBalanceManipulators.Count, Is.EqualTo(1));
        }

        [Test]
        public void Given_ADarkGatedTextBalanceApplied_When_TheThemeTurnsLight_Then_TheTextBalanceManipulatorIsRemoved()
        {
            // Arrange — text-balance owns a shared inline max-width slot, so its teardown is bespoke rather
            // than the shared configure step; the off-edge has to reach it just as the on-edge did.
            using var scope = new ReconcilerScope();
            var tree = new VNode[] { V.Label(className: "dark:text-balance", text: "hello") };
            scope.Reconciler.Reconcile(scope.Root, System.Array.Empty<VNode>(), tree);
            VelvetTheme.IsDark = true;
            Assume.That(scope.Reconciler.Context.TextBalanceManipulators.Count, Is.EqualTo(1),
                "Precondition: the dark payload attached the manipulator");

            // Act
            VelvetTheme.IsDark = false;

            // Assert
            Assert.That(scope.Reconciler.Context.TextBalanceManipulators.Count, Is.EqualTo(0));
        }

        [Test]
        public void Given_ADarkGatedTextBalanceBesideAMaxWidth_When_TheThemeTurnsLight_Then_TheUtilitysMaxWidthIsRestored()
        {
            // Arrange — the variant analogue of the literal co-present-max-width contract: text-balance
            // borrows the element's max-width slot and nulls it on detach, so the utility's own value has
            // to come back. max-w-[50px] is inline-resolved, so it is exactly the kind of token that never
            // appears on the live class list the variant path re-derives from.
            using var scope = new ReconcilerScope();
            var tree = new VNode[] { V.Label(className: "max-w-[50px] dark:text-balance", text: "hello") };
            scope.Reconciler.Reconcile(scope.Root, System.Array.Empty<VNode>(), tree);
            var label = scope.Root.Q<Label>();
            Assume.That(label.style.maxWidth.value.value, Is.EqualTo(50f),
                "Precondition: the co-present max-w-[50px] utility resolved its own value at mount");
            VelvetTheme.IsDark = true;
            Assume.That(scope.Reconciler.Context.TextBalanceManipulators.Count, Is.EqualTo(1),
                "Precondition: the dark payload attached the manipulator that borrows the slot");

            // Act — the only thing that changes is the theme; nothing re-renders.
            VelvetTheme.IsDark = false;

            // Assert
            Assert.That(label.style.maxWidth.value.value, Is.EqualTo(50f));
        }

        [Test]
        public void Given_ATrackedElementWithALiteralTextBalanceBesideAMaxWidth_When_TextBalanceIsRenderedAway_Then_TheUtilitysMaxWidthIsRestored()
        {
            // Arrange — no variant gates text-balance here; the dark:gap-4 is only there to put the element
            // in the variant-applied table, which is what switches its layout gates onto the live class
            // list. The teardown then runs on an ordinary re-render, with no variant toggle involved.
            using var scope = new ReconcilerScope();
            var tree1 = new VNode[] { V.Label(className: "max-w-[50px] text-balance dark:gap-4", text: "hello") };
            scope.Reconciler.Reconcile(scope.Root, System.Array.Empty<VNode>(), tree1);
            var label = scope.Root.Q<Label>();
            Assume.That(label.style.maxWidth.value.value, Is.EqualTo(50f),
                "Precondition: the co-present max-w-[50px] utility resolved its own value at mount");
            VelvetTheme.IsDark = true;
            Assume.That(label.ClassListContains("gap-4"), Is.True,
                "Precondition: the dark payload put a layout gate class on the live class list");

            // Act — re-render without the text-balance token, everything else unchanged.
            var tree2 = new VNode[] { V.Label(className: "max-w-[50px] dark:gap-4", text: "hello") };
            scope.Reconciler.Reconcile(scope.Root, tree1, tree2);

            // Assert
            Assert.That(label.style.maxWidth.value.value, Is.EqualTo(50f));
        }

        [Test]
        public void Given_ATextBalanceBesideAVariantSuppliedMaxWidth_When_TextBalanceIsRenderedAway_Then_TheVariantsMaxWidthIsRestored()
        {
            // Arrange — the max-width here is supplied by the dark: layer, not by a utility of the
            // element's own. That is the case no class-list scan can restore, on either array: a variant
            // token is skipped, because its payload is not a token the element itself carries. Nothing
            // gates layout on this element, so it also proves the restore on the ordinary reconcile path.
            using var scope = new ReconcilerScope();
            var tree1 = new VNode[] { V.Label(className: "dark:max-w-[80px] text-balance", text: "hello") };
            scope.Reconciler.Reconcile(scope.Root, System.Array.Empty<VNode>(), tree1);
            var label = scope.Root.Q<Label>();
            VelvetTheme.IsDark = true;
            Assume.That(label.style.maxWidth.value.value, Is.EqualTo(80f),
                "Precondition: the dark payload resolved its own max-width before text-balance is removed");

            // Act — remove just the text-balance token; the dark: layer is untouched.
            var tree2 = new VNode[] { V.Label(className: "dark:max-w-[80px]", text: "hello") };
            scope.Reconciler.Reconcile(scope.Root, tree1, tree2);

            // Assert
            Assert.That(label.style.maxWidth.value.value, Is.EqualTo(80f));
        }
    }
}
