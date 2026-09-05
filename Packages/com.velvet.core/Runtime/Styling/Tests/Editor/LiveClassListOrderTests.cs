using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Mono.Cecil;
using Mono.Cecil.Cil;
using NUnit.Framework;
using UnityEngine;
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
    /// Two cases hold the roster: one reads the enumerations off the runtime assembly and the verdicts off
    /// the attributes here, the other reads the IL of the cases carrying those attributes and requires the
    /// body under a verdict to reach the reader it names. A declaration is therefore answered by a body
    /// rather than by its string. What that still leaves open is a reader some case already runs while
    /// arranging something else, and whether the assertion under a verdict measures the verdict at all —
    /// neither is mechanical, and both stay a reviewer's to check.
    /// The cases whose verdict is that the order decides are the ones an editor bump has to settle: they
    /// name what today's answer rests on, so that question is a run rather than a re-audit.
    /// </remarks>
    [TestFixture]
    internal sealed class LiveClassListOrderTests
    {
        // The runtime assembly's own spelling of each reader, in the form Spelled builds: return type,
        // declaring type, method name with its generic arity, and full parameter type names.
        private const string LiveClassesReader =
            "System.String[] Velvet.FiberNodePatcher.LiveClasses(UnityEngine.UIElements.VisualElement)";
        private const string TransitionSlotsReader =
            "Velvet.MotionTransitionSlots Velvet.MotionNativeTransitionGuard.DeclaredSlots("
            + "UnityEngine.UIElements.VisualElement, System.Boolean)";
        private const string MotionReapplyReader =
            "System.Void Velvet.StyleAnimationScheduler.ReapplyMotionOwnedInlineValues("
            + "UnityEngine.UIElements.VisualElement)";
        private const string BaseLayerSeedReader =
            "System.Void Velvet.StyleClassProjection/Model.SeedBaseLayer(UnityEngine.UIElements.VisualElement)";
        private const string ClipShapeReader =
            "System.Boolean Velvet.StyleClipPathClass.TryExtractLive("
            + "UnityEngine.UIElements.VisualElement, Velvet.ClipPathSpec&)";
        private const string BalanceWidthReader =
            "System.Boolean Velvet.StyleTextBalanceClass.DeclaresWidthClass("
            + "UnityEngine.UIElements.VisualElement)";

        // Marks the case that measures one reader's verdict. The roster reads these off the methods carrying
        // [Test] rather than off a list of its own, and the case beside it reads the marked method's IL, so
        // a verdict costs a case whose body reaches the reader.
        [AttributeUsage(AttributeTargets.Method)]
        private sealed class ReaderVerdictAttribute : Attribute
        {
            public ReaderVerdictAttribute(string reader) => Reader = reader;

            public string Reader { get; }
        }

        private static readonly MethodInfo RouteOneClass = typeof(FiberNodePatcher)
            .GetMethod("AddClass", BindingFlags.NonPublic | BindingFlags.Static)!;

        private static readonly MethodInfo MaterializeLiveClasses = typeof(FiberNodePatcher)
            .GetMethod("LiveClasses", BindingFlags.NonPublic | BindingFlags.Static)!;

        private static readonly MethodInfo SettleMotionOwnedInlineValues = typeof(StyleAnimationScheduler)
            .GetMethod("ReapplyMotionOwnedInlineValues", BindingFlags.NonPublic | BindingFlags.Static)!;

        private readonly Dictionary<FilterFunctionDefinition, string> _customFilterNames = new();

        [TearDown]
        public void TearDown()
        {
            foreach (var pair in _customFilterNames)
            {
                VelvetFilters.Unregister(pair.Value);
                if (pair.Key != null)
                {
                    UnityEngine.Object.DestroyImmediate(pair.Key);
                }
            }
            _customFilterNames.Clear();
        }

        // The reconciler's own routing decides whether a token reaches the class list at all, so an element
        // arranged by calling AddToClassList directly can carry a token the routing would have kept off it.
        // This is the create path; the patch path carries its own copy of the same routing, which the
        // routing case below holds beside this one.
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

        private static void Settle(VisualElement element)
            => SettleMotionOwnedInlineValues.Invoke(null, new object[] { element });

        // The name is kept beside the definition so the composed order can be read back by name rather than
        // by definition reference.
        private void RegisterCustomFilter(string name)
        {
            var definition = ScriptableObject.CreateInstance<FilterFunctionDefinition>();
            definition.parameters = new[]
            {
                new FilterParameterDeclaration
                {
                    name = "amount",
                    interpolationDefaultValue = new FilterParameter(0f),
                },
            };
            _customFilterNames[definition] = name;
            VelvetFilters.Register(name, definition);
        }

        // A function this fixture registered no name for is rendered by its type, so a list holding
        // something else reads as that rather than as an absence.
        private string ComposedCustomFilters(VisualElement element)
        {
            var functions = element.style.filter.value;
            if (functions == null)
            {
                return string.Empty;
            }
            var names = new List<string>();
            for (var i = 0; i < functions.Count; i++)
            {
                var definition = functions[i].customDefinition;
                names.Add(definition != null && _customFilterNames.TryGetValue(definition, out var name)
                    ? name
                    : functions[i].type.ToString());
            }
            return string.Join(" ", names);
        }

        // GREEN_ON_BASE(construction): both sides here are the repository's own content — the runtime
        // assembly's IL and the verdicts this fixture's own cases declare. The two therefore agree on a
        // base run, which cannot separate them. Add an `element.GetClasses()` call to a production method
        // that holds none and this case reddens.
        [Test]
        public void Given_TheRuntimeAssembly_When_ItsBodiesAreReadForLiveClassListEnumerations_Then_EveryMethodHoldingOneHasACaseDeclaringItsVerdict()
        {
            // Arrange — read from IL rather than from the sources: a sibling in the reconciler spells the
            // enumeration in a comment saying it is NOT what that path reads, and a text scan counts it.
            using var runtime = ModuleDefinition.ReadModule(typeof(V).Assembly.Location);

            // Act
            var measured = LiveClassListReaders(runtime)
                .Select(method => Spelled(method))
                .Distinct()
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToList();

            // Assert
            Assert.That(string.Join("\n", measured), Is.EqualTo(string.Join("\n", VerdictsDeclaredHere()
                    .Select(pair => pair.Reader)
                    .Distinct()
                    .OrderBy(name => name, StringComparer.Ordinal))),
                "the two sides name different readers: a method reads the live class list with no case here "
                + "on whether its answer depends on the order the classes arrived in, or a case declares a "
                + "verdict for a method that no longer reads it and the declaration has to go");
        }

        // GREEN_ON_BASE(construction): both sides here are this assembly's own content.
        // The verdict attributes and the IL of the bodies carrying them therefore agree on a base run,
        // which cannot separate them. Move a `[ReaderVerdict]` line onto a case that calls no reader —
        // the roster case itself, or an empty one — and this is what reddens.
        [Test]
        public void Given_EachVerdictDeclaredHere_When_TheILOfTheCaseCarryingItIsRead_Then_ThatCaseReachesTheReaderItNames()
        {
            // Arrange — a reader this fixture drives through a reflection handle held anywhere but a static
            // field of this type is invisible to the graph, so a verdict resting on one can read as
            // unreached: a false red rather than a hole.
            using var runtime = ModuleDefinition.ReadModule(typeof(V).Assembly.Location);
            using var here = ModuleDefinition.ReadModule(typeof(LiveClassListOrderTests).Assembly.Location);
            var fixtureType = here.GetTypes()
                .Single(type => type.FullName == typeof(LiveClassListOrderTests).FullName);
            var edges = CallEdges(fixtureType, runtime, here);

            // Act
            var unreached = VerdictsDeclaredHere()
                .Where(pair => !Reaches(edges, Spelled(InIl(fixtureType, pair.Case)), pair.Reader))
                .Select(pair => $"{pair.Case.Name} declares {pair.Reader}")
                .OrderBy(line => line, StringComparer.Ordinal);

            // Assert — that a verdict was found at all rides along: an attribute nothing carried would
            // leave nothing to call unreached and agree with a fixture whose every case reached its reader.
            Assert.That((string.Join("\n", unreached), VerdictsDeclaredHere().Any()),
                Is.EqualTo((string.Empty, true)),
                "a verdict names a reader the body carrying it never reaches, so what signs that reader off "
                + "is a line rather than a body that gets to it");
        }

        // GREEN_ON_BASE(construction): both sides here are the runtime assembly's own IL.
        // The two therefore agree on a base run, which cannot separate them. Add a write to
        // `state.PaintTail` in a body that records no class array and this is what reddens.
        [Test]
        public void Given_TheRuntimeAssembly_When_ItsWritesToARecordedPaintVerdictAreRead_Then_EachSitsInABodyRecordingTheClassArrayToo()
        {
            // Arrange — the live class list stands in for the recorded array on the re-sync's one path that
            // has none, and it drives the layout gates alone because the paint sequence under them runs only
            // for a state carrying a paint verdict. What confines it there is that the verdict is written
            // only where the array is; what the stand-in can still move is the layout gates' own last-wins
            // values, which the cases declaring the live-class-list reader pin.
            using var runtime = ModuleDefinition.ReadModule(typeof(V).Assembly.Location);
            var state = typeof(VariantGateState).FullName;

            // Act
            var verdictWriters = runtime.GetTypes()
                .SelectMany(type => type.Methods)
                .Where(method => method.HasBody)
                .Where(method => Writes(method, state, nameof(VariantGateState.PaintTail)))
                .OrderBy(method => Spelled(method), StringComparer.Ordinal)
                .ToList();
            var alsoRecordingTheArray = verdictWriters
                .Where(method => Writes(method, state, nameof(VariantGateState.Reconciled)));

            // Assert — the non-empty term rides along: a field name the scan resolved nowhere would leave
            // both sides empty and agree.
            Assert.That(
                (string.Join("\n", alsoRecordingTheArray.Select(method => Spelled(method))),
                    verdictWriters.Count > 0),
                Is.EqualTo((string.Join("\n", verdictWriters.Select(method => Spelled(method))), true)),
                "a paint verdict is recorded on a variant gate state by a body that records no class array "
                + "beside it, so a state can carry the verdict with no array beside it and the re-sync's "
                + "live-class-list stand-in stops being confined to the layout gates");
        }

        // GREEN_ON_BASE(characterization): the base already resolves the last clip token on the list.
        // What shows the case can fail is a `break` after the first match in TryExtractLive: measured,
        // each arrangement then resolves the shape it was handed first and the pair inverts.
        [Test]
        [ReaderVerdict(ClipShapeReader)]
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
        [ReaderVerdict(LiveClassesReader)]
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

        // GREEN_ON_BASE(characterization): the base already resolves the last grid-cols token on the list.
        // What shows the case can fail is a `break` after the first match in StyleGridClass.TryExtract:
        // measured, each arrangement then takes the column count it was handed first and the pair inverts.
        [Test]
        [ReaderVerdict(LiveClassesReader)]
        public void Given_TwoColumnCountsOnOneElement_When_TheOrderTheyWereAddedInIsReversed_Then_TheColumnCountTheReSyncResolvesIsTheOneAddedLast()
        {
            // Arrange — the same re-sync hands its class source to the grid manipulator as well, and the
            // column count that manipulator is configured with is the last grid-cols token that parses.
            var added = Carrying("grid-cols-2", "grid-cols-4");
            var reversed = Carrying("grid-cols-4", "grid-cols-2");

            // Act
            StyleGridClass.TryExtract(LiveClasses(added), out var fromAdded);
            StyleGridClass.TryExtract(LiveClasses(reversed), out var fromReversed);

            // Assert
            Assert.That((fromAdded, fromReversed), Is.EqualTo((4, 2)),
                "the column count this path configures the grid with is whichever grid-cols token the "
                + "class list hands over last");
        }

        // GREEN_ON_BASE(characterization): the base already prefers a parsed count to the bare marker.
        // What shows the case can fail is assigning `columns = 1` where the marker is seen rather than in
        // the tail of the scan: measured, the arrangement ending on the marker then resolves one column.
        [Test]
        [ReaderVerdict(LiveClassesReader)]
        public void Given_TheBareGridMarkerBesideAColumnCount_When_TheOrderTheyWereAddedInIsReversed_Then_TheColumnCountIsTheSameBothWays()
        {
            // Arrange — of the tokens the order-deciding readings resolve from, the bare marker is the one
            // that also declares a property of its own, so what keeps the projection's re-append clear of
            // this reading is that the marker's position decides nothing rather than that it can never be
            // suppressed. The table case below holds the rest, which declare none.
            var added = Carrying("grid", "grid-cols-2");
            var reversed = Carrying("grid-cols-2", "grid");

            // Act
            StyleGridClass.TryExtract(LiveClasses(added), out var fromAdded);
            StyleGridClass.TryExtract(LiveClasses(reversed), out var fromReversed);

            // Assert — the column count spelled out rather than compared to itself: two single columns
            // would agree while the marker decided both.
            Assert.That((fromAdded, fromReversed), Is.EqualTo((2, 2)),
                "the bare grid marker contributes its single column only where no grid-cols token parsed, "
                + "so where one did the marker cannot decide the count from either end of the list");
        }

        // GREEN_ON_BASE(characterization): the base already resolves the last gap-x token on the list.
        // What shows the case can fail is a first-wins guard on the horizontal arm of the scan in
        // StyleGridClass.ExtractGaps: measured, each arrangement then takes the gap it was handed first.
        [Test]
        [ReaderVerdict(LiveClassesReader)]
        public void Given_TwoColumnGapsOnOneElement_When_TheOrderTheyWereAddedInIsReversed_Then_TheGapTheGridResolvesIsTheOneAddedLast()
        {
            // Arrange — a grid routes its own gap rather than leaving it to the gap manipulator, so this is
            // a second last-wins reading of the same class source, accumulating per axis.
            var added = Carrying("gap-x-4", "gap-x-8");
            var reversed = Carrying("gap-x-8", "gap-x-4");

            // Act
            StyleGridClass.ExtractGaps(LiveClasses(added), out var fromAdded, out _);
            StyleGridClass.ExtractGaps(LiveClasses(reversed), out var fromReversed, out _);

            // Assert — 16px and 32px are the shared spacing scale's own values for the two tokens.
            Assert.That((fromAdded, fromReversed), Is.EqualTo((32f, 16f)),
                "the column gap this path configures the grid with is whichever gap-x token the class list "
                + "hands over last");
        }

        // GREEN_ON_BASE(characterization): the base already resolves the last gap-y token on the list.
        // What shows the case can fail is a first-wins guard on the vertical arm of the same scan:
        // measured, each arrangement then takes the gap it was handed first and the pair inverts.
        [Test]
        [ReaderVerdict(LiveClassesReader)]
        public void Given_TwoRowGapsOnOneElement_When_TheOrderTheyWereAddedInIsReversed_Then_TheRowGapTheGridResolvesIsTheOneAddedLast()
        {
            // Arrange — the same scan accumulates the two axes independently, so the column gap the case
            // above pins says nothing about which gap-y token the row gap ends on.
            var added = Carrying("gap-y-4", "gap-y-8");
            var reversed = Carrying("gap-y-8", "gap-y-4");

            // Act
            StyleGridClass.ExtractGaps(LiveClasses(added), out _, out var fromAdded);
            StyleGridClass.ExtractGaps(LiveClasses(reversed), out _, out var fromReversed);

            // Assert — 16px and 32px are the shared spacing scale's own values for the two tokens.
            Assert.That((fromAdded, fromReversed), Is.EqualTo((32f, 16f)),
                "the row gap this path configures the grid with is whichever gap-y token the class list "
                + "hands over last");
        }

        // GREEN_ON_BASE(characterization): the base already resolves the last divide axis token on the list.
        // What shows the case can fail is a `break` after the first match in StyleDivideClass.TryExtract:
        // measured, each arrangement then takes the width it was handed first and the pair inverts.
        [Test]
        [ReaderVerdict(LiveClassesReader)]
        public void Given_TwoDividerWidthsOnOneElement_When_TheOrderTheyWereAddedInIsReversed_Then_TheWidthTheReSyncResolvesIsTheOneAddedLast()
        {
            // Arrange — the divide manipulator is the third reader the same class source feeds, and it
            // accumulates a single axis + width from whichever axis token parses last.
            var added = Carrying("divide-x-2", "divide-x-8");
            var reversed = Carrying("divide-x-8", "divide-x-2");

            // Act
            StyleDivideClass.TryExtract(LiveClasses(added), out var fromAdded);
            StyleDivideClass.TryExtract(LiveClasses(reversed), out var fromReversed);

            // Assert
            Assert.That((fromAdded.Width, fromReversed.Width), Is.EqualTo((8f, 2f)),
                "the divider width this path configures the manipulator with is whichever divide axis "
                + "token the class list hands over last");
        }

        // GREEN_ON_BASE(characterization): the base already resolves the last divide style token.
        // What shows the case can fail is a first-wins guard on the style arm of the same scan: measured,
        // each arrangement then takes the style it was handed first and the pair inverts.
        [Test]
        [ReaderVerdict(LiveClassesReader)]
        public void Given_TwoDividerStylesOnOneElement_When_TheOrderTheyWereAddedInIsReversed_Then_TheStyleTheReSyncResolvesIsTheOneAddedLast()
        {
            // Arrange — one scan accumulates the axis, the style and the colour into separate slots, so the
            // width the case above pins says nothing about which style token the spec ends on. The axis
            // token is what makes the spec resolve at all: a lone style class is inert.
            var added = Carrying("divide-x-2", "divide-dashed", "divide-dotted");
            var reversed = Carrying("divide-x-2", "divide-dotted", "divide-dashed");

            // Act
            StyleDivideClass.TryExtract(LiveClasses(added), out var fromAdded);
            StyleDivideClass.TryExtract(LiveClasses(reversed), out var fromReversed);

            // Assert
            Assert.That((fromAdded.Style, fromReversed.Style),
                Is.EqualTo((BorderLineStyle.Dotted, BorderLineStyle.Dashed)),
                "the divider style this path configures the manipulator with is whichever divide style "
                + "token the class list hands over last");
        }

        // GREEN_ON_BASE(characterization): the base already resolves the last divide colour token.
        // What shows the case can fail is a first-wins guard on the colour arm of the same scan: measured,
        // each arrangement then takes the colour it was handed first and the pair inverts.
        [Test]
        [ReaderVerdict(LiveClassesReader)]
        public void Given_TwoDividerColoursOnOneElement_When_TheOrderTheyWereAddedInIsReversed_Then_TheColourTheReSyncResolvesIsTheOneAddedLast()
        {
            // Arrange — the third slot the same scan accumulates, and the one whose resolved value comes
            // from the palette rather than from the token, so the two palette entries are read here rather
            // than spelled as channel values.
            VelvetPalette.TryResolveColorToken("red-500", out var red);
            VelvetPalette.TryResolveColorToken("blue-500", out var blue);
            var added = Carrying("divide-x-2", "divide-red-500", "divide-blue-500");
            var reversed = Carrying("divide-x-2", "divide-blue-500", "divide-red-500");

            // Act
            StyleDivideClass.TryExtract(LiveClasses(added), out var fromAdded);
            StyleDivideClass.TryExtract(LiveClasses(reversed), out var fromReversed);

            // Assert — that the two palette entries differ rides along, since two equal colours would agree
            // here whatever decided between them.
            Assert.That((fromAdded.Color == blue, fromReversed.Color == red, red == blue),
                Is.EqualTo((true, true, false)),
                "the divider colour this path configures the manipulator with is whichever divide colour "
                + "token the class list hands over last");
        }

        // GREEN_ON_BASE(characterization): the base already answers this from the set, not the order.
        // What shows the case can fail is trading the `return true` in DeclaresWidthClass for an
        // assignment that keeps scanning: measured, the arrangement ending on w-auto then answers false.
        [Test]
        [ReaderVerdict(BalanceWidthReader)]
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
        [ReaderVerdict(TransitionSlotsReader)]
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
        [ReaderVerdict(BaseLayerSeedReader)]
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
            // for, beside one plain utility. Routing decides which of them a class list can hold at all,
            // which is what bounds the settle path's own verdict to the family the case below holds.
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

        // GREEN_ON_BASE(characterization): the base already composes the pair in live-class-list order.
        // What shows the case can fail is collecting the classes with `Insert(0, cls)` rather than
        // `Add(cls)` in the settle re-apply: measured, each arrangement then composes the two the other
        // way round and the pair inverts.
        [Test]
        [ReaderVerdict(MotionReapplyReader)]
        public void Given_TwoCustomFilterTokensRoutedBeforeTheirNamesWereRegistered_When_TheMotionSettleReappliesThem_Then_TheyComposeInTheOrderTheClassListHoldsThem()
        {
            // Arrange — whether a resolver owns a token is decided when the token is routed, and a
            // filter-[name:args] whose name is not registered yet is owned by nobody, so it lands on the
            // live class list. Registering the names afterwards makes the settle path's re-apply the FIRST
            // application of each, and a custom filter's first application is what fixes its compose slot.
            var added = Carrying("filter-[halo:1]", "filter-[speckle:2]");
            var reversed = Carrying("filter-[speckle:2]", "filter-[halo:1]");
            RegisterCustomFilter("halo");
            RegisterCustomFilter("speckle");

            // Act
            Settle(added);
            Settle(reversed);

            // Assert — the class list of one arrangement rides along, sorted so it pins no order of its
            // own: had the names been registered before the routing, both tokens would have resolved to
            // inline style at that point and the compose order would be the routing's rather than this
            // reading's.
            Assert.That((SortedClassList(added), ComposedCustomFilters(added), ComposedCustomFilters(reversed)),
                Is.EqualTo(("filter-[halo:1] filter-[speckle:2]", "halo speckle", "speckle halo")),
                "the settle path re-applies each token the live class list still names, and for a custom "
                + "filter applied there for the first time that list's order is the compose order");
        }

        // GREEN_ON_BASE(characterization): the bundled sheets declare none of these but the canary.
        // What shows the case can fail is adding a `gap-4` entry to StyleUtilityProperties.g.cs: measured,
        // the token then joins the canary on the answering side.
        [Test]
        public void Given_TheTokensAnOrderDecidedReadingResolvesFrom_When_LookedUpBesideAUtilityWithARule_Then_OnlyThatUtilityDeclaresAProperty()
        {
            // Arrange — a class the projection suppresses is restored by APPENDING it to the live class
            // list, which moves it past every class declared after it, and a class that declares no
            // property is never suppressed in the first place. One token per order-decided value: the clip
            // shape, the gap, the column count, the two grid gaps, the three divide slots and the custom
            // filter's compose slot. The bare grid marker is not among them because it does declare one;
            // what keeps the append clear of the count it feeds is the marker's own case above.
            var tokens = new[]
            {
                "clip-path-[circle(40%)]", "gap-4", "grid-cols-2", "gap-x-4", "gap-y-4",
                "divide-x-2", "divide-solid", "divide-red-500", "filter-[halo:1]", "p-4",
            };

            // Act
            var declaring = tokens.Where(cls =>
                StyleUtilityProperties.TryGet(cls, out var rule) && !rule.Properties.IsEmpty);

            // Assert — p-4 is the canary: a lookup that answered nothing would leave this empty and agree
            // with a table that had lost every entry.
            Assert.That(string.Join(" ", declaring), Is.EqualTo("p-4"),
                "p-4 apart, no token above declares a property of its own, so the projection can neither "
                + "suppress one nor re-append it past the reading that token decides");
        }

        // Return type, generic arity and the full parameter type names are all in this because the fold
        // they prevent is silent: without them `Distinct` collapses two distinct methods onto one spelling
        // — a generic overload onto its non-generic twin, two whose parameter types share a simple name,
        // two conversion operators differing only in what they return — and the second reader of such a
        // pair is left signed off by the first one's declaration.
        private static string Spelled(MethodReference method)
        {
            var arity = method is GenericInstanceMethod instance
                ? instance.GenericArguments.Count
                : method.GenericParameters.Count;
            return $"{method.ReturnType.FullName} {method.DeclaringType.FullName}.{method.Name}"
                + (arity == 0 ? string.Empty : "`" + arity)
                + $"({string.Join(", ", method.Parameters.Select(parameter => parameter.ParameterType.FullName))})";
        }

        // The graph is keyed by the IL spelling alone. Naming a case from its reflected form instead would
        // put a second spelling beside this one, and the two would have to agree on how every declaring and
        // parameter type is written for a case to be found in the graph at all.
        private static MethodDefinition InIl(TypeDefinition fixtureType, MethodInfo method)
            => fixtureType.Methods.Single(candidate => candidate.Name == method.Name);

        private static List<MethodDefinition> LiveClassListReaders(ModuleDefinition runtime)
            => runtime.GetTypes()
                .SelectMany(type => type.Methods)
                .Where(method => method.HasBody)
                .Where(method => method.Body.Instructions.Any(instruction =>
                    instruction.Operand is MethodReference reference
                    && reference.Name == nameof(VisualElement.GetClasses)
                    && reference.DeclaringType.FullName == typeof(VisualElement).FullName))
                .ToList();

        private static bool Writes(MethodDefinition method, string declaringType, string field)
            => method.Body.Instructions.Any(instruction =>
                instruction.OpCode == OpCodes.Stfld
                && instruction.Operand is FieldReference written
                && written.Name == field
                && written.DeclaringType.FullName == declaringType);

        // A call site names its callee by reference, and Spelled reads a reference the way it reads a
        // definition, so an edge lands without resolving anything across the two assemblies. A reflected
        // call is invisible to it, and the three private production methods the cases drive are all taken
        // that way, so every method of this type reading a handle field inherits an edge to what
        // ReflectionHandles resolved that field to.
        private static Dictionary<string, HashSet<string>> CallEdges(
            TypeDefinition fixtureType, params ModuleDefinition[] modules)
        {
            var methods = modules
                .SelectMany(module => module.GetTypes())
                .SelectMany(type => type.Methods)
                .ToList();

            var byTypeAndName = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            foreach (var method in methods)
            {
                var key = $"{method.DeclaringType.FullName}|{method.Name}";
                if (!byTypeAndName.TryGetValue(key, out var sameName))
                {
                    byTypeAndName[key] = sameName = new List<string>();
                }
                sameName.Add(Spelled(method));
            }

            var edges = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            foreach (var method in methods.Where(candidate => candidate.HasBody))
            {
                var callees = Callees(edges, Spelled(method));
                foreach (var instruction in method.Body.Instructions)
                {
                    if (instruction.Operand is MethodReference callee)
                    {
                        callees.Add(Spelled(callee));
                    }
                }
            }

            var handles = ReflectionHandles(fixtureType, byTypeAndName);
            foreach (var method in fixtureType.Methods.Where(candidate => candidate.HasBody))
            {
                foreach (var instruction in method.Body.Instructions)
                {
                    if (instruction.OpCode == OpCodes.Ldsfld
                        && instruction.Operand is FieldReference read
                        && handles.TryGetValue(read.FullName, out var targets))
                    {
                        Callees(edges, Spelled(method)).UnionWith(targets);
                    }
                }
            }

            return edges;
        }

        // The type token and the name literal that built a handle both survive in the static initializer,
        // which is enough to name the target without resolving the handle.
        private static Dictionary<string, List<string>> ReflectionHandles(
            TypeDefinition fixtureType, Dictionary<string, List<string>> byTypeAndName)
        {
            var handles = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            var initializer = fixtureType.Methods
                .FirstOrDefault(method => method.Name == ".cctor" && method.HasBody);
            if (initializer == null)
            {
                return handles;
            }

            TypeReference token = null;
            string literal = null;
            foreach (var instruction in initializer.Body.Instructions)
            {
                if (instruction.OpCode == OpCodes.Ldtoken && instruction.Operand is TypeReference declaring)
                {
                    token = declaring;
                }
                else if (instruction.OpCode == OpCodes.Ldstr)
                {
                    literal = (string)instruction.Operand;
                }
                else if (StoresAHandle(instruction, token, literal))
                {
                    var field = (FieldReference)instruction.Operand;
                    handles[field.FullName] = byTypeAndName.TryGetValue(
                        $"{token.FullName}|{literal}", out var targets) ? targets : new List<string>();
                }
            }
            return handles;
        }

        private static bool StoresAHandle(Instruction instruction, TypeReference token, string literal)
            => instruction.OpCode == OpCodes.Stsfld
                && instruction.Operand is FieldReference field
                && field.FieldType.FullName == typeof(MethodInfo).FullName
                && token != null
                && literal != null;

        private static HashSet<string> Callees(Dictionary<string, HashSet<string>> edges, string caller)
        {
            if (!edges.TryGetValue(caller, out var callees))
            {
                edges[caller] = callees = new HashSet<string>(StringComparer.Ordinal);
            }
            return callees;
        }

        private static bool Reaches(Dictionary<string, HashSet<string>> edges, string from, string target)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal) { from };
            var pending = new Queue<string>();
            pending.Enqueue(from);
            while (pending.Count > 0)
            {
                if (!edges.TryGetValue(pending.Dequeue(), out var callees))
                {
                    continue;
                }
                foreach (var callee in callees)
                {
                    if (callee == target)
                    {
                        return true;
                    }
                    if (seen.Add(callee))
                    {
                        pending.Enqueue(callee);
                    }
                }
            }
            return false;
        }

        private static IEnumerable<MethodInfo> CasesHere()
            => typeof(LiveClassListOrderTests)
                .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Where(method => method.IsDefined(typeof(TestAttribute), inherit: false));

        private static IEnumerable<(MethodInfo Case, string Reader)> VerdictsDeclaredHere()
            => CasesHere().SelectMany(method => method
                .GetCustomAttributes<ReaderVerdictAttribute>(inherit: false)
                .Select(attribute => (Case: method, Reader: attribute.Reader)));
    }
}
