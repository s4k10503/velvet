using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies the order of overlapping <c>rounded-*</c> utilities: the one Tailwind emits later holds a shared
    /// corner whichever is written first, for the shorthand against a side, a side against a corner, and the
    /// logical forms against the physical ones. A stylesheet class that takes part is written inline, and the
    /// sheet itself declares the classes in that order.
    /// </summary>
    [TestFixture]
    internal sealed class RadiusPrecedenceTests
    {
        // Top-left, top-right, bottom-right, bottom-left, the order a four-value border-radius lists them.
        private static string Corners(VisualElement element)
        {
            var style = element.style;
            return string.Join(",", (int)style.borderTopLeftRadius.value.value,
                (int)style.borderTopRightRadius.value.value, (int)style.borderBottomRightRadius.value.value,
                (int)style.borderBottomLeftRadius.value.value);
        }

        private static string CornersOf(string className)
        {
            using var reconciler = new Reconciler();
            var root = new VisualElement();
            reconciler.Reconcile(root, System.Array.Empty<VNode>(), new VNode[] { V.Div(className) });
            return Corners(root.ElementAt(0));
        }

        // The pairs below set the one Tailwind emits first to 2px and the one it emits later to 8px, so the corners
        // they share read 8 and a corner only the first sets reads 2. The compiled order is rounded, s, ss, e, se,
        // ee, es, t, l, tl, r, tr, b, br, bl.
        [TestCase("rounded-r-[8px] rounded-t-[2px]", "2,8,8,0")]
        [TestCase("rounded-t-[8px] rounded-[2px]", "8,8,2,2")]
        [TestCase("rounded-s-[8px] rounded-[2px]", "8,2,2,8")]
        [TestCase("rounded-tl-[8px] rounded-t-[2px]", "8,2,0,0")]
        [TestCase("rounded-tr-[8px] rounded-t-[2px]", "2,8,0,0")]
        [TestCase("rounded-b-[8px] rounded-l-[2px]", "2,0,8,8")]
        [TestCase("rounded-l-[8px] rounded-t-[2px]", "8,2,0,8")]
        [TestCase("rounded-tl-[8px] rounded-l-[2px]", "8,0,0,2")]
        [TestCase("rounded-br-[8px] rounded-r-[2px]", "0,2,8,0")]
        [TestCase("rounded-br-[8px] rounded-b-[2px]", "0,0,8,2")]
        [TestCase("rounded-t-[8px] rounded-s-[2px]", "8,8,0,2")]
        [TestCase("rounded-l-[8px] rounded-ss-[2px]", "8,0,0,8")]
        [TestCase("rounded-l-[8px] rounded-es-[2px]", "8,0,0,8")]
        [TestCase("rounded-r-[8px] rounded-se-[2px]", "0,8,8,0")]
        [TestCase("rounded-r-[8px] rounded-e-[2px]", "0,8,8,0")]
        [TestCase("rounded-br-[8px] rounded-ee-[2px]", "0,0,8,0")]
        [TestCase("rounded-bl-[8px] rounded-es-[2px]", "0,0,0,8")]
        [TestCase("rounded-ss-[8px] rounded-s-[2px]", "8,0,0,2")]
        [TestCase("rounded-se-[8px] rounded-e-[2px]", "0,8,2,0")]
        [TestCase("rounded-ss-[8px] rounded-[2px]", "8,2,2,2")]
        [TestCase("!rounded-r-[8px] !rounded-t-[2px]", "2,8,8,0")]
        public void Given_OverlappingInlineRadiiWithTheLaterInTailwindsOrderWrittenFirst_When_Reconciled_Then_ItHoldsTheSharedCorner(
            string className, string expected)
        {
            // Act
            var corners = CornersOf(className);

            // Assert
            Assert.That(corners, Is.EqualTo(expected));
        }

        // GREEN_ON_BASE(characterization): the base lets the radius written last hold the corner, and that is the one Tailwind emits later in each of these.
        // Letting the earlier in Tailwind's order win instead reddens every case.
        [TestCase("rounded-t-[2px] rounded-r-[8px]", "2,8,8,0")]
        [TestCase("rounded-[2px] rounded-t-[8px]", "8,8,2,2")]
        [TestCase("rounded-t-[2px] rounded-tl-[8px]", "8,2,0,0")]
        [TestCase("rounded-l-[2px] rounded-b-[8px]", "2,0,8,8")]
        [TestCase("rounded-s-[2px] rounded-t-[8px]", "8,8,0,2")]
        [TestCase("rounded-ss-[2px] rounded-l-[8px]", "8,0,0,8")]
        [TestCase("rounded-se-[2px] rounded-r-[8px]", "0,8,8,0")]
        [TestCase("rounded-s-[2px] rounded-ss-[8px]", "8,0,0,2")]
        [TestCase("rounded-[2px] rounded-ss-[8px]", "8,2,2,2")]
        public void Given_OverlappingInlineRadiiWithTheLaterInTailwindsOrderWrittenLast_When_Reconciled_Then_ItHoldsTheSharedCorner(
            string className, string expected)
        {
            // Act
            var corners = CornersOf(className);

            // Assert
            Assert.That(corners, Is.EqualTo(expected));
        }

        [Test]
        public void Given_TheRadiusUtilities_When_SortedByTheirCascadeRank_Then_TheyRunInTailwindsEmissionOrder()
        {
            // Arrange — the order Tailwind's compile() emits one bracket utility of each in.
            var emitted = new[]
            {
                "rounded-[1px]", "rounded-s-[1px]", "rounded-ss-[1px]", "rounded-e-[1px]", "rounded-se-[1px]",
                "rounded-ee-[1px]", "rounded-es-[1px]", "rounded-t-[1px]", "rounded-l-[1px]", "rounded-tl-[1px]",
                "rounded-r-[1px]", "rounded-tr-[1px]", "rounded-b-[1px]", "rounded-br-[1px]", "rounded-bl-[1px]",
            };

            // Act
            var sorted = emitted.Reverse().OrderBy(token =>
            {
                StyleArbitraryValueResolver.TryParseLayer(token, out var style);
                return StyleSpacingPrecedence.RankOf(style.Property);
            }).ToArray();

            // Assert
            Assert.That(sorted, Is.EqualTo(emitted));
        }

        [TestCase("rounded-t-lg rounded-[2px]", "rounded-t-[8px] rounded-[2px]")]
        [TestCase("rounded-[2px] rounded-t-lg", "rounded-[2px] rounded-t-[8px]")]
        [TestCase("rounded-t rounded-[2px]", "rounded-t-[4px] rounded-[2px]")]
        [TestCase("rounded-t-none rounded-[2px]", "rounded-t-[0px] rounded-[2px]")]
        [TestCase("rounded-t-full rounded-[2px]", "rounded-t-[9999px] rounded-[2px]")]
        [TestCase("rounded-tl-2xl rounded-[2px]", "rounded-tl-[16px] rounded-[2px]")]
        [TestCase("rounded-tl-lg rounded-t-[2px]", "rounded-tl-[8px] rounded-t-[2px]")]
        [TestCase("rounded-l-lg rounded-t-[2px]", "rounded-l-[8px] rounded-t-[2px]")]
        [TestCase("rounded-l-lg rounded-s-[2px]", "rounded-l-[8px] rounded-s-[2px]")]
        [TestCase("rounded-tl-lg rounded-s-[2px]", "rounded-tl-[8px] rounded-s-[2px]")]
        [TestCase("rounded-tl-lg rounded-ss-[2px]", "rounded-tl-[8px] rounded-ss-[2px]")]
        [TestCase("rounded-t-lg rounded-ss-[2px]", "rounded-t-[8px] rounded-ss-[2px]")]
        [TestCase("rounded-r-lg rounded-se-[2px]", "rounded-r-[8px] rounded-se-[2px]")]
        [TestCase("hover:rounded-t-lg hover:rounded-[2px]", "hover:rounded-t-[8px] hover:rounded-[2px]")]
        [TestCase("!rounded-t-lg !rounded-[2px]", "!rounded-t-[8px] !rounded-[2px]")]
        [TestCase("rounded-t-lg! rounded-[2px]!", "rounded-t-[8px]! rounded-[2px]!")]
        public void Given_AStylesheetRadiusClassBesideAnInlineRadiusItFollows_When_ClassNamesAreParsed_Then_TheClassIsWrittenInline(
            string className, string expected)
        {
            // Act
            var tokens = V.ParseClassNames(className);

            // Assert
            Assert.That(string.Join(" ", tokens), Is.EqualTo(expected));
        }

        // GREEN_ON_BASE(characterization): the base rewrites no class, so each list below reaches the reconciler as written.
        // Each is a pair where an inline value beating the class is already Tailwind's answer, the two do not
        // contend, or the class is no radius class, so a rewrite of every class beside an inline one reddens it.
        [TestCase("rounded-lg rounded-t-[2px]")]
        [TestCase("rounded-lg rounded-s-[2px]")]
        [TestCase("rounded-lg rounded-[2px]")]
        [TestCase("rounded-t-lg rounded-tl-[2px]")]
        [TestCase("rounded-t-lg rounded-b-[2px]")]
        [TestCase("rounded-t-lg rounded-bl-[2px]")]
        [TestCase("rounded-t-lg hover:rounded-[2px]")]
        [TestCase("rounded-t-lg !rounded-[2px]")]
        [TestCase("rounded-t-huge size-[52px]")]
        [TestCase("size-[52px] flex")]
        [TestCase("rounded-t-lg rounded-b-lg")]
        public void Given_ARadiusClassListThatNeedsNoRewrite_When_ClassNamesAreParsed_Then_TheyAreAsWritten(string className)
        {
            // Act
            var tokens = V.ParseClassNames(className);

            // Assert
            Assert.That(string.Join(" ", tokens), Is.EqualTo(className));
        }

        [TestCase("rounded-t-[4px] flex", "rounded-[8px]", "flex")]
        [TestCase("rounded-s-[4px]", "rounded-[8px]", "")]
        [TestCase("rounded-l-[4px]", "rounded-s-[8px]", "")]
        [TestCase("rounded-ss-[4px]", "rounded-l-[8px]", "")]
        public void Given_BaseAndPoseRadii_When_ThePoseWritesAllTheBaseDoes_Then_TheBaseRadiusGoes(
            string baseClasses, string pose, string expected)
        {
            // Act
            var kept = StyleSpacingPrecedence.WithoutOverriddenBase(
                V.ParseClassNames(baseClasses), V.ParseClassNames(pose));

            // Assert
            Assert.That(string.Join(" ", kept), Is.EqualTo(expected));
        }

        // GREEN_ON_BASE(characterization): the base filters no radius from a base list, so these stay as written.
        // A pose that leaves part of the base radius unwritten keeps it, which filtering on any overlap reddens.
        [TestCase("rounded-[8px]", "rounded-t-[4px]", "rounded-[8px]")]
        [TestCase("rounded-t-[4px]", "rounded-b-[8px]", "rounded-t-[4px]")]
        public void Given_BaseAndPoseRadii_When_ThePoseLeavesPartOfTheBaseUnwritten_Then_TheBaseRadiusStays(
            string baseClasses, string pose, string expected)
        {
            // Act
            var kept = StyleSpacingPrecedence.WithoutOverriddenBase(
                V.ParseClassNames(baseClasses), V.ParseClassNames(pose));

            // Assert
            Assert.That(string.Join(" ", kept), Is.EqualTo(expected));
        }

        [TestCase("rounded-[2px]", "rounded-t-lg", "rounded-t-[8px]")]
        [TestCase("rounded-s-[2px]", "rounded-l-lg", "rounded-l-[8px]")]
        public void Given_BaseAndPoseRadii_When_TheNarrowerPoseClassIsInlined_Then_ItIsWrittenInline(
            string baseClasses, string pose, string expected)
        {
            // Act
            var inlined = StyleSpacingPrecedence.InlineNarrowerPose(
                V.ParseClassNames(baseClasses), V.ParseClassNames(pose));

            // Assert
            Assert.That(string.Join(" ", inlined), Is.EqualTo(expected));
        }

        // GREEN_ON_BASE(characterization): the base inlines no pose class, so this one reaches the pose as written.
        // A pose class on corners the base radius does not write has nothing to lose to it, which inlining every pose class reddens.
        [Test]
        public void Given_APoseRadiusClassOnOtherCornersThanTheBase_When_TheNarrowerPoseClassIsInlined_Then_ItStaysAsWritten()
        {
            // Act
            var inlined = StyleSpacingPrecedence.InlineNarrowerPose(
                V.ParseClassNames("rounded-t-[2px]"), V.ParseClassNames("rounded-b-lg"));

            // Assert
            Assert.That(string.Join(" ", inlined), Is.EqualTo("rounded-b-lg"));
        }

        [TestCase("rounded-s-lg", ArbitraryProperty.BorderStartRadius, 8f)]
        [TestCase("rounded-e", ArbitraryProperty.BorderEndRadius, 4f)]
        [TestCase("rounded-ss-md", ArbitraryProperty.BorderStartStartRadius, 6f)]
        [TestCase("rounded-se-none", ArbitraryProperty.BorderStartEndRadius, 0f)]
        [TestCase("rounded-es-xl", ArbitraryProperty.BorderEndStartRadius, 12f)]
        [TestCase("rounded-ee-full", ArbitraryProperty.BorderEndEndRadius, 9999f)]
        [TestCase("rounded-s-[12px]", ArbitraryProperty.BorderStartRadius, 12f)]
        [TestCase("rounded-l-lg", ArbitraryProperty.BorderLeftRadius, 8f)]
        [TestCase("rounded-tl-[3px]", ArbitraryProperty.BorderTopLeftRadius, 3f)]
        [TestCase("ps-4", ArbitraryProperty.PaddingLeft, 16f)]
        public void Given_AToken_When_ParsedAsALayer_Then_OnlyALogicalRadiusTakesAPropertyOfItsOwn(
            string className, object property, float value)
        {
            // Act
            var ok = StyleArbitraryValueResolver.TryParseLayer(className, out var style);

            // Assert
            Assert.That((ok, style.Property, style.Value), Is.EqualTo((true, (ArbitraryProperty)property, value)));
        }

        [Test]
        public void Given_ATokenNoParserClaims_When_ParsedAsALayer_Then_NothingResolves()
        {
            // Act
            var ok = StyleArbitraryValueResolver.TryParseLayer("rounded-s-huge", out _);

            // Assert
            Assert.That(ok, Is.False);
        }

        [Test]
        public void Given_TheRoundedClassesOfEveryFamilyAndStep_When_TheirSheetPositionsAreComparedWithTailwindsOrder_Then_OverlappingOnesFollowIt()
        {
            // Arrange — a class that shares a corner with another and sets a different group of corners sits after
            // it in the sheet exactly when Tailwind emits it after; two classes of one group set the same corners,
            // so the scale does not order them.
            var families = new[] { "", "-t", "-l", "-tl", "-r", "-tr", "-b", "-br", "-bl" };
            var steps = new[] { "", "-none", "-sm", "-md", "-lg", "-xl", "-2xl", "-3xl", "-full" };
            var names = families.SelectMany(family => steps.Select(step => "rounded" + family + step)).ToArray();
            var problems = new List<string>();

            // Act
            foreach (var name in names.Where(name => !StyleUtilityProperties.TryGet(name, out _)))
            {
                problems.Add("no rule for " + name);
            }
            foreach (var first in names)
            {
                foreach (var second in names)
                {
                    if (!StyleUtilityProperties.TryGet(first, out var a) || !StyleUtilityProperties.TryGet(second, out var z)
                        || !a.Properties.Overlaps(z.Properties) || a.Properties == z.Properties)
                    {
                        continue;
                    }
                    if (StyleRuleOrder.CompareUtilities(first, second) < 0 && a.CascadePosition > z.CascadePosition)
                    {
                        problems.Add(first + " is declared after " + second);
                    }
                }
            }

            // Assert
            Assert.That(string.Join("; ", problems), Is.Empty);
        }
    }

    /// <summary>
    /// The same order read off a real panel with the bundled stylesheet attached, where the classes cascade
    /// against each other and against an inline radius.
    /// </summary>
    internal sealed class RadiusPrecedencePanelTests : PanelTestBase
    {
        protected override void LoadStyleSheets() => VelvetStyleUtilities.AttachTo(_window.rootVisualElement);

        private VisualElement Box => _window.rootVisualElement.Q<VisualElement>("box");

        private string Radii(string className)
        {
            _mounted = V.Mount(_window.rootVisualElement,
                V.Div(name: "box", className: "w-[200px] h-[200px] " + className));
            ForcePanelUpdate(Box.panel);
            var style = Box.resolvedStyle;
            return string.Join(",", (int)style.borderTopLeftRadius, (int)style.borderTopRightRadius,
                (int)style.borderBottomRightRadius, (int)style.borderBottomLeftRadius);
        }

        [TestCase("rounded-l-sm rounded-b-lg", "2,0,8,8")]
        [TestCase("rounded-b-lg rounded-l-sm", "2,0,8,8")]
        [TestCase("rounded-r rounded-t-lg", "8,4,4,0")]
        [TestCase("rounded-t-lg rounded-r", "8,4,4,0")]
        [TestCase("rounded-t-lg rounded-[2px]", "8,8,2,2")]
        [TestCase("rounded-[2px] rounded-t-lg", "8,8,2,2")]
        [TestCase("rounded-l-lg rounded-s-[2px]", "8,0,0,8")]
        [TestCase("rounded-s-[2px] rounded-l-lg", "8,0,0,8")]
        [TestCase("rounded-r-lg rounded-se-[2px]", "0,8,8,0")]
        public void Given_RadiusClassesAndInlineRadiiOnOnePanel_When_LaidOut_Then_TheLaterInTailwindsOrderHoldsTheCorner(
            string className, string expected)
        {
            // Act
            var radii = Radii(className);

            // Assert
            Assert.That(radii, Is.EqualTo(expected));
        }
    }
}
