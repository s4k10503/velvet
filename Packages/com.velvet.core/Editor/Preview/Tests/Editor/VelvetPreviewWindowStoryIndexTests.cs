using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine.UIElements;
using Velvet.Editor.Preview;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    // This assembly holds VelvetPreviewDuplicateIdTests' colliding pair, so discovering it is refused.
    internal sealed class VelvetPreviewWindowStoryIndexTests
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

        private VelvetPreviewWindow _window;

        [SetUp]
        public void SetUp()
        {
            TestGraphics.IgnoreIfHeadless("an EditorWindow panel");
            _window = EditorWindow.GetWindow<VelvetPreviewWindow>();
            _window.Show();
        }

        [TearDown]
        public void TearDown()
        {
            if (_window == null) return;
            _window.Close();
            _window = null;
        }

        private static List<VelvetPreviewStory> DiscoverThisAssembly()
        {
            var discover = typeof(VelvetPreviewRegistry).GetMethod(
                "DiscoverStoriesIn", BindingFlags.Static | BindingFlags.NonPublic);
            try
            {
                return (List<VelvetPreviewStory>)discover!.Invoke(
                    null, new object[] { new[] { typeof(VelvetPreviewWindowStoryIndexTests).Assembly } });
            }
            catch (TargetInvocationException ex) when (ex.InnerException != null)
            {
                throw ex.InnerException;
            }
        }

        [Test]
        public void Given_AStorySetHoldingADuplicateId_When_TheWindowRefreshes_Then_ItListsNoStoryAndShowsTheRefusal()
        {
            // Arrange
            var refusal = string.Empty;
            try
            {
                DiscoverThisAssembly();
            }
            catch (InvalidOperationException ex)
            {
                refusal = ex.Message;
            }

            // Act
            _window.RefreshStories(DiscoverThisAssembly);

            // Assert
            var status = (Label)typeof(VelvetPreviewWindow).GetField("_statusLabel", Private)?.GetValue(_window);
            var stories = (List<VelvetPreviewStory>)typeof(VelvetPreviewWindow).GetField("_stories", Private)
                ?.GetValue(_window);
            Assert.That((status?.text, stories?.Count ?? -1), Is.EqualTo((refusal, 0)));
        }

        [Test]
        public void Given_CachedStoryDiscovery_When_TheWindowRediscovers_Then_TheStoriesAreDiscoveredAfresh()
        {
            // Arrange
            var cached = VelvetPreviewRegistry.DiscoverStories();

            // Act
            _window.Rediscover();

            // Assert
            var fresh = VelvetPreviewRegistry.DiscoverStories();
            var listed = (List<VelvetPreviewStory>)typeof(VelvetPreviewWindow).GetField("_stories", Private)
                ?.GetValue(_window);
            Assert.That(
                (ReferenceEquals(fresh, cached), fresh.Count > 0 && listed?.Count > 0 && ReferenceEquals(listed[0], fresh[0])),
                Is.EqualTo((false, true)));
        }
    }
}
