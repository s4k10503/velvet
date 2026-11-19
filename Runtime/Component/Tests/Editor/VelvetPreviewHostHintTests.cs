#if UNITY_EDITOR
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;

namespace Velvet.Tests
{
    // This assembly declares no [VelvetPreviewSetup], which is the condition under test.
    internal sealed class VelvetPreviewHostHintTests
    {
        private StyleSheet _sheet;

        private static VNode Story() => V.Div();

        [SetUp]
        public void SetUp() => _sheet = ScriptableObject.CreateInstance<StyleSheet>();

        [TearDown]
        public void TearDown()
        {
            VelvetStyleHints.PreviewStyleSheet = null;
            Object.DestroyImmediate(_sheet);
        }

        // GREEN_ON_BASE(characterization): the base takes the hint on every mount, setups or none.
        // The branch takes it after each setup and has to take it once more where there is none.
        [Test]
        public void Given_AHintAndAnAssemblyWithNoSetup_When_AStoryMounts_Then_TheSheetIsOnTheTarget()
        {
            // Arrange
            var method = typeof(VelvetPreviewHostHintTests).GetMethod(
                nameof(Story), BindingFlags.Static | BindingFlags.NonPublic);
            var ctor = typeof(VelvetPreviewStory).GetConstructor(
                BindingFlags.Instance | BindingFlags.NonPublic, null,
                new[] { typeof(MethodInfo), typeof(VelvetPreviewAttribute) }, null);
            Assume.That(ctor, Is.Not.Null, "VelvetPreviewStory's internal constructor must exist");
            var story = (VelvetPreviewStory)ctor.Invoke(
                new object[] { method, new VelvetPreviewAttribute { Name = "Hinted", Group = "HintFixture" } });
            var target = new VisualElement();
            using var host = new VelvetPreviewHost(target);
            VelvetStyleHints.PreviewStyleSheet = _sheet;

            // Act
            host.Mount(story);

            // Assert
            Assert.That(target.styleSheets.Contains(_sheet), Is.True);
        }
    }
}
#endif
