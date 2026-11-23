using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins CSS's transition-behavior on the bundled transition utilities: <c>transition-all</c>'s list, and the
    /// <c>transition-discrete</c> and <c>transition-normal</c> rules of <c>_preflight.uss</c>.
    /// </summary>
    internal sealed class TransitionDiscretePanelTests : PanelTestBase
    {
        private const BindingFlags Internal = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static
            | BindingFlags.Instance;

        // CSS transitions these under transition-behavior: normal: visibility keeps its visible end for the whole
        // transition, and background-position interpolates.
        private static readonly HashSet<string> s_cssTransitionsByDefault = new(StringComparer.Ordinal)
        {
            "visibility", "background-position-x", "background-position-y",
        };

        private double _now;

        protected override void LoadStyleSheets() => VelvetStyleUtilities.AttachTo(_window.rootVisualElement);

        private VisualElement Resolved(string classNames)
        {
            var element = new VisualElement();
            foreach (var className in classNames.Split(' '))
            {
                element.AddToClassList(className);
            }
            _window.rootVisualElement.Add(element);
            _now = 100.0;
            EditorPanelTestHelpers.SetPanelTimeFunction(element.panel, () => _now);
            ForcePanelUpdate(element.panel);
            EditorPanelTestHelpers.DriveAnimationsOnce(element.panel);
            return element;
        }

        private void RunFor(VisualElement element, double seconds)
        {
            ForcePanelUpdate(element.panel);
            _now += seconds;
            EditorPanelTestHelpers.DriveAnimationsOnce(element.panel);
            ForcePanelUpdate(element.panel);
        }

        // A quarter of the way into the one-second transitions the cases declare.
        private void RunAQuarterSecond(VisualElement element) => RunFor(element, 0.25);

        private void Swap(VisualElement element, string from, string to)
        {
            element.RemoveFromClassList(from);
            element.AddToClassList(to);
            RunAQuarterSecond(element);
        }

        // The longhands UI Toolkit animates whose transitions it interpolates, and those it only switches that CSS
        // transitions by default, sorted. Each animatable property is started between the element's own values, which
        // records the Values class its longhands dispatch to.
        private static string[] InterpolatedOrTransitionedByCss(VisualElement element)
        {
            var engine = typeof(VisualElement).Assembly;
            var util = engine.GetType("UnityEngine.UIElements.StyleSheets.StylePropertyUtil", throwOnError: true);
            var animatable = (IEnumerable)util.GetField("s_AnimatableProperties", Internal)!.GetValue(null);
            var names = (IDictionary)util.GetField("s_IdToName", Internal)!.GetValue(null);
            var start = engine.GetType("UnityEngine.UIElements.ComputedStyle", throwOnError: true)
                .GetMethod("StartAnimation", Internal)!;
            var style = typeof(VisualElement).GetField("m_Style", Internal)!.GetValue(element);
            Func<float, float> linear = t => t;
            foreach (var id in animatable)
            {
                start.Invoke(null, new object[] { element, id, style, style, 1000, 0, linear });
            }
            var system = typeof(VisualElement).GetMethod("GetStylePropertyAnimationSystem", Internal)!
                .Invoke(element, null);
            var dispatched = (IDictionary)system.GetType().GetField("m_PropertyToValues", Internal)!.GetValue(system);
            var listed = new List<string>();
            foreach (DictionaryEntry entry in dispatched)
            {
                var name = (string)names[entry.Key];
                if (!OnlySwitches(entry.Value.GetType()) || s_cssTransitionsByDefault.Contains(name))
                {
                    listed.Add(name);
                }
            }
            return listed.OrderBy(name => name, StringComparer.Ordinal).ToArray();
        }

        private static bool OnlySwitches(Type values)
        {
            for (var type = values; type != null; type = type.BaseType)
            {
                if (type.IsGenericType && type.GetGenericTypeDefinition().Name == "ValuesDiscrete`1") return true;
            }
            return false;
        }

        [TestCase("transition-all")]
        [TestCase("transition-all transition-discrete transition-normal")]
        public void Given_ClassesThatTransitionEverythingUnderTheNormalBehavior_When_Resolved_Then_TheyListWhatTheEngineInterpolatesAndWhatCssTransitionsByDefault(
            string classNames)
        {
            // Arrange
            var element = Resolved(classNames);
            var expected = InterpolatedOrTransitionedByCss(element);

            // Act
            var listed = element.resolvedStyle.transitionProperty.Select(property => property.ToString())
                .OrderBy(name => name, StringComparer.Ordinal).ToArray();

            // Assert
            Assert.That(listed, Is.EqualTo(expected));
        }

        [Test]
        public void Given_AnElementTransitioningAll_When_ItsJustifyContentClassChanges_Then_ItTakesTheNewValueAtOnce()
        {
            // Arrange
            var element = Resolved("transition-all duration-1000 ease-linear justify-start");

            // Act
            Swap(element, "justify-start", "justify-end");

            // Assert
            Assert.That(element.resolvedStyle.justifyContent, Is.EqualTo(Justify.FlexEnd));
        }

        [Test]
        public void Given_AnElementTransitioningAll_When_AFontDefinitionIsWrittenInline_Then_ItShowsTheNewFontAtOnce()
        {
            // Arrange
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            var element = Resolved("transition-all duration-1000 ease-linear");

            // Act
            element.style.unityFontDefinition = new StyleFontDefinition(FontDefinition.FromFont(font));
            RunAQuarterSecond(element);

            // Assert
            Assert.That(element.resolvedStyle.unityFontDefinition.font, Is.SameAs(font));
        }

        // GREEN_ON_BASE(characterization): the base transitions opacity under transition-all as this change does.
        // Taking `opacity` out of transition-all's list in _effects.uss reddens this case.
        [Test]
        public void Given_AnElementTransitioningAllOnItsOwnDuration_When_ItsOpacityClassChanges_Then_AQuarterOfTheWayInItIsAQuarterOfTheWay()
        {
            // Arrange — transition-all's own duration, the sheet's 0.15s, so that nothing else gives the element one.
            var element = Resolved("transition-all ease-linear opacity-100");
            element.RemoveFromClassList("opacity-100");
            element.AddToClassList("opacity-0");

            // Act
            RunFor(element, 0.15 / 4);

            // Assert
            Assert.That(element.resolvedStyle.opacity, Is.EqualTo(0.75f).Within(0.01f));
        }

        // GREEN_ON_BASE(characterization): the base hands transition-all's switching properties to the engine anyway.
        // Deleting the `.transition-all.transition-discrete` rule of _preflight.uss reddens this case.
        [Test]
        public void Given_AnElementTransitioningAllWithTransitionDiscrete_When_ItsJustifyContentClassChanges_Then_AQuarterSecondInItKeepsTheOldValue()
        {
            // Arrange
            var element = Resolved("transition-all transition-discrete duration-1000 ease-linear justify-start");

            // Act
            Swap(element, "justify-start", "justify-end");

            // Assert
            Assert.That(element.resolvedStyle.justifyContent, Is.EqualTo(Justify.FlexStart));
        }

        // GREEN_ON_BASE(characterization): the engine switches a property it cannot interpolate before its end, on the base too.
        // Nothing in Velvet reddens this case: it fails when the engine stops switching by three quarters of the way.
        [Test]
        public void Given_AnElementTransitioningAllWithTransitionDiscrete_When_ItsJustifyContentClassChanges_Then_ThreeQuartersInItHoldsTheNewValue()
        {
            // Arrange
            var element = Resolved("transition-all transition-discrete duration-1000 ease-linear justify-start");
            element.RemoveFromClassList("justify-start");
            element.AddToClassList("justify-end");
            RunAQuarterSecond(element);

            // Act
            RunFor(element, 0.5);

            // Assert
            Assert.That(element.resolvedStyle.justifyContent, Is.EqualTo(Justify.FlexEnd));
        }

        // Every utility declaring transition-property after transition-all, in cascade order.
        private static IEnumerable<string> UtilitiesDeclaredAfterTransitionAll()
        {
            var table = (IDictionary)typeof(StyleTransitionUtilities).GetField("ByClassName", Internal)!.GetValue(null);
            StyleTransitionUtilities.TryGet("transition-all", out var transitionAll, out _);
            return table.Keys.Cast<string>()
                .Select(name => (Name: name, Position: StyleTransitionUtilities.TryGet(name, out var position, out _) ? position : -1))
                .Where(utility => utility.Position > transitionAll)
                .OrderBy(utility => utility.Position)
                .Select(utility => utility.Name);
        }

        private string[] Listed(string classNames) =>
            Resolved(classNames).resolvedStyle.transitionProperty.Select(property => property.ToString()).ToArray();

        // GREEN_ON_BASE(characterization): on the base each of these outranks transition-all, as it does here.
        // Deleting a utility's `.transition-all.transition-discrete.<utility>` rule of _preflight.uss reddens its case.
        [TestCaseSource(nameof(UtilitiesDeclaredAfterTransitionAll))]
        public void Given_AUtilityDeclaredAfterTransitionAll_When_TransitionAllAndTransitionDiscreteJoinIt_Then_ItListsWhatItListsAlone(
            string utility)
        {
            // Arrange
            var alone = Listed(utility);

            // Act
            var joined = Listed("transition-all transition-discrete " + utility);

            // Assert
            Assert.That(joined, Is.EqualTo(alone));
        }

        private static IEnumerable<TestCaseData> NeighboursDeclaredAfterTransitionAll()
        {
            var utilities = UtilitiesDeclaredAfterTransitionAll().ToArray();
            for (var i = 1; i < utilities.Length; i++)
            {
                yield return new TestCaseData(utilities[i - 1], utilities[i]);
            }
        }

        // GREEN_ON_BASE(characterization): on the base the later of two such neighbours outranks the earlier, as here.
        // Swapping the `_preflight.uss` rules of two neighbours whose lists differ reddens their case.
        [TestCaseSource(nameof(NeighboursDeclaredAfterTransitionAll))]
        public void Given_TwoNeighboursDeclaredAfterTransitionAll_When_TransitionAllAndTransitionDiscreteJoinThem_Then_TheyListWhatTheyListAlone(
            string earlier, string later)
        {
            // Arrange
            var alone = Listed(earlier + " " + later);

            // Act
            var joined = Listed("transition-all transition-discrete " + earlier + " " + later);

            // Assert
            Assert.That(joined, Is.EqualTo(alone));
        }
    }
}
