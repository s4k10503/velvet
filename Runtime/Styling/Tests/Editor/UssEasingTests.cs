using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;

namespace Velvet.Tests
{
    [TestFixture]
    internal sealed class UssEasingTests
    {
        [Test]
        public void Given_EveryEasingMode_When_EvaluatedAlongTheCurve_Then_ItMatchesTheCurveUIToolkitEasesATransitionBy()
        {
            // Arrange — every mode, and one either side of the enum.
            var convert = typeof(VisualElement).Assembly.GetType("UnityEngine.UIElements.ComputedTransitionUtils")
                .GetMethod("ConvertTransitionFunction", BindingFlags.NonPublic | BindingFlags.Static);
            var modes = Enum.GetValues(typeof(EasingMode)).Cast<EasingMode>()
                .Append((EasingMode)(-1))
                .Append((EasingMode)Enum.GetValues(typeof(EasingMode)).Length);
            var samples = new[] { 0.1f, 0.3f, 0.5f, 0.7f, 0.9f };

            // Act
            var worst = modes.Select(mode =>
            {
                var curve = (Func<float, float>)convert.Invoke(null, new object[] { mode });
                return samples.Max(t => Mathf.Abs(curve(t) - UssEasing.Evaluate(mode, t)));
            }).ToArray();

            // Assert
            Assert.That(worst, Is.All.LessThan(1e-6f));
        }
    }
}
