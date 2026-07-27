using System;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Coverage for the projection at the level where the live class list IS the question rather than a proxy
    /// for one: tokens carrying no USS rule — a user's own class, and the families Velvet realises in C# —
    /// which have no resolved style to read at all. Everything whose real question is a resolved value is
    /// pinned on a panel by <see cref="VariantClassProjectionPanelTests"/>. GWT, one assert per case.
    /// </summary>
    [TestFixture]
    internal sealed class ClassProjectionModelTests
    {
        private static VisualElement WithBaseClasses(params string[] classes)
        {
            var element = new VisualElement();
            foreach (var cls in classes)
            {
                StyleClassProjection.Add(element, cls, StyleLayerPriority.Base);
            }
            return element;
        }

        [Test]
        public void Given_TwoUserAuthoredClassesOnDifferentLayers_When_TheUpperOneApplies_Then_TheLowerOneSurvives()
        {
            // Arrange — neither class carries a bundled USS rule, so the projection knows nothing about what
            // they write and cannot rank them.
            var element = WithBaseClasses("my-card");

            // Act
            StyleClassProjection.Add(element, "my-card-dark", StyleLayerPriority.Dark);

            // Assert
            Assert.IsTrue(element.ClassListContains("my-card"));
        }

        [Test]
        public void Given_AUserAuthoredBaseAndAnImportantPayload_When_ThePayloadApplies_Then_TheBaseStillSurvives()
        {
            // Arrange — the important band raises the payload's layer, but with no property set on either side
            // there is nothing for the ranking to act on, so the bang cannot break this tie either.
            var element = WithBaseClasses("my-card");

            // Act
            StyleClassProjection.Add(element, "my-card-dark", StyleLayerPriority.ImportantOf(StyleLayerPriority.Dark));

            // Assert
            Assert.IsTrue(element.ClassListContains("my-card"));
        }

        [Test]
        public void Given_OneTokenHeldByTwoVariants_When_TheHigherOneTurnsOff_Then_TheTokenStays()
        {
            // Arrange — the pre-projection class toggle was not reference-counted, so whichever variant
            // turned off first took the shared token with it.
            var element = new VisualElement();
            StyleVariantPayload.Apply(element, new string?[] { "gap-4" }, true, StyleLayerPriority.Dark);
            StyleVariantPayload.Apply(element, new string?[] { "gap-4" }, true, StyleLayerPriority.ResponsiveMd);

            // Act
            StyleVariantPayload.Apply(element, new string?[] { "gap-4" }, false, StyleLayerPriority.Dark);

            // Assert
            Assert.IsTrue(element.ClassListContains("gap-4"));
        }

        [Test]
        public void Given_ATokenWrittenBothLiterallyAndBehindAVariant_When_TheVariantTurnsOff_Then_TheLiteralStays()
        {
            // Arrange — the literal token was written straight onto the element before any model existed, so
            // the model has to adopt it when one is built or the payload's off-toggle takes it along.
            var element = WithBaseClasses("gap-4");
            StyleVariantPayload.Apply(element, new string?[] { "gap-4" }, true, StyleLayerPriority.ResponsiveMd);

            // Act
            StyleVariantPayload.Apply(element, new string?[] { "gap-4" }, false, StyleLayerPriority.ResponsiveMd);

            // Assert
            Assert.IsTrue(element.ClassListContains("gap-4"));
        }

        [Test]
        public void Given_ALiteralTokenAndAPayloadThatNeverApplied_When_ThePayloadEvaluatesOff_Then_TheLiteralStays()
        {
            // Arrange — the structural, has-, attribute and supports- families evaluate an unconditional off
            // for a rule that never matched, and it reaches an element carrying no model at all.
            var element = WithBaseClasses("gap-4");

            // Act
            StyleVariantPayload.Apply(element, new string?[] { "gap-4" }, false, StyleLayerPriority.Structural);

            // Assert
            Assert.IsTrue(element.ClassListContains("gap-4"));
        }

        [Test]
        public void Given_APooledElementWhoseBaseClassWasSuppressed_When_ItIsResetAndReused_Then_TheBaseApplies()
        {
            // Arrange — a Label that reached the pool while a dark payload was holding background-color.
            var label = new Label();
            StyleClassProjection.Add(label, "bg-white", StyleLayerPriority.Base);
            StyleClassProjection.Add(label, "bg-neutral-900", StyleLayerPriority.Dark);
            var suppressedBeforeReset = label.ClassListContains("bg-white");

            // Act
            FiberLabelPoolHelper.ResetLabelForReuse(label);
            StyleClassProjection.Add(label, "bg-white", StyleLayerPriority.Base);

            // Assert — the next consumer's own base class is not judged against the previous one's layers.
            Assert.AreEqual((false, true), (suppressedBeforeReset, label.ClassListContains("bg-white")));
        }
    }

    /// <summary>
    /// The same pool-reuse invariant one altitude up, driven through the real removal-to-rent cycle
    /// (<c>FiberElementCleaner</c>, then <c>VNodePool</c>) rather than by calling the reset helper directly, so
    /// the two scrub points are pinned independently. GWT, one assert.
    /// </summary>
    [TestFixture]
    internal sealed class ClassProjectionPoolCycleTests : PanelTestBase
    {
        private readonly record struct ShowState(bool Show);

        private sealed class ShowStore : Store<ShowState>
        {
            public ShowStore() : base(new ShowState(true)) { }
            public void Set(bool show) => SetState(_ => new ShowState(show));
            protected override void ResetCore() => SetState(_ => new ShowState(true));
        }

        private static ShowStore s_store;

        // The dark manipulator seeds its gate when the element attaches to a panel, so a detached root would
        // never apply the payload this fixture needs to be in force before the pool return.
        public override void SetUp()
        {
            base.SetUp();
            s_store = null;
        }

        public override void TearDown()
        {
            VelvetTheme.IsDark = false;
            base.TearDown();
        }

        [Component]
        private static VNode Card()
        {
            var show = Hooks.UseStore(s_store, s => s.Show);
            return V.Div(name: "card", children: show
                ? new VNode[] { V.Label(name: "cell", className: "bg-white dark:bg-neutral-900", text: "x") }
                : Array.Empty<VNode>());
        }

        [Test]
        public void Given_ALabelPooledWhileItsBaseColourWasSuppressed_When_ItIsRentedBack_Then_TheBaseColourApplies()
        {
            // Arrange — mounted in dark mode, so the payload holds background-color and the base class is off
            // the element; hiding then returns that very Label to the pool.
            VelvetTheme.IsDark = true;
            using var store = new ShowStore();
            s_store = store;
            var root = _window.rootVisualElement;
            _mounted = V.Mount(root, V.Component(Card, key: "card"));
            var scheduler = _mounted.Root.Reconciler.Context.BatchScheduler;
            var before = root.Q<Label>("cell");
            var suppressedWhileDark = before.ClassListContains("bg-white");
            store.Set(false);
            scheduler.DrainImmediateForTest();

            // Act — the theme goes light and the card is shown again, renting the Label back.
            VelvetTheme.IsDark = false;
            store.Set(true);
            scheduler.DrainImmediateForTest();

            // Assert — the recycled Label carries no verdict from its previous mount. The instance identity
            // is part of the assertion because a freshly allocated Label satisfies the rest of it: were the
            // rent path to stop handing this one back, the case would pass while covering nothing.
            var after = root.Q<Label>("cell");
            Assert.AreEqual((true, false, true),
                (ReferenceEquals(before, after), suppressedWhileDark, after.ClassListContains("bg-white")));
        }
    }
}
