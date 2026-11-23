using System.Reflection;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies a <see cref="PanelFrameClock"/> subscription's life against the panel it is subscribed on.
    /// <list type="bullet">
    /// <item>A host attached to its panel receives the panel's frames.</item>
    /// <item>A host that leaves its panel stops receiving them.</item>
    /// <item>A disposed subscription stays inactive when its host attaches to a panel again.</item>
    /// </list>
    /// </summary>
    [TestFixture]
    internal sealed class PanelFrameClockSubscriptionTests : PanelTestBase
    {
        private int _frames;

        [SetUp]
        public override void SetUp()
        {
            base.SetUp();
            UseFrameFakeClockHost.Reset();
            EditorPanelTestHelpers.SetPanelTimeFunction(_window.rootVisualElement.panel, UseFrameFakeClockHost.ReadFakeClock);
            _frames = 0;
        }

        private IFrameSubscription SubscribeOnPanel(VisualElement host)
        {
            _window.rootVisualElement.Add(host);
            var subscription = PanelFrameClock.Instance.Subscribe(host, 0, _ => _frames++, null);
            EditorPanelTestHelpers.DriveSchedulerOnce(_window.rootVisualElement.panel);
            return subscription;
        }

        private void Tick()
        {
            UseFrameFakeClockHost.Ms += 16;
            EditorPanelTestHelpers.DriveSchedulerOnce(_window.rootVisualElement.panel);
        }

        // GREEN_ON_BASE(characterization): the base hands a panel's frames to a callback whose host is attached when it subscribes.
        // Drop `_subscription = current.Subscribe(_priority, _onFrame, _clock);` from EnsureSubscribed and no frame
        // reaches the callback.
        [Test]
        public void Given_AHostAttachedToItsPanel_When_ThePanelTicks_Then_TheCallbackFires()
        {
            // Arrange
            using var subscription = SubscribeOnPanel(new VisualElement());
            var before = _frames;

            // Act
            Tick();

            // Assert
            Assert.That(_frames - before, Is.GreaterThan(0));
        }

        // GREEN_ON_BASE(characterization): the base stops a subscription's frames once its host leaves the panel.
        // Drop `_subscription.Active = false;` from the detach callback and the callback still fires on the tick.
        [Test]
        public void Given_AHostThatLeftItsPanel_When_ThePanelTicks_Then_TheCallbackDoesNotFire()
        {
            // Arrange
            var host = new VisualElement();
            using var subscription = SubscribeOnPanel(host);
            host.RemoveFromHierarchy();
            var before = _frames;

            // Act
            Tick();

            // Assert
            Assert.That(_frames - before, Is.EqualTo(0));
        }

        // GREEN_ON_BASE(characterization): the base leaves a disposed subscription inactive when its host attaches to a panel again.
        // Drop `_host.UnregisterCallback(_onAttach);` from Dispose and the attach callback sets the subscription
        // active again.
        [Test]
        public void Given_ADisposedSubscription_When_ItsHostAttachesToThePanelAgain_Then_ItIsNotActive()
        {
            // Arrange
            var host = new VisualElement();
            var subscription = SubscribeOnPanel(host);
            subscription.Dispose();
            host.RemoveFromHierarchy();

            // Act
            _window.rootVisualElement.Add(host);

            // Assert
            var inner = subscription.GetType()
                .GetField("_subscription", BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(subscription);
            var active = (bool)inner.GetType().GetField("Active").GetValue(inner);
            Assert.That(active, Is.False);
        }
    }
}
