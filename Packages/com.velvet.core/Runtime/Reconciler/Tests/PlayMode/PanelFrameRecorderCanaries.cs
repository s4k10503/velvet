using System;
using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests.Performance
{
    /// <summary>
    /// Answers for the reading the frame benchmarks take through <see cref="PanelFrameRecorder"/>, never for what
    /// they measure: a count of allocations made before a frame boundary, read after it.
    /// </summary>
    [TestFixture]
    internal sealed class PanelFrameRecorderCanaries
    {
        private const string PanelName = "VelvetPanelFrameRecorderCanaryPanel";
        private const int KnownAllocations = 500;

        private GameObject _gameObject = null!;
        private PanelSettings _panelSettings = null!;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _gameObject = new GameObject("PanelFrameRecorderCanaryRoot");
            var document = _gameObject.AddComponent<UIDocument>();
            _panelSettings = TestPanelSettings.Create();
            _panelSettings.name = PanelName;
            document.panelSettings = _panelSettings;
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            UnityEngine.Object.Destroy(_gameObject);
            UnityEngine.Object.Destroy(_panelSettings);
            yield return null;
        }

        // GREEN_ON_BASE(characterization): the recorder behaviour this pins is the engine's, which the base runs too.
        [UnityTest]
        public IEnumerator Given_AllocationsMadeAfterStart_When_TheFrameEndsBeforeCapture_Then_CaptureCountsThem()
        {
            // Arrange
            var frame = new PanelFrameRecorder(PanelName);
            frame.Start();
            for (var i = 0; i < KnownAllocations; i++)
            {
                GC.KeepAlive(new byte[16]);
            }

            // Act
            yield return null;
            frame.Capture();
            frame.Stop();

            // Assert
            Assert.That(frame.AllocationBlocks, Is.GreaterThanOrEqualTo(KnownAllocations));
        }
    }
}
