using System.Collections;
using System.Diagnostics;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests.Performance
{
    /// <summary>
    /// Answers for how <see cref="IdleFrameBenchmarks"/>' scheduler series are read, never for what they measure: a
    /// frame's pass over the panel's scheduled items stops before it has run all of them when they take long enough,
    /// so a <c>Frame.TickScheduledActions</c> sample can be that stop rather than the whole of the polls' cost.
    /// </summary>
    [TestFixture]
    internal sealed class PanelSchedulerCanaries
    {
        private const string PanelName = "VelvetPanelSchedulerCanaryPanel";
        private const int Items = 100;
        private const double MillisecondsEach = 1.0;

        private GameObject _gameObject = null!;
        private PanelSettings _panelSettings = null!;
        private VisualElement _host = null!;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _gameObject = new GameObject("PanelSchedulerCanaryRoot");
            var document = _gameObject.AddComponent<UIDocument>();
            _panelSettings = TestPanelSettings.Create();
            _panelSettings.name = PanelName;
            document.panelSettings = _panelSettings;
            yield return null;
            _host = new VisualElement();
            document.rootVisualElement.Add(_host);
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            _host.RemoveFromHierarchy();
            UnityEngine.Object.Destroy(_gameObject);
            UnityEngine.Object.Destroy(_panelSettings);
            yield return null;
        }

        // GREEN_ON_BASE(characterization): the scheduler behaviour this pins is the engine's, which the base runs too.
        [UnityTest]
        public IEnumerator Given_ScheduledItemsTakingAFrameAndMoreTogether_When_OneFrameTicksTheScheduler_Then_SomeOfThemWait()
        {
            // Arrange
            var ran = 0;
            for (var i = 0; i < Items; i++)
            {
                _host.schedule.Execute(() =>
                {
                    Spin(MillisecondsEach);
                    ran++;
                });
            }

            // Act
            yield return null;

            // Assert
            Assert.That(ran, Is.InRange(1, Items - 1));
        }

        private static void Spin(double milliseconds)
        {
            var until = Stopwatch.GetTimestamp() + (long)(milliseconds * Stopwatch.Frequency / 1000.0);
            while (Stopwatch.GetTimestamp() < until)
            {
            }
        }
    }
}
