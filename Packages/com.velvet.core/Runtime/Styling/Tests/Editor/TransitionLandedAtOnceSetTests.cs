using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins <c>StyleTransitionBehavior.LandedAtOnce</c> to the longhands UI Toolkit only switches partway through a
    /// transition, read from the engine itself.
    /// </summary>
    internal sealed class TransitionLandedAtOnceSetTests : PanelTestBase
    {
        private const BindingFlags Internal = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static
            | BindingFlags.Instance;

        // CSS transitions these under transition-behavior: normal: visibility keeps its visible end for the whole
        // transition, and background-position interpolates.
        private static readonly HashSet<string> s_cssTransitionsByDefault = new(StringComparer.Ordinal)
        {
            "visibility", "background-position-x", "background-position-y",
        };

        // Left to switch as they did (StyleTransitionBehavior.LandedAtOnce says why).
        private static readonly HashSet<string> s_fontProperties = new(StringComparer.Ordinal)
        {
            "-unity-font", "-unity-font-definition", "-unity-font-style",
        };

        private static string[] Sorted(IEnumerable<string> names) => names.OrderBy(name => name, StringComparer.Ordinal).ToArray();

        // Each animatable property is started between the element's own values, which records the Values class its
        // longhands dispatch to; those whose class derives from ValuesDiscrete only switch.
        private static IEnumerable<string> OnlySwitchedByTheEngine(VisualElement element)
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
            foreach (DictionaryEntry entry in dispatched)
            {
                if (OnlySwitches(entry.Value.GetType()))
                {
                    yield return (string)names[entry.Key];
                }
            }
        }

        private static bool OnlySwitches(Type values)
        {
            for (var type = values; type != null; type = type.BaseType)
            {
                if (type.IsGenericType && type.GetGenericTypeDefinition().Name == "ValuesDiscrete`1") return true;
            }
            return false;
        }

        [Test]
        public void Given_TheLonghandsTheEngineOnlySwitches_When_WhatCssTransitionsAndTheFontsAreSetAside_Then_TheRestAreLandedAtOnce()
        {
            // Arrange
            var element = new VisualElement();
            _window.rootVisualElement.Add(element);
            ForcePanelUpdate(element.panel);

            // Act
            var expected = Sorted(OnlySwitchedByTheEngine(element)
                .Where(name => !s_cssTransitionsByDefault.Contains(name) && !s_fontProperties.Contains(name)));

            // Assert
            Assert.That(Sorted(StyleTransitionBehavior.LandedAtOnce), Is.EqualTo(expected));
        }
    }
}
