#if UNITY_EDITOR
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies that in an EditorWindow <c>vw</c> and <c>vh</c> measure the window's content — its
    /// <c>rootVisualElement</c> — though what they read is the panel's root.
    /// </summary>
    internal sealed class ViewportWindowTests : PanelTestBase
    {
        // The re-measuring poll ticks on the panel's clock, which a fixture driving the scheduler back to back
        // does not move, so a fake clock is advanced a tick's worth before each drive.
        private VisualElement MountLeaf(string className)
        {
            var panel = _window.rootVisualElement.panel;
            UseFrameFakeClockHost.Reset();
            EditorPanelTestHelpers.SetPanelTimeFunction(panel, UseFrameFakeClockHost.ReadFakeClock);
            _mounted = V.Mount(_window.rootVisualElement, V.Div(name: "leaf", className: className));
            for (var i = 0; i < 3; i++)
            {
                ForcePanelUpdate(panel);
                UseFrameFakeClockHost.Ms += 16;
                EditorPanelTestHelpers.DriveSchedulerOnce(panel);
            }
            ForcePanelUpdate(panel);
            return _window.rootVisualElement.Q<VisualElement>("leaf");
        }

        [Test]
        public void Given_AHalfViewportWidthInAWindow_When_Settled_Then_ItIsHalfTheWindowsContentWidth()
        {
            // Act
            var leaf = MountLeaf("w-[50vw] h-[10px]");

            // Assert — NaN unless the window laid its content out.
            var content = _window.rootVisualElement.layout.width;
            Assert.That(leaf.layout.width, Is.EqualTo(content > 0f ? content / 2f : float.NaN).Within(0.01f));
        }

        [Test]
        public void Given_AHalfViewportHeightInAWindow_When_Settled_Then_ItIsHalfTheWindowsContentHeight()
        {
            // Act
            var leaf = MountLeaf("w-[10px] h-[50vh]");

            // Assert — NaN unless the window laid its content out.
            var content = _window.rootVisualElement.layout.height;
            Assert.That(leaf.layout.height, Is.EqualTo(content > 0f ? content / 2f : float.NaN).Within(0.01f));
        }
    }
}
#endif
