using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Holds that a payload family written behind a variant reaches the same state as the same utility
    /// written bare, over the product of variant categories and payload families.
    /// <para>
    /// The failure this exists for is a family that works bare and is inert behind a variant, with no
    /// diagnostic. Eleven variant fixtures existed and none caught it, because each drives one variant
    /// against one payload rather than the product.
    /// </para>
    /// <para>
    /// The families are read out of the payload dispatcher rather than listed here, so one added tomorrow is
    /// in the matrix for existing. What cannot be derived is a utility that stands for a family — the
    /// dispatcher answers about a token, not about which token to write — so each is declared, and the
    /// derived and declared sets are held equal in both directions.
    /// </para>
    /// <para>
    /// The assertion compares the variant form against the BARE form rather than an expected value, so it
    /// states "a variant does not change what a utility does" and needs no per-family oracle. A family whose
    /// representative changes nothing observable would satisfy that vacuously, which the baseline term rules
    /// out.
    /// </para>
    /// </summary>
    [TestFixture]
    internal sealed class VariantReachesEveryFamilyTests : PanelTestBase
    {
        private const string DispatcherPath = "Packages/com.velvet.core/Runtime/Styling/StyleVariantPayload.cs";

        // Each family the payload dispatcher asks about, in the form it is written there. Deliberately not
        // anchored on the "Class" suffix: the inline-resolved arbitrary values are gated by a resolver whose
        // name does not carry it, and an anchor that excluded them would have kept the largest family out of
        // the matrix while reporting nothing missing.
        private static readonly Regex FamilyPattern =
            new(@"(?<family>Style\w+\.Is\w+)\(core\)", RegexOptions.Compiled);

        // A utility that stands for each family. Declared because the dispatcher answers "is this token
        // mine", not "write this one" — and stated per family so a reader can see what is being exercised.
        private static readonly Dictionary<string, string> Representatives = new(StringComparer.Ordinal)
        {
            ["StyleArbitraryValueResolver.IsInlineResolved"] = "w-[200px]",
            ["StyleClipPathClass.IsClipPathClass"] = "clip-path-[circle(40%)]",
            ["StyleFontClass.IsArbitraryFontClass"] = "font-[550]",
            ["StyleTextEffectClass.IsArbitraryLeadingClass"] = "leading-[3px]",
            ["StyleTextBalanceClass.IsWidthDeclaringToken"] = "w-40",
            ["StyleGapClass.IsGapToken"] = "gap-4",
            ["StyleGridClass.IsGridToken"] = "grid-cols-2",
            ["StyleDivideClass.IsDivideToken"] = "divide-y",
            ["StyleTextBalanceClass.IsTextBalanceToken"] = "text-balance",
            ["StyleSkewClass.IsSkewClass"] = "skew-x-6",
            ["StyleShadowClass.IsShadowClass"] = "shadow-lg",
            ["StyleGradientClass.IsGradientClass"] = "bg-gradient-to-r from-red-500 to-blue-500",
            ["StyleAnimateClass.IsAnimateClass"] = "animate-pulse",
            ["StyleBorderStyleClass.IsBorderStyleClass"] = "border-dashed",
            ["StyleRingClass.IsRingClass"] = "ring-2",
            ["StyleFontClass.IsFontToken"] = "italic",
            ["StyleTextEffectClass.IsTextEffectToken"] = "uppercase",
        };

        // A ratchet on the declaration, because the two-directional check alone is satisfied by deleting a
        // family from the dispatcher AND its representative here — one plausible cleanup edit that removes a
        // family's coverage with both tests green.
        private const int FamilyFloor = 17;

        /// <summary>One variant per category, with what opens its gate.</summary>
        /// <remarks>
        /// The categories are what bounds the matrix: a defect in the routing is a property of how a variant
        /// delivers its payload, not of which of the 23 kinds is spelled, so one per category catches the
        /// class at a cost that grows with the families rather than with the kinds. State and relational are
        /// separate entries although one pointer edge opens both, because they reach the dispatcher through
        /// different manipulators.
        /// </remarks>
        private static readonly (string Prefix, Action<VariantReachesEveryFamilyTests, VisualElement> Open)[]
            Variants =
            {
                ("dark:", (fixture, host) => VelvetTheme.IsDark = true),
                ("hover:", (fixture, host) => Hover(host)),
                ("group-hover:", (fixture, host) => Hover(host)),
                ("md:", (fixture, host) => fixture.ResolveAt(WidePanel, host)),
                ("dark:hover:", (fixture, host) =>
                {
                    VelvetTheme.IsDark = true;
                    Hover(host);
                }),
            };

        private const float NarrowPanel = 500f;
        private const float WidePanel = 1000f;

        // Below the md breakpoint at rest, so the responsive gate is shut until the opener widens the panel:
        // a gate already open at mount is not a gate this fixture drove.
        protected override Rect WindowSize => new Rect(0, 0, NarrowPanel, 600);

        private bool _darkBefore;
        private double _clock;

        // The bundled sheet, because the families realised from USS rather than from C# write nothing an
        // unstyled panel can show: without it their representatives read as inert and the matrix would
        // exercise only the half Velvet resolves itself.
        protected override void LoadStyleSheets() => VelvetStyleUtilities.AttachTo(_window.rootVisualElement);

        [SetUp]
        public override void SetUp()
        {
            base.SetUp();
            EditorPanelTestHelpers.SetPanelTimeFunction(_window.rootVisualElement.panel, () => _clock);
            _darkBefore = VelvetTheme.IsDark;
            VelvetTheme.IsDark = false;
        }

        [TearDown]
        public override void TearDown()
        {
            VelvetTheme.IsDark = _darkBefore;
            base.TearDown();
        }

        private static IReadOnlyList<string> Families() =>
            FamilyPattern.Matches(File.ReadAllText(Path.GetFullPath(DispatcherPath)))
                .Select(match => match.Groups["family"].Value)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToList();

        /// <remarks>
        /// Both directions, because the matrix below is driven by the DECLARED set. A one-way check that the
        /// dispatcher's families are all declared would let a deleted family delete its own coverage: it
        /// leaves the derived set, the matrix stops posing it, and the deletion — the exact defect this
        /// fixture exists for — passes. Dropping one now leaves a representative naming nothing.
        /// </remarks>
        [Test]
        public void Given_ThePayloadDispatcher_When_ItsFamiliesAreDerived_Then_TheyAreExactlyTheDeclaredOnes()
        {
            // Arrange
            var families = Families();

            // Act
            var undeclared = families.Where(family => !Representatives.ContainsKey(family));
            var orphaned = Representatives.Keys.Where(family => !families.Contains(family));

            // Assert — the ratchet rides along because an unread file declares nothing missing either.
            Assert.That((Representatives.Count >= FamilyFloor, string.Join("\n", undeclared.Concat(orphaned))),
                Is.EqualTo((true, string.Empty)),
                "the families the dispatcher gates and the families this fixture stands a utility for have "
                + "drifted apart, so the matrix below poses a different question than the code answers");
        }

        [Test]
        public void Given_EveryPayloadFamily_When_WrittenBehindEachVariantCategory_Then_ItReachesWhatTheBareFormReaches()
        {
            // Arrange
            var families = Representatives.Keys.OrderBy(family => family, StringComparer.Ordinal).ToList();

            // Act
            var divergent = new List<string>();
            var inert = new List<string>();
            foreach (var (prefix, open) in Variants)
            {
                // Every render runs the opener, the bare ones included, so the only difference between the
                // two sides is where the utility sits — not that one of them saw a theme flip, a pointer
                // edge or a resize the other did not.
                var none = Observe(string.Empty, open);
                foreach (var family in families)
                {
                    var utility = Representatives[family];
                    var bare = Observe(utility, open);
                    var gated = Observe(Gated(utility, prefix), open);

                    if (StripClasses(bare) == StripClasses(none))
                    {
                        inert.Add($"{prefix} {family}: '{utility}' changes nothing a bare render shows");
                    }
                    else if (bare != gated)
                    {
                        divergent.Add($"{prefix} {family}: '{utility}'\n  bare  {bare}\n  {prefix} {gated}");
                    }
                }
            }

            // Assert — inert and divergent are reported together because a representative that measures
            // nothing and a family that does not resolve are the same failure of this fixture to mean
            // anything, arrived at from opposite ends.
            Assert.That((Representatives.Count >= FamilyFloor, string.Join("\n", inert.Concat(divergent))),
                Is.EqualTo((true, string.Empty)),
                "a utility behind a variant does not reach what the same utility reaches written bare");
        }

        // Every token of a representative behind the gate, not just its head: a representative that needs
        // more than one utility to do anything (a gradient without its stops paints nothing) would otherwise
        // have its tail applied unconditionally and its head alone gated.
        private static string Gated(string utility, string prefix) =>
            string.Join(" ", utility.Split(' ').Select(token => prefix + token));

        // The class terms dropped, because the vacuity check needs a representative to move something a
        // reader would SEE, and for most families the utility's own class lands on the live list whether or
        // not the payload was resolved.
        private static string StripClasses(string fingerprint) =>
            string.Join(" ", fingerprint.Split(' ').Where(term => !term.Contains("/cls=", StringComparison.Ordinal)));

        // Every inline style a family could write, read off the interface rather than listed. A hand-listed
        // set is the per-family oracle this fixture exists to avoid: two representatives were reported inert
        // for writing a longhand the list happened not to name.
        private static readonly System.Reflection.PropertyInfo[] StyleProperties =
            typeof(IStyle).GetProperties().OrderBy(property => property.Name, StringComparer.Ordinal).ToArray();

        // The resolved side as well, since a USS-realised family writes no inline style at all — its whole
        // effect is what the cascade computes.
        private static readonly System.Reflection.PropertyInfo[] ResolvedProperties =
            typeof(IResolvedStyle).GetProperties().OrderBy(property => property.Name, StringComparer.Ordinal)
                .ToArray();

        /// <summary>What one render of <paramref name="className"/> shows, in terms no single family owns.</summary>
        /// <remarks>
        /// Compared whole rather than per family, so adding a family needs no oracle written for it.
        /// </remarks>
        private string Observe(string className, Action<VariantReachesEveryFamilyTests, VisualElement> open)
        {
            VelvetTheme.IsDark = false;
            _clock = 0.0;
            _window.position = new Rect(0, 0, NarrowPanel, 600);
            var host = new VisualElement();
            _window.rootVisualElement.Add(host);
            try
            {
                // Two carriers under a group, because a family's payload can require one shape or the other:
                // the spacing and paint families need an element with children, text-balance stands down on
                // anything that is not a text element, and a relational payload needs a group ancestor. All
                // three are rendered for every family so the choice stays out of the declarations.
                using var mounted = V.Mount(host, V.Div(className: "group", children: new VNode?[]
                {
                    V.Div(className: className,
                        children: new VNode?[] { V.Label(text: "a"), V.Label(text: "b") }),
                    V.Div(className: "w-[160px]", children: new VNode?[]
                    {
                        // Narrow enough that the sentence wraps, which is the only state text-balance has
                        // anything to do in.
                        V.Label(className: className, text: "the quick brown fox jumps over the lazy dog"),
                    }),
                }));

                // Laid out on both sides of the gate, because a family whose payload is applied from a
                // geometry callback bakes nothing at NaN size: at no size, its at-rest form and its variant
                // form differ for want of a layout pass rather than for want of the variant reaching it.
                ForcePanelUpdate(host.panel);
                // Opened AFTER the mount, so what the matrix exercises is the re-sync a live gate change
                // drives rather than the create-time pass, which the reconciled array already covers.
                open(this, host);
                ForcePanelUpdate(host.panel);

                var registries = Registries(mounted);

                _clock = 1.0;
                Tick(host.panel);
                var first = Fingerprint(host, registries);

                // A second reading after real time has moved, because the animate driver derives its phase
                // from the wall clock rather than the panel's: freezing the panel clock does not align the
                // two sides, and the gated render starts its loop later than the bare one by however long
                // the fixture took to get there. Comparing samples would then report a phase difference as a
                // variant failure. Whatever moved between the two readings is time-driven, and the
                // fingerprint keeps that fact in place of the value — which is the term that discriminates
                // anyway, since a family the variant never reached writes nothing at all.
                System.Threading.Thread.Sleep(DrivenSampleGapMs);
                _clock = 2.0;
                Tick(host.panel);
                var second = Fingerprint(host, registries);

                return string.Join(" ", first.OrderBy(entry => entry.Key, StringComparer.Ordinal).Select(entry =>
                    entry.Key + "="
                    + (second.TryGetValue(entry.Key, out var later) && later == entry.Value
                        ? entry.Value
                        : "<driven>")));
            }
            finally
            {
                host.RemoveFromHierarchy();
            }
        }

        // Long enough that a wall-clock-driven property lands on a different float, short enough that the
        // matrix pays it once per render without being felt.
        private const int DrivenSampleGapMs = 40;

        // Fired on every element rather than on one and left to bubble, because the state and relational
        // signals read the edge on different elements — the payload's own carrier and its group ancestor.
        private static void Hover(VisualElement root)
        {
            // An element nothing ever registered a callback on has no handler for this edge, and the
            // simulator refuses rather than no-opping — so skipping it is the same delivery, not a gap.
            if (CallbackRegistry.GetValue(root) != null)
            {
                using var evt = PointerOverEvent.GetPooled();
                root.SimulateEvent(evt);
            }
            for (var index = 0; index < root.childCount; index++)
            {
                Hover(root[index]);
            }
        }

        private static readonly System.Reflection.FieldInfo CallbackRegistry =
            typeof(CallbackEventHandler).GetField("m_CallbackRegistry",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;

        // Sets the panel width, lays out, then fires the geometry event the responsive manipulator re-reads
        // its width source from.
        private void ResolveAt(float width, VisualElement host)
        {
            _window.position = new Rect(0, 0, width, 600);
            ForcePanelUpdate(host.panel);
            using var evt = EventBase<GeometryChangedEvent>.GetPooled();
            host.panel.visualTree.SimulateEvent(evt);
        }

        private static void Tick(IPanel panel)
        {
            EditorPanelTestHelpers.DriveSchedulerOnce(panel);
            EditorPanelTestHelpers.DriveAnimationsOnce(panel);
        }

        private static Dictionary<string, string> Fingerprint(
            VisualElement root, IReadOnlyDictionary<VisualElement, string> registries)
        {
            var reading = new Dictionary<string, string>(StringComparer.Ordinal);
            Read(root, "0", reading, registries);
            return reading;
        }

        /// <summary>Which of the reconciler's per-element registries hold each element.</summary>
        /// <remarks>
        /// A family whose whole effect at this layer is that a manipulator got attached shows nothing in a
        /// reading of styles: text-balance moved not one style here, and reported inert, while the variant
        /// path was in fact reaching it. Membership is generic — one term derived from whatever registries
        /// the context declares — so a family added later is covered without a term written for it.
        /// </remarks>
        private static Dictionary<VisualElement, string> Registries(MountedTree mounted)
        {
            var context = mounted.GetType().GetField("Root", Instance)!.GetValue(mounted);
            context = context!.GetType().GetProperty("Reconciler", Instance)!.GetValue(context);
            context = context!.GetType().GetProperty("Context", Instance)!.GetValue(context);

            var byElement = new Dictionary<VisualElement, List<string>>();
            foreach (var property in context!.GetType().GetProperties()
                         .Where(property => typeof(System.Collections.IDictionary)
                             .IsAssignableFrom(property.PropertyType))
                         // The variant machinery's own bookkeeping, which a gated render enters by being
                         // gated at all and a bare one never does. It is the premise of the comparison
                         // rather than a payload's effect. Substring rather than a list, which costs a
                         // family registry its term should one ever be named for a variant.
                         .Where(property => !property.Name.Contains("Variant", StringComparison.Ordinal))
                         .OrderBy(property => property.Name, StringComparer.Ordinal))
            {
                if (property.GetValue(context) is not System.Collections.IDictionary registry)
                {
                    continue;
                }
                foreach (var key in registry.Keys)
                {
                    if (key is not VisualElement element)
                    {
                        continue;
                    }
                    if (!byElement.TryGetValue(element, out var names))
                    {
                        byElement[element] = names = new List<string>();
                    }
                    names.Add(property.Name);
                }
            }
            return byElement.ToDictionary(entry => entry.Key, entry => string.Join(",", entry.Value));
        }

        private const System.Reflection.BindingFlags Instance =
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public
            | System.Reflection.BindingFlags.NonPublic;

        // The whole mounted subtree from the container down, because half the families write somewhere other
        // than the element carrying the class: gap writes the CHILDREN's margins and ring places an overlay
        // BESIDE it, and a reading of that element alone reported both as reached while their gate was
        // deleted.
        private static void Read(VisualElement element, string path, Dictionary<string, string> reading,
            IReadOnlyDictionary<VisualElement, string> registries)
        {
            reading[path + "/reg"] = registries.TryGetValue(element, out var names) ? names : string.Empty;
            reading[path + "/cls"] =
                string.Join(",", element.GetClasses().OrderBy(name => name, StringComparer.Ordinal));
            reading[path + "/kids"] = element.childCount.ToString();
            // Spaces out, because the fingerprint is a space-joined list of terms and a wrapped sentence
            // would split into several of them.
            reading[path + "/text"] = ((element as TextElement)?.text ?? string.Empty).Replace(' ', '_');
            var style = element.style;
            foreach (var property in StyleProperties)
            {
                reading[path + "/" + property.Name] = property.GetValue(style)?.ToString() ?? string.Empty;
            }
            var resolved = element.resolvedStyle;
            foreach (var property in ResolvedProperties)
            {
                reading[path + "/~" + property.Name] = property.GetValue(resolved)?.ToString() ?? string.Empty;
            }
            for (var index = 0; index < element.childCount; index++)
            {
                Read(element[index], path + "." + index, reading, registries);
            }
        }
    }
}
