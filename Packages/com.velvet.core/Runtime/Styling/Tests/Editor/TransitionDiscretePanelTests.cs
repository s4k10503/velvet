using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;
using Velvet.Editor;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins CSS's transition-behavior on the bundled transition utilities: <c>transition-all</c>'s list, the
    /// <c>transition-discrete</c> and <c>transition-normal</c> classes <c>StyleTransitionBehavior</c> resolves,
    /// and the list a variant tween takes.
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

        private sealed class ClassNames : Store<string>
        {
            public ClassNames(string initial) : base(initial) { }

            public void Set(string value) => SetState(_ => value);

            protected override void ResetCore() => SetState(_ => string.Empty);
        }

        private static ClassNames s_classNames;

        [Component]
        private static VNode Leaf() => V.Div(name: "leaf", className: Hooks.UseStore(s_classNames, x => x));

        private readonly List<MountedTree> _mounts = new();
        private StyleSheet _sheet;
        private double _now;

        protected override void LoadStyleSheets() => VelvetStyleUtilities.AttachTo(_window.rootVisualElement);

        public override void TearDown()
        {
            VelvetTheme.IsDark = false;
            foreach (var mount in _mounts)
            {
                mount.Dispose();
            }
            _mounts.Clear();
            if (_sheet != null)
            {
                UnityEngine.Object.DestroyImmediate(_sheet);
                _sheet = null;
            }
            base.TearDown();
        }

        // An element carrying classNames as a plain class list, which no Velvet class handling reads.
        private VisualElement Resolved(string classNames)
        {
            var element = new VisualElement();
            foreach (var className in classNames.Split(' '))
            {
                element.AddToClassList(className);
            }
            return Attached(element);
        }

        // An element a className gives classNames, through the class projection.
        private VisualElement Mounted(string classNames)
        {
            var root = new VisualElement();
            _window.rootVisualElement.Add(root);
            _mounts.Add(V.Mount(root, V.Div(name: "leaf", className: classNames)));
            return Attached(root.Q<VisualElement>("leaf"));
        }

        private VisualElement Attached(VisualElement element)
        {
            if (element.panel == null)
            {
                _window.rootVisualElement.Add(element);
            }
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

        private static string[] Listed(VisualElement element)
            => element.resolvedStyle.transitionProperty.Select(property => property.ToString()).ToArray();

        private static string[] Sorted(IEnumerable<string> names) => names.OrderBy(name => name, StringComparer.Ordinal).ToArray();

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
            return Sorted(listed);
        }

        private static bool OnlySwitches(Type values)
        {
            for (var type = values; type != null; type = type.BaseType)
            {
                if (type.IsGenericType && type.GetGenericTypeDefinition().Name == "ValuesDiscrete`1") return true;
            }
            return false;
        }

        // A stylesheet of the application's own, attached after the bundled one.
        private void AttachSheet(string uss)
        {
            _sheet = ScriptableObject.CreateInstance<StyleSheet>();
            var impl = Activator.CreateInstance(StockStyleSheetImport.Impl!, true)!;
            try
            {
                StockStyleSheetImport.Import!.Invoke(impl, new object[] { _sheet, uss });
            }
            catch (TargetInvocationException)
            {
                // The importer reports through its context, which this instance has none of; what it stored is used.
            }
            _window.rootVisualElement.styleSheets.Add(_sheet);
        }

        [Test]
        public void Given_TransitionAll_When_Resolved_Then_ItListsWhatTheEngineInterpolatesAndWhatCssTransitionsByDefault()
        {
            // Arrange
            var element = Resolved("transition-all");
            var expected = InterpolatedOrTransitionedByCss(element);

            // Act
            var listed = Sorted(Listed(element));

            // Assert
            Assert.That(listed, Is.EqualTo(expected));
        }

        [Test]
        public void Given_TransitionDiscreteAndTransitionNormalOnOneRank_When_TransitionAllResolves_Then_ItKeepsItsOwnList()
        {
            // Arrange
            var element = Mounted("transition-all transition-discrete transition-normal");
            var expected = InterpolatedOrTransitionedByCss(element);

            // Act
            var listed = Sorted(Listed(element));

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
        // Deleting the AddToModel of transition-all-discrete in StyleTransitionBehavior.Sync reddens this case.
        [Test]
        public void Given_AnElementTransitioningAllWithTransitionDiscrete_When_ItsJustifyContentIsWritten_Then_AQuarterSecondInItKeepsTheOldValue()
        {
            // Arrange
            var element = Mounted("transition-all transition-discrete duration-1000 ease-linear");

            // Act
            element.style.justifyContent = Justify.FlexEnd;
            RunAQuarterSecond(element);

            // Assert
            Assert.That(element.resolvedStyle.justifyContent, Is.EqualTo(Justify.FlexStart));
        }

        // GREEN_ON_BASE(characterization): the base hands transition-all's switching properties to the engine anyway.
        // Deleting the AddToModel of transition-all-discrete in StyleTransitionBehavior.Add reddens this case.
        [Test]
        public void Given_TransitionDiscreteWrittenBeforeTransitionAll_When_ItsJustifyContentIsWritten_Then_AQuarterSecondInItKeepsTheOldValue()
        {
            // Arrange
            var element = Mounted("transition-discrete transition-all duration-1000 ease-linear");

            // Act
            element.style.justifyContent = Justify.FlexEnd;
            RunAQuarterSecond(element);

            // Assert
            Assert.That(element.resolvedStyle.justifyContent, Is.EqualTo(Justify.FlexStart));
        }

        [Test]
        public void Given_TransitionAllWithTransitionDiscrete_When_ARenderDropsTransitionDiscrete_Then_TransitionAllKeepsItsOwnList()
        {
            // Arrange
            s_classNames = new ClassNames("transition-all transition-discrete");
            var root = new VisualElement();
            _window.rootVisualElement.Add(root);
            _mounted = V.Mount(root, V.Component(Leaf, key: "root"));
            var element = Attached(root.Q<VisualElement>("leaf"));
            var expected = InterpolatedOrTransitionedByCss(element);

            // Act
            s_classNames.Set("transition-all");
            _mounted.FlushStateForTest();
            ForcePanelUpdate(element.panel);

            // Assert
            Assert.That(Sorted(Listed(element)), Is.EqualTo(expected));
        }

        // GREEN_ON_BASE(characterization): on the base transition-discrete alone gives the element no duration.
        // Leaving transition-all-discrete on when StyleTransitionBehavior.Remove takes transition-all off reddens this case.
        [Test]
        public void Given_TransitionAllWithTransitionDiscrete_When_ARenderDropsTransitionAll_Then_NothingGivesTheElementADuration()
        {
            // Arrange
            s_classNames = new ClassNames("transition-all transition-discrete");
            var root = new VisualElement();
            _window.rootVisualElement.Add(root);
            _mounted = V.Mount(root, V.Component(Leaf, key: "root"));
            var element = Attached(root.Q<VisualElement>("leaf"));

            // Act
            s_classNames.Set("transition-discrete");
            _mounted.FlushStateForTest();
            ForcePanelUpdate(element.panel);

            // Assert
            Assert.That(element.resolvedStyle.transitionDuration.Select(time => time.value).ToArray(), Is.EqualTo(new[] { 0f }));
        }

        // GREEN_ON_BASE(characterization): the engine switches a property it cannot interpolate before its end, on the base too.
        // Nothing in Velvet reddens this case: it fails when the engine stops switching by three quarters of the way.
        [Test]
        public void Given_AnElementTransitioningAllWithTransitionDiscrete_When_ItsJustifyContentIsWritten_Then_ThreeQuartersInItHoldsTheNewValue()
        {
            // Arrange
            var element = Mounted("transition-all transition-discrete duration-1000 ease-linear");
            element.style.justifyContent = Justify.FlexEnd;
            RunAQuarterSecond(element);

            // Act
            RunFor(element, 0.5);

            // Assert
            Assert.That(element.resolvedStyle.justifyContent, Is.EqualTo(Justify.FlexEnd));
        }

        // GREEN_ON_BASE(characterization): the base lists `all` for transition-all whatever ranks the behaviours.
        // Ranking transition-normal over transition-discrete whatever their keys, as compound selectors did, reddens this.
        [Test]
        public void Given_TransitionNormalAndADarkTransitionDiscrete_When_TheThemeTurnsDark_Then_TransitionAllListsAll()
        {
            // Arrange
            var element = Mounted("transition-all transition-normal dark:transition-discrete");

            // Act
            VelvetTheme.IsDark = true;
            ForcePanelUpdate(element.panel);

            // Assert
            Assert.That(Listed(element), Is.EqualTo(new[] { "all" }));
        }

        [Test]
        public void Given_TransitionDiscreteAndADarkTransitionNormal_When_TheThemeTurnsDark_Then_TransitionAllKeepsItsOwnList()
        {
            // Arrange
            var element = Mounted("transition-all transition-discrete dark:transition-normal");
            var expected = InterpolatedOrTransitionedByCss(element);

            // Act
            VelvetTheme.IsDark = true;
            ForcePanelUpdate(element.panel);

            // Assert
            Assert.That(Sorted(Listed(element)), Is.EqualTo(expected));
        }

        // GREEN_ON_BASE(characterization): on the base a rule of the application's own sheet outranks transition-all.
        // A selector raising transition-discrete above single-class rules, as `.transition-all.transition-discrete` did,
        // reddens this case.
        [Test]
        public void Given_AnApplicationRuleListingOpacity_When_TransitionAllAndTransitionDiscreteJoinIt_Then_TheRuleHoldsTheProperty()
        {
            // Arrange
            AttachSheet(".app-list { transition-property: opacity; }");

            // Act
            var element = Mounted("transition-all transition-discrete app-list");

            // Assert
            Assert.That(Listed(element), Is.EqualTo(new[] { "opacity" }));
        }

        // GREEN_ON_BASE(characterization): on the base a rule selecting `.transition-all` applies as it does here.
        // Taking `transition-all` off the live list where transition-all-discrete goes on reddens this case.
        [Test]
        public void Given_AnApplicationRuleSelectingTransitionAll_When_TransitionDiscreteJoinsIt_Then_TheRuleStillApplies()
        {
            // Arrange
            AttachSheet(".transition-all.app-slow { transition-duration: 2s; }");

            // Act
            var element = Mounted("transition-all transition-discrete app-slow");

            // Assert
            Assert.That(element.resolvedStyle.transitionDuration.Select(time => time.value).ToArray(),
                Is.EqualTo(new[] { 2f }));
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

        // GREEN_ON_BASE(characterization): on the base each of these outranks transition-all, as it does here.
        // Declaring `.transition-all-discrete` after a utility in _effects.uss reddens that utility's case.
        [TestCaseSource(nameof(UtilitiesDeclaredAfterTransitionAll))]
        public void Given_AUtilityDeclaredAfterTransitionAll_When_TransitionAllAndTransitionDiscreteJoinIt_Then_ItListsWhatItListsAlone(
            string utility)
        {
            // Arrange
            var alone = Listed(Mounted(utility));

            // Act
            var joined = Listed(Mounted("transition-all transition-discrete " + utility));

            // Assert
            Assert.That(joined, Is.EqualTo(alone));
        }

        [Test]
        public void Given_AVariantTween_When_ItStarts_Then_ItListsWhatTheEngineInterpolatesAndWhatCssTransitionsByDefault()
        {
            // Arrange
            var element = Attached(new VisualElement());
            var expected = InterpolatedOrTransitionedByCss(element);

            // Act
            new StyleAnimationScheduler().PlayVariantEnter(element, Array.Empty<string>(), Array.Empty<string>(),
                durationSec: 1f, easing: EasingMode.Linear, delaySec: 0f);
            ForcePanelUpdate(element.panel);

            // Assert
            Assert.That(Sorted(Listed(element)), Is.EqualTo(expected));
        }

        // GREEN_ON_BASE(characterization): the base's variant tween lists `all` on every element.
        // Passing `false` for AllowsDiscrete in StyleAnimationScheduler.ApplyTransitionStyles reddens this case.
        [Test]
        public void Given_AVariantTweenOnAnElementWithTransitionDiscrete_When_ItStarts_Then_ItListsAll()
        {
            // Arrange
            var element = Mounted("transition-discrete");

            // Act
            new StyleAnimationScheduler().PlayVariantEnter(element, Array.Empty<string>(), Array.Empty<string>(),
                durationSec: 1f, easing: EasingMode.Linear, delaySec: 0f);
            ForcePanelUpdate(element.panel);

            // Assert
            Assert.That(Listed(element), Is.EqualTo(new[] { "all" }));
        }

        [Test]
        public void Given_AVariantTweenWithAnOverrideNamingAll_When_ItStarts_Then_TheOverrideListsWhatTheEngineInterpolatesAndWhatCssTransitionsByDefault()
        {
            // Arrange
            var element = Attached(new VisualElement());
            var expected = InterpolatedOrTransitionedByCss(element);
            var overrides = new[] { new StylePropertyTransition("all", durationSec: 0.5f) };

            // Act
            new StyleAnimationScheduler().PlayVariantEnter(element, Array.Empty<string>(), Array.Empty<string>(),
                durationSec: 1f, easing: EasingMode.Linear, delaySec: 0f, propertyOverrides: overrides);
            ForcePanelUpdate(element.panel);

            // Assert
            Assert.That(Sorted(Listed(element).Skip(expected.Length)), Is.EqualTo(expected));
        }
    }
}
