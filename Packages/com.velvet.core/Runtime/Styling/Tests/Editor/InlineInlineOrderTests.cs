using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies two base utilities that both resolve inline and write one property: the one Tailwind emits later
    /// holds it whichever is written first, the earlier is left out of the class list, and taking either away leaves
    /// the other in effect. An inline border width that later inline tokens write in part is cut down to the sides
    /// they leave. A stylesheet class written inline beside a narrower inline token is ordered by the name it was
    /// written with, not by the inline form it became. A variant token and a token of another property are left as
    /// written.
    /// </summary>
    [TestFixture]
    internal sealed class InlineInlineOrderTests
    {
        private readonly List<(string Name, FilterFunctionDefinition Definition)> _registered = new();

        [TearDown]
        public void TearDown()
        {
            foreach (var (name, definition) in _registered)
            {
                VelvetFilters.Unregister(name);
                UnityEngine.Object.DestroyImmediate(definition);
            }
            _registered.Clear();
        }

        private static VisualElement MountOne(Reconciler reconciler, VisualElement root, string className)
        {
            reconciler.Reconcile(root, Array.Empty<VNode>(), new VNode[] { V.Div(className) });
            return root.ElementAt(0);
        }

        private static VisualElement Replace(Reconciler reconciler, VisualElement root, string oldClassName, string newClassName)
        {
            var oldTree = new VNode[] { V.Div(oldClassName) };
            var newTree = new VNode[] { V.Div(newClassName) };
            reconciler.Reconcile(root, Array.Empty<VNode>(), oldTree);
            var element = root.ElementAt(0);
            reconciler.Reconcile(root, oldTree, newTree);
            return element;
        }

        // Same registration as LiveClassListOrderTests, which this copy leaves for its own fixture: the definition
        // here only has to make the name resolve, so it declares one parameter and nothing else.
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
            _registered.Add((name, definition));
            VelvetFilters.Register(name, definition);
        }

        // One or more rows for sizing, spacing, inset, radius, a colour with an opacity, border width and colour per
        // side, the transforms, opacity, font size, tracking, duration, aspect, a filter and the important layer. Tailwind
        // emits the later of two tokens of one property last, and that one is left whichever is written first.
        [TestCase("w-[1px] w-[2px]", "w-[2px]")]
        [TestCase("w-[2px] w-[1px]", "w-[2px]")]
        [TestCase("w-[3px] w-[1px] w-[2px]", "w-[3px]")]
        [TestCase("w-[1px] flex w-[2px]", "flex w-[2px]")]
        [TestCase("w-1/2 w-[10px]", "w-[10px]")]
        [TestCase("w-[10px] w-1/2", "w-[10px]")]
        [TestCase("h-[10%] h-[20%]", "h-[20%]")]
        [TestCase("min-w-[1px] min-w-[2px]", "min-w-[2px]")]
        [TestCase("max-h-[2px] max-h-[1px]", "max-h-[2px]")]
        [TestCase("size-[1px] size-[2px]", "size-[2px]")]
        [TestCase("basis-[1px] basis-[2px]", "basis-[2px]")]
        [TestCase("p-[1px] p-[2px]", "p-[2px]")]
        [TestCase("px-[2px] px-[1px]", "px-[2px]")]
        [TestCase("pt-[1px] pt-[2px]", "pt-[2px]")]
        [TestCase("m-[1px] m-[2px]", "m-[2px]")]
        [TestCase("mt-[1px] -mt-[2px]", "mt-[1px]")]
        [TestCase("-mt-[1px] -mt-[2px]", "-mt-[2px]")]
        [TestCase("ps-[1px] ps-[2px]", "ps-[2px]")]
        [TestCase("top-[1px] top-[2px]", "top-[2px]")]
        [TestCase("inset-x-[2px] inset-x-[1px]", "inset-x-[2px]")]
        [TestCase("rounded-[1px] rounded-[2px]", "rounded-[2px]")]
        [TestCase("rounded-tl-[2px] rounded-tl-[1px]", "rounded-tl-[2px]")]
        [TestCase("bg-[#ff0000] bg-red-500/50", "bg-red-500/50")]
        [TestCase("bg-red-500/50 bg-[#ff0000]", "bg-red-500/50")]
        [TestCase("bg-red-500/50 bg-red-500/75", "bg-red-500/75")]
        [TestCase("text-[#ff0000] text-red-500/50", "text-red-500/50")]
        [TestCase("border-[#ff0000] border-red-500/50", "border-red-500/50")]
        [TestCase("border-b-red-500 border-b-[#0000ff]", "border-b-red-500")]
        [TestCase("border-b-[#0000ff] border-b-red-500", "border-b-red-500")]
        [TestCase("border-x-red-500 border-x-[#0000ff]", "border-x-red-500")]
        [TestCase("border-s-red-500 border-s-[#0000ff]", "border-s-red-500")]
        [TestCase("border-t-[1px] border-t-[2px]", "border-t-[2px]")]
        [TestCase("border-[1px] border-[2px]", "border-[2px]")]
        [TestCase("border-s-[1px] border-s-[2px]", "border-s-[2px]")]
        [TestCase("scale-[1.5] scale-[2]", "scale-[2]")]
        [TestCase("scale-x-[2] scale-x-[1.5]", "scale-x-[2]")]
        [TestCase("translate-x-[4px] translate-x-[8px]", "translate-x-[8px]")]
        [TestCase("translate-y-[8px] translate-y-[4px]", "translate-y-[8px]")]
        [TestCase("rotate-[10deg] rotate-[20deg]", "rotate-[20deg]")]
        [TestCase("opacity-[.3] opacity-[.7]", "opacity-[.7]")]
        [TestCase("text-[12px] text-[14px]", "text-[14px]")]
        [TestCase("tracking-[1px] tracking-[2px]", "tracking-[2px]")]
        [TestCase("duration-[300ms] duration-[500ms]", "duration-[500ms]")]
        [TestCase("aspect-[4/3] aspect-[16/9]", "aspect-[16/9]")]
        [TestCase("blur-[2px] blur-[4px]", "blur-[4px]")]
        [TestCase("grow-[2] grow-[3]", "grow-[3]")]
        [TestCase("!w-[1px] !w-[2px]", "!w-[2px]")]
        [TestCase("w-[2px]! w-[1px]!", "w-[2px]!")]
        [TestCase("!bg-[#ff0000] !bg-red-500/50", "!bg-red-500/50")]
        public void Given_TwoInlineTokensOfOneProperty_When_ClassNamesAreParsed_Then_OnlyTheOneTailwindEmitsLastIsKept(
            string className, string expected)
        {
            // Act
            var tokens = V.ParseClassNames(className);

            // Assert
            Assert.That(string.Join(" ", tokens), Is.EqualTo(expected));
        }

        // GREEN_ON_BASE(characterization): the base leaves every token of these lists as written.
        // Each is a pair that does not contend for a property (another property, another layer, a variant, an
        // important class that stays a class, a slash size whose line height is read off the token, an identical
        // token, a shorthand beside its longhand that another pass orders), so leaving an inline token out for a
        // later one of another property reddens it.
        [TestCase("w-[1px] h-[2px]")]
        [TestCase("scale-[1.5] scale-x-[2]")]
        [TestCase("scale-x-[2] scale-y-[3]")]
        [TestCase("translate-x-[4px] translate-y-[8px]")]
        [TestCase("bg-[#ff0000] text-[#00ff00]")]
        [TestCase("text-[14px] text-[#ffffff]")]
        [TestCase("!w-[1px] w-[2px]")]
        [TestCase("w-[2px] !w-[1px]")]
        [TestCase("!w-[10px] !w-px")]
        [TestCase("!w-px !w-[10px]")]
        [TestCase("md:w-[1px] w-[2px]")]
        [TestCase("hover:w-[1px] hover:w-[2px]")]
        [TestCase("blur-[2px] brightness-[1.5]")]
        [TestCase("border-[3px] border-[#ff0000]")]
        [TestCase("border-[3px] bg-red-500")]
        [TestCase("text-[14px]/6 text-[16px]")]
        [TestCase("text-[16px] text-[14px]/6")]
        [TestCase("text-[14px] text-[16px]/6")]
        [TestCase("w-[1px] w-[1px]")]
        [TestCase("p-[1px] pt-[2px]")]
        [TestCase("pt-[2px] p-[1px]")]
        [TestCase("size-[1px] w-[2px]")]
        [TestCase("!border-[3px] border-t-[4px]")]
        public void Given_TwoInlineTokensThatDoNotContendForALonghand_When_ClassNamesAreParsed_Then_TheyAreAsWritten(
            string className)
        {
            // Act
            var tokens = V.ParseClassNames(className);

            // Assert
            Assert.That(string.Join(" ", tokens), Is.EqualTo(className));
        }

        // A bracket border width is cut down to the sides the later inline tokens leave, as a class cuts it
        // (InlineSheetOrderTests), and the cut tokens keep the important modifier as it was spelled.
        [TestCase("border-[3px] border-t-[4px]", "border-r-[3px] border-b-[3px] border-l-[3px] border-t-[4px]")]
        [TestCase("border-t-[4px] border-[3px]", "border-t-[4px] border-r-[3px] border-b-[3px] border-l-[3px]")]
        [TestCase("border-[3px] border-s-[2px]", "border-t-[3px] border-r-[3px] border-b-[3px] border-s-[2px]")]
        [TestCase("border-[3px] border-t-[4px] border-t-[5px]", "border-r-[3px] border-b-[3px] border-l-[3px] border-t-[5px]")]
        [TestCase("border-[3px] border-t-[4px] border-l-4", "border-r-[3px] border-b-[3px] border-t-[4px] border-l-4")]
        [TestCase("border-[3px] border-t-[4px] border-b-[5px] border-l-[6px] border-r-[7px]",
            "border-t-[4px] border-b-[5px] border-l-[6px] border-r-[7px]")]
        [TestCase("!border-[3px] !border-t-[4px]", "!border-r-[3px] !border-b-[3px] !border-l-[3px] !border-t-[4px]")]
        [TestCase("border-[3px]! border-t-[4px]!", "border-r-[3px]! border-b-[3px]! border-l-[3px]! border-t-[4px]!")]
        public void Given_AnInlineBorderWidthTheLaterInlineTokensWriteInPart_When_ClassNamesAreParsed_Then_ItIsCutDownToTheSidesTheyLeave(
            string className, string expected)
        {
            // Act
            var tokens = V.ParseClassNames(className);

            // Assert
            Assert.That(string.Join(" ", tokens), Is.EqualTo(expected));
        }

        // A class that is written inline beside a narrower inline token is compared with its neighbours by the name it
        // was written with: w-[10px] follows w-4 and precedes w-[16px], so w-[10px] beside w-4 stays whichever way
        // the inline form would sort.
        [TestCase("size-[10px] w-[20px] w-4", "size-[10px] w-[20px]")]
        [TestCase("size-[10px] w-4 w-[20px]", "size-[10px] w-[20px]")]
        [TestCase("size-[10px] w-[10px] w-4", "size-[10px] w-[10px]")]
        [TestCase("size-[10px] w-4 w-[10px]", "size-[10px] w-[10px]")]
        [TestCase("pt-2 pt-4 p-[3px]", "pt-[16px] p-[3px]")]
        [TestCase("pt-4 pt-2 p-[3px]", "pt-[16px] p-[3px]")]
        [TestCase("pt-[10px] pt-4 p-[3px]", "pt-[10px] p-[3px]")]
        [TestCase("pt-4 pt-[10px] p-[3px]", "pt-[10px] p-[3px]")]
        [TestCase("hover:pt-2 hover:pt-4 hover:p-[3px]", "hover:pt-[16px] hover:p-[3px]")]
        [TestCase("!pt-2 !pt-4 !p-[3px]", "!pt-[16px] !p-[3px]")]
        public void Given_TwoWritersOfOneSlotOnceAClassIsWrittenInline_When_ClassNamesAreParsed_Then_TheOneTailwindEmitsLastIsKept(
            string className, string expected)
        {
            // Act
            var tokens = V.ParseClassNames(className);

            // Assert
            Assert.That(string.Join(" ", tokens), Is.EqualTo(expected));
        }

        // GREEN_ON_BASE(characterization): the base leaves both tokens of the list as written.
        // Ordering two custom filters as one property, whatever their names, reddens it.
        [Test]
        public void Given_TwoCustomFiltersOfDifferentNames_When_ClassNamesAreParsed_Then_BothAreKept()
        {
            // Arrange
            RegisterCustomFilter("orderone");
            RegisterCustomFilter("ordertwo");

            // Act
            var tokens = V.ParseClassNames("filter-[orderone:1] filter-[ordertwo:2]");

            // Assert
            Assert.That(string.Join(" ", tokens), Is.EqualTo("filter-[orderone:1] filter-[ordertwo:2]"));
        }

        [Test]
        public void Given_TwoValuesOfOneCustomFilter_When_ClassNamesAreParsed_Then_TheOneTailwindEmitsLastIsKept()
        {
            // Arrange
            RegisterCustomFilter("orderone");

            // Act
            var tokens = V.ParseClassNames("filter-[orderone:2] filter-[orderone:1]");

            // Assert
            Assert.That(string.Join(" ", tokens), Is.EqualTo("filter-[orderone:2]"));
        }

        [TestCase("w-[2px] w-[1px]", 2f)]
        [TestCase("w-[3px] w-[1px] w-[2px]", 3f)]
        [TestCase("w-[10px] w-1/2", 10f)]
        [TestCase("!w-[2px] !w-[1px]", 2f)]
        public void Given_TwoInlineWidthsWrittenWithTheLaterInTailwindOrderFirst_When_Reconciled_Then_ThatOneHoldsTheWidth(
            string className, float expected)
        {
            // Arrange
            using var reconciler = new Reconciler();
            var root = new VisualElement();

            // Act
            var element = MountOne(reconciler, root, className);

            // Assert
            Assert.That(element.style.width.value.value, Is.EqualTo(expected));
        }

        // GREEN_ON_BASE(characterization): the base lets the token written last hold the width, which here is the one Tailwind emits last.
        [TestCase("w-[1px] w-[2px]", 2f)]
        [TestCase("w-1/2 w-[10px]", 10f)]
        [TestCase("!w-[1px] !w-[2px]", 2f)]
        public void Given_TwoInlineWidthsWrittenWithTheLaterInTailwindOrderLast_When_Reconciled_Then_ThatOneHoldsTheWidth(
            string className, float expected)
        {
            // Arrange
            using var reconciler = new Reconciler();
            var root = new VisualElement();

            // Act
            var element = MountOne(reconciler, root, className);

            // Assert
            Assert.That(element.style.width.value.value, Is.EqualTo(expected));
        }

        [TestCase("bg-red-500/50 bg-[#ff0000]", 0.5f)]
        [TestCase("bg-red-500/75 bg-red-500/50", 0.75f)]
        public void Given_TwoInlineBackgroundsWrittenWithTheLaterInTailwindOrderFirst_When_Reconciled_Then_ThatOneHoldsTheColour(
            string className, float alpha)
        {
            // Arrange
            using var reconciler = new Reconciler();
            var root = new VisualElement();

            // Act
            var element = MountOne(reconciler, root, className);

            // Assert
            Assert.That(element.style.backgroundColor.value.a, Is.EqualTo(alpha).Within(0.01f));
        }

        // GREEN_ON_BASE(characterization): the base lets the token written last hold the colour, which here is the one Tailwind emits last.
        [Test]
        public void Given_TwoInlineBackgroundsWrittenWithTheLaterInTailwindOrderLast_When_Reconciled_Then_ThatOneHoldsTheColour()
        {
            // Arrange
            using var reconciler = new Reconciler();
            var root = new VisualElement();

            // Act
            var element = MountOne(reconciler, root, "bg-[#ff0000] bg-red-500/50");

            // Assert
            Assert.That(element.style.backgroundColor.value.a, Is.EqualTo(0.5f).Within(0.01f));
        }

        [Test]
        public void Given_TwoInlineBottomBorderColoursWrittenWithTheLaterInTailwindOrderFirst_When_Reconciled_Then_ThatOneHoldsTheColour()
        {
            // Arrange
            using var reconciler = new Reconciler();
            var root = new VisualElement();

            // Act
            var element = MountOne(reconciler, root, "border-b-red-500/50 border-b-[#ff0000]");

            // Assert
            Assert.That(element.style.borderBottomColor.value.a, Is.EqualTo(0.5f).Within(0.01f));
        }

        [Test]
        public void Given_TheEarlierOfThreeInlineWidthsRemoved_When_Reconciled_Then_TheLaterInTailwindOrderStillHoldsTheWidth()
        {
            // Arrange
            using var reconciler = new Reconciler();
            var root = new VisualElement();

            // Act
            var element = Replace(reconciler, root, "w-[3px] w-[1px] w-[2px]", "w-[3px] w-[1px]");

            // Assert
            Assert.That(element.style.width.value.value, Is.EqualTo(3f));
        }

        // GREEN_ON_BASE(characterization): the base re-applies the surviving tokens after an inline token is removed.
        [TestCase("w-[1px] w-[2px]", "w-[1px]", 1f)]
        [TestCase("w-[1px] w-[2px]", "w-[2px]", 2f)]
        [TestCase("w-[2px] w-[1px]", "w-[1px]", 1f)]
        public void Given_OneOfTwoInlineWidthsRemoved_When_Reconciled_Then_TheOtherHoldsTheWidth(
            string oldClassName, string newClassName, float expected)
        {
            // Arrange
            using var reconciler = new Reconciler();
            var root = new VisualElement();

            // Act
            var element = Replace(reconciler, root, oldClassName, newClassName);

            // Assert
            Assert.That(element.style.width.value.value, Is.EqualTo(expected));
        }

        [Test]
        public void Given_TheEarlierOfThreeInlineBackgroundsRemoved_When_Reconciled_Then_TheLaterInTailwindOrderStillHoldsTheColour()
        {
            // Arrange
            using var reconciler = new Reconciler();
            var root = new VisualElement();

            // Act
            var element = Replace(reconciler, root, "bg-red-500/75 bg-[#ff0000] bg-red-500/50",
                "bg-red-500/75 bg-[#ff0000]");

            // Assert
            Assert.That(element.style.backgroundColor.value.a, Is.EqualTo(0.75f).Within(0.01f));
        }

        // GREEN_ON_BASE(characterization): the base re-applies the surviving tokens after an inline token is removed.
        [TestCase("bg-[#ff0000] bg-red-500/50", "bg-[#ff0000]", 1f)]
        [TestCase("bg-[#ff0000] bg-red-500/50", "bg-red-500/50", 0.5f)]
        public void Given_OneOfTwoInlineBackgroundsRemoved_When_Reconciled_Then_TheOtherHoldsTheColour(
            string oldClassName, string newClassName, float alpha)
        {
            // Arrange
            using var reconciler = new Reconciler();
            var root = new VisualElement();

            // Act
            var element = Replace(reconciler, root, oldClassName, newClassName);

            // Assert
            Assert.That(element.style.backgroundColor.value.a, Is.EqualTo(alpha).Within(0.01f));
        }

        [TestCase("border-t-[4px] border-[3px]")]
        [TestCase("!border-t-[4px] !border-[3px]")]
        public void Given_AnInlineBorderWidthWrittenAfterTheSideItDoesNotHold_When_Reconciled_Then_TheSideKeepsItsOwnWidth(
            string className)
        {
            // Arrange
            using var reconciler = new Reconciler();
            var root = new VisualElement();

            // Act
            var element = MountOne(reconciler, root, className);

            // Assert
            var style = element.style;
            Assert.That(
                (style.borderTopWidth.value, style.borderRightWidth.value, style.borderBottomWidth.value,
                    style.borderLeftWidth.value),
                Is.EqualTo((4f, 3f, 3f, 3f)));
        }

        // GREEN_ON_BASE(characterization): the base lets the side written last hold its width, which here is the one Tailwind emits last.
        [Test]
        public void Given_AnInlineBorderWidthWrittenBeforeTheSideItDoesNotHold_When_Reconciled_Then_TheSideKeepsItsOwnWidth()
        {
            // Arrange
            using var reconciler = new Reconciler();
            var root = new VisualElement();

            // Act
            var element = MountOne(reconciler, root, "border-[3px] border-t-[4px]");

            // Assert
            var style = element.style;
            Assert.That(
                (style.borderTopWidth.value, style.borderRightWidth.value, style.borderBottomWidth.value,
                    style.borderLeftWidth.value),
                Is.EqualTo((4f, 3f, 3f, 3f)));
        }

        [Test]
        public void Given_AClassWrittenInlineBesideAnInlineWidthOfTheSameProperty_When_Reconciled_Then_TheOneTailwindEmitsLastHoldsTheWidth()
        {
            // Arrange
            using var reconciler = new Reconciler();
            var root = new VisualElement();

            // Act
            var element = MountOne(reconciler, root, "size-[10px] w-[10px] w-4");

            // Assert
            Assert.That((element.style.width.value.value, element.style.height.value.value), Is.EqualTo((10f, 10f)));
        }

        [Test]
        public void Given_AClassWrittenInlineBesideAnInlinePaddingOfTheSameSide_When_Reconciled_Then_TheOneTailwindEmitsLastHoldsTheSide()
        {
            // Arrange
            using var reconciler = new Reconciler();
            var root = new VisualElement();

            // Act
            var element = MountOne(reconciler, root, "pt-[10px] pt-4 p-[3px]");

            // Assert
            Assert.That((element.style.paddingTop.value.value, element.style.paddingRight.value.value),
                Is.EqualTo((10f, 3f)));
        }

        // GREEN_ON_BASE(characterization): the base lets the token written last hold the width, which here is the one Tailwind emits last.
        [Test]
        public void Given_AClassWrittenInlineBeforeAnInlineWidthOfTheSameProperty_When_Reconciled_Then_TheInlineWidthHoldsIt()
        {
            // Arrange
            using var reconciler = new Reconciler();
            var root = new VisualElement();

            // Act
            var element = MountOne(reconciler, root, "size-[10px] w-4 w-[10px]");

            // Assert
            Assert.That((element.style.width.value.value, element.style.height.value.value), Is.EqualTo((10f, 10f)));
        }
    }
}
