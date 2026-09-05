using System;
using System.Linq;
using System.Reflection;
using Mono.Cecil;
using NUnit.Framework;
using UnityEngine.UIElements;

namespace Velvet.Tests
{
    /// <summary>
    /// Whether each production reader of an element's live class list resolves its answer from the ORDER the
    /// classes were added in, or only from the SET of them. Where a case permutes that order, what it
    /// compares is what the reader RESOLVED — never the order the class list returned, which would put one
    /// query on both sides of a comparison.
    /// </summary>
    /// <remarks>
    /// The roster case is what makes a reader written later arrive with a verdict of its own rather than
    /// unnoticed. The cases whose verdict is that the order decides are the ones an editor bump has to
    /// settle: they name what today's answer rests on, so that question is a run rather than a re-audit.
    /// </remarks>
    [TestFixture]
    internal sealed class LiveClassListOrderTests
    {
        // Every method in the runtime assembly that enumerates a live class list. Held here rather than
        // derived, because the point is that a method joining the list has to stop and be given a verdict.
        private static readonly string[] LiveClassListReaders =
        {
            "Velvet.FiberNodePatcher.LiveClasses",
            "Velvet.MotionNativeTransitionGuard.DeclaredSlots",
            "Velvet.StyleAnimationScheduler.ReapplyMotionOwnedInlineValues",
            "Velvet.StyleClassProjection/Model.SeedBaseLayer",
            "Velvet.StyleClipPathClass.TryExtractLive",
            "Velvet.StyleTextBalanceClass.DeclaresWidthClass",
        };

        private static readonly MethodInfo RouteOneClass = typeof(FiberNodePatcher)
            .GetMethod("AddClass", BindingFlags.NonPublic | BindingFlags.Static)!;

        private static readonly MethodInfo MaterializeLiveClasses = typeof(FiberNodePatcher)
            .GetMethod("LiveClasses", BindingFlags.NonPublic | BindingFlags.Static)!;

        // The reconciler's own routing decides whether a token reaches the class list at all or resolves to
        // inline style instead, so an element arranged by calling AddToClassList directly can carry a token
        // the routing would have kept off it. This is the create path; the patch path carries its own copy
        // of the same routing, which the last case below holds beside this one.
        private static VisualElement Carrying(params string[] classNames)
        {
            var element = new VisualElement();
            FiberElementFactory.ApplyClassNames(element, classNames);
            return element;
        }

        private static VisualElement Patched(params string[] classNames)
        {
            var element = new VisualElement();
            foreach (var cls in classNames)
            {
                RouteOneClass.Invoke(null, new object[] { element, cls });
            }
            return element;
        }

        private static string[] LiveClasses(VisualElement element)
            => (string[])MaterializeLiveClasses.Invoke(null, new object[] { element })!;

        private static string SortedClassList(VisualElement element)
            => string.Join(" ", element.GetClasses().OrderBy(cls => cls, StringComparer.Ordinal));

        // GREEN_ON_BASE(construction): the roster names methods the base's own assembly holds.
        // The two sides therefore agree on a base run, which cannot separate them. Add an
        // `element.GetClasses()` call to a production method and this case reddens.
        [Test]
        public void Given_TheRuntimeAssembly_When_ItsBodiesAreReadForLiveClassListEnumerations_Then_EveryMethodHoldingOneIsOnTheRoster()
        {
            // Arrange — read from IL rather than from the sources: a sibling in the reconciler spells the
            // enumeration in a comment saying it is NOT what that path reads, and a text scan counts it.
            using var runtime = ModuleDefinition.ReadModule(typeof(V).Assembly.Location);

            // Act
            var measured = runtime.GetTypes()
                .SelectMany(type => type.Methods)
                .Where(method => method.HasBody)
                .Where(method => method.Body.Instructions.Any(instruction =>
                    instruction.Operand is MethodReference reference
                    && reference.Name == nameof(VisualElement.GetClasses)
                    && reference.DeclaringType.FullName == typeof(VisualElement).FullName))
                .Select(method => $"{method.DeclaringType.FullName}.{method.Name}")
                .Distinct()
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToList();

            // Assert
            Assert.That(string.Join("\n", measured), Is.EqualTo(string.Join("\n", LiveClassListReaders)),
                "a method reads the live class list with no verdict here on whether its answer depends on "
                + "the order the classes arrived in; give it a case in this fixture and list it above");
        }

