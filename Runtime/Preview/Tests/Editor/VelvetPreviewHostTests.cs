using System;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Mount contract for <see cref="VelvetPreviewHost"/>: a story mounts onto a real panel and its element
    /// appears under the target; unmounting (dispose) removes it; and a failing story (null tree or a throw)
    /// leaves no leftover stylesheet and no live <see cref="VelvetPreviewHost.Story"/> — so the window does not
    /// keep repainting / re-mounting something that cannot render.
    /// </summary>
    /// <remarks>
    /// Stories are built directly from this fixture's methods (via the <c>internal</c> story constructor,
    /// reached by reflection) rather than through a registry scan, so these tests do not depend on — or trip the
    /// diagnostics of — the other preview fixtures sharing this test assembly.
    /// </remarks>
    internal sealed class VelvetPreviewHostTests
    {
        private const string Marker = "preview-host-marker";

        private static VNode MarkerStory() => V.Div(name: Marker, className: "box");
        private static VNode NullStory() => null;                                  // idiomatic render-nothing
        private static VNode ThrowingStory() => throw new InvalidOperationException("boom");

        private EditorWindow _window;

        [SetUp]
        public void SetUp()
        {
            TestGraphics.IgnoreIfHeadless("an EditorWindow panel");
            _window = ScriptableObject.CreateInstance<HostWindow>();
            _window.position = new Rect(0, 0, 400, 300);
            _window.Show();
        }

        [TearDown]
        public void TearDown()
        {
            VelvetStyleHints.PreviewStyleSheet = null;
            if (_window == null) return;
            _window.Close();
            UnityEngine.Object.DestroyImmediate(_window);
            _window = null;
        }

        // Builds a VelvetPreviewStory around a named method on this fixture without going through discovery.
        private static VelvetPreviewStory Story(string methodName)
        {
            var method = typeof(VelvetPreviewHostTests).GetMethod(
                methodName, BindingFlags.Static | BindingFlags.NonPublic);
            Assume.That(method, Is.Not.Null, $"fixture method '{methodName}' must exist");
            var attribute = new VelvetPreviewAttribute { Name = methodName, Group = "HostFixture" };
            var ctor = typeof(VelvetPreviewStory).GetConstructor(
                BindingFlags.Instance | BindingFlags.NonPublic, null,
                new[] { typeof(MethodInfo), typeof(VelvetPreviewAttribute) }, null);
            Assume.That(ctor, Is.Not.Null, "VelvetPreviewStory's internal constructor must exist");
            return (VelvetPreviewStory)ctor.Invoke(new object[] { method, attribute });
        }

        [Test]
        public void Given_AStory_When_HostMounts_Then_TheStoryElementIsUnderTheTarget()
        {
            // Arrange
            using var host = new VelvetPreviewHost(_window.rootVisualElement);

            // Act
            host.Mount(Story(nameof(MarkerStory)));

            // Assert
            Assert.That(_window.rootVisualElement.Q<VisualElement>(Marker), Is.Not.Null);
        }

        [Test]
        public void Given_AMountedStory_When_HostDisposed_Then_TheStoryElementIsRemoved()
        {
            // Arrange
            var host = new VelvetPreviewHost(_window.rootVisualElement);
            host.Mount(Story(nameof(MarkerStory)));
            Assume.That(_window.rootVisualElement.Q<VisualElement>(Marker), Is.Not.Null, "the story mounted first");

            // Act
            host.Dispose();

            // Assert
            Assert.That(_window.rootVisualElement.Q<VisualElement>(Marker), Is.Null);
        }

        [Test]
        public void Given_AHintedStyleSheet_When_AStoryMountsNull_Then_TheSheetIsNotLeftOnTheTarget()
        {
            // Arrange
            var sheet = ScriptableObject.CreateInstance<StyleSheet>();
            VelvetStyleHints.PreviewStyleSheet = sheet;
            using var host = new VelvetPreviewHost(_window.rootVisualElement);

            // Act
            host.Mount(Story(nameof(NullStory)));

            // Assert
            Assert.That(_window.rootVisualElement.styleSheets.Contains(sheet), Is.False);
        }

        [Test]
        public void Given_AStoryThatThrows_When_HostMounts_Then_StoryIsLeftNull()
        {
            // Arrange
            using var host = new VelvetPreviewHost(_window.rootVisualElement);

            // Act
            host.Mount(Story(nameof(ThrowingStory)));

            // Assert
            Assert.That(host.Story, Is.Null);
        }

        private sealed class HostWindow : EditorWindow { }
    }
}