        // GREEN_ON_BASE(characterization): the base already resolves the last clip token on the list.
        // What shows the case can fail is a `break` after the first match in TryExtractLive: measured,
        // each arrangement then resolves the shape it was handed first and the pair inverts.
        [Test]
        public void Given_TwoClipShapesOnOneElement_When_TheOrderTheyWereAddedInIsReversed_Then_TheShapeThatResolvesIsTheOneAddedLast()
        {
            // Arrange — a variant clip payload lands on the live list beside the base clip, so two clip
            // tokens on one element is the shape this reading exists for.
            const string circle = "clip-path-[circle(40%)]";
            const string inset = "clip-path-[inset(10%)]";
            var added = Carrying(circle, inset);
            var reversed = Carrying(inset, circle);

            // Act
            StyleClipPathClass.TryExtractLive(added, out var fromAdded);
            StyleClipPathClass.TryExtractLive(reversed, out var fromReversed);

            // Assert
            Assert.That((fromAdded?.Source, fromReversed?.Source), Is.EqualTo((inset, circle)),
                "this reading resolves whichever clip token the class list hands over last");
        }

        // GREEN_ON_BASE(characterization): the base already resolves the last gap token on the list.
        // What shows the case can fail is a `break` after the first match in StyleGapClass.TryExtract:
        // measured, each arrangement then takes the gap it was handed first and the pair inverts.
        [Test]
        public void Given_TwoGapTokensOnOneElement_When_TheOrderTheyWereAddedInIsReversed_Then_TheGapTheReSyncResolvesIsTheOneAddedLast()
        {
            // Arrange — LiveClasses is the class source a re-sync uses when the element has no recorded
            // array of its own, and the gap extractor it feeds takes the last token that parses.
            var added = Carrying("gap-4", "gap-8");
            var reversed = Carrying("gap-8", "gap-4");

            // Act
            StyleGapClass.TryExtract(LiveClasses(added), out var fromAdded, out _);
            StyleGapClass.TryExtract(LiveClasses(reversed), out var fromReversed, out _);

            // Assert — 16px and 32px are the shared spacing scale's own values for the two tokens.
            Assert.That((fromAdded, fromReversed), Is.EqualTo((32f, 16f)),
                "the gap this path configures the manipulator with is whichever gap token the class list "
                + "hands over last");
        }

        // GREEN_ON_BASE(characterization): the base already answers this from the set, not the order.
        // What shows the case can fail is trading the `return true` in DeclaresWidthClass for an
        // assignment that keeps scanning: measured, the arrangement ending on w-auto then answers false.
        [Test]
        public void Given_AWidthTokenBesideTheAutoToken_When_TheOrderTheyWereAddedInIsReversed_Then_TheBalanceVerdictIsTheSameBothWays()
        {
            // Arrange — w-auto declares nothing to stand down for, so a reading that took the last matching
            // token rather than any of them answers differently depending on which arrived second.
            var added = Carrying("w-32", "w-auto");
            var reversed = Carrying("w-auto", "w-32");

            // Act
            var fromAdded = StyleTextBalanceClass.DeclaresWidthClass(added);
            var fromReversed = StyleTextBalanceClass.DeclaresWidthClass(reversed);

            // Assert — both true rather than merely equal: two falses would agree while measuring nothing.
            Assert.That((fromAdded, fromReversed), Is.EqualTo((true, true)),
                "the balance manipulator stands down for a declared width wherever it sits in the list");
        }

        // GREEN_ON_BASE(characterization): the base already picks this winner by cascade position.
        // What shows the case can fail is widening `position > winningPosition` to take every match:
        // measured, the two arrangements then declare different slots.
        [Test]
        public void Given_TwoTransitionUtilitiesOnOneElement_When_TheOrderTheyWereAddedInIsReversed_Then_TheDeclaredSlotsAreTheSameBothWays()
        {
            // Arrange — the two sit at different positions in the bundled cascade, so the winner by cascade
            // position and the winner by arrival order are different utilities in one of the two orders.
            var added = Carrying("transition-colors", "transition-transform");
            var reversed = Carrying("transition-transform", "transition-colors");
            var colorsAlone = MotionNativeTransitionGuard.DeclaredSlots(Carrying("transition-colors"));
            var transformAlone = MotionNativeTransitionGuard.DeclaredSlots(Carrying("transition-transform"));

            // Act
            var fromAdded = MotionNativeTransitionGuard.DeclaredSlots(added);
            var fromReversed = MotionNativeTransitionGuard.DeclaredSlots(reversed);

            // Assert — that the two utilities declare different slots rides along, since a pair that
            // declared the same ones would agree here whatever decided between them.
            Assert.That((fromAdded, fromReversed, colorsAlone == transformAlone),
                Is.EqualTo((colorsAlone, colorsAlone, false)),
                "the later cascade position wins this reading, not the later place in the class list");
        }

        // GREEN_ON_BASE(characterization): the base already ranks a base class against the bands above.
        // What shows the case can fail is `JudgeBand` claiming each entry's properties as it goes:
        // measured, the second padding token is then judged dead against the first and leaves the list.
        [Test]
        public void Given_TwoBaseClassesWritingOnePropertyUnderAPayload_When_TheOrderTheyWereAddedInIsReversed_Then_TheSurvivingClassesAreTheSameBothWays()
        {
            // Arrange — the payload above them is what builds the model, and the model seeds its base layer
            // from the live class list. Both base classes write padding, so a seed that let an earlier entry
            // claim against a later one would keep whichever of them arrived first.
            var added = Carrying("p-4", "p-8");
            var reversed = Carrying("p-8", "p-4");
            StyleClassProjection.Add(added, "my-token", StyleLayerPriority.Dark);
            StyleClassProjection.Add(reversed, "my-token", StyleLayerPriority.Dark);

            // Act
            var fromAdded = SortedClassList(added);
            var fromReversed = SortedClassList(reversed);

            // Assert — both spelled out rather than compared to each other: two empty lists would agree.
            Assert.That((fromAdded, fromReversed),
                Is.EqualTo(("my-token p-4 p-8", "my-token p-4 p-8")),
                "a base class is ranked against the bands above it, not against the base classes seeded "
                + "beside it, so the seed order cannot decide which of them survives");
        }

        // GREEN_ON_BASE(characterization): the base already routes a resolver-owned token to inline style.
        // What shows the case can fail is either routing sending every token to StyleClassProjection.Add:
        // measured on each of the two, all five then reach that path's class list.
        [Test]
        public void Given_TheTokenFamiliesTheMotionReapplyLooksFor_When_EitherRoutingRunsThem_Then_OnlyTheOneNoResolverClaimsReachesTheLiveClassList()
        {
            // Arrange — the four bracket and static-scale families the settle path re-applies inline values
            // for, beside one plain utility. Routing decides which of them a class list can hold at all.
            var tokens = new[] { "w-[240px]", "translate-x-4", "opacity-[.5]", "bg-[#ff0000]", "p-4" };
            var created = Carrying(tokens);
            var patched = Patched(tokens);

            // Act — each path's live class list, with the width slot the first token owns beside it.
            var fromCreate = $"{SortedClassList(created)} | {created.style.width.value.value}";
            var fromPatch = $"{SortedClassList(patched)} | {patched.style.width.value.value}";

            // Assert — the plain utility alone on both paths, and the resolved width showing where a token
            // the resolver owns went instead: dropped outright it would leave the same class list.
            Assert.That((fromCreate, fromPatch), Is.EqualTo(("p-4 | 240", "p-4 | 240")),
                "a token a resolver owns must not reach the USS class list; one that does gets its inline "
                + "value re-applied from the live list, in whatever order that list holds it");
        }
    }
}
