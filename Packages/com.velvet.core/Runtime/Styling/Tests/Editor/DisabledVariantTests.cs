using System;
using System.Reflection;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies the <c>disabled:</c> variant: its payload holds while the element or an ancestor has
    /// <c>enabledSelf</c> off, the condition UI Toolkit's <c>:disabled</c> pseudo-class matches. Mounted in a
    /// real panel, since UI Toolkit announces an <c>enabledSelf</c> write only on an element that has one.
    /// </summary>
    [TestFixture]
    internal sealed class DisabledVariantTests : PanelTestBase
    {
        private bool _darkBefore;

        public override void SetUp()
        {
            base.SetUp();
            _darkBefore = VelvetTheme.IsDark;
            VelvetTheme.IsDark = false;
        }

        public override void TearDown()
        {
            base.TearDown();
            s_setClass = default;
            VelvetTheme.IsDark = _darkBefore;
        }

        private (VisualElement Outer, VisualElement Leaf) Mount(string leafClassName)
        {
            _mounted = V.Mount(_window.rootVisualElement,
                V.Div(name: "outer", children: new VNode?[]
                {
                    V.Div(name: "leaf", className: leafClassName),
                }));
            var root = _window.rootVisualElement;
            return (root.Q<VisualElement>("outer"), root.Q<VisualElement>("leaf"));
        }

        private static string s_initialClass;
        private static StateUpdater<string> s_setClass;

        [Component]
        private static VNode RenderSwitchable()
        {
            var (className, setClass) = Hooks.UseState(s_initialClass);
            s_setClass = setClass;
            return V.Div(name: "leaf", className: className);
        }

        private static void RegisterProbe<TEvent>(VisualElement element, Action onEvent)
            where TEvent : EventBase<TEvent>, new()
            => element.RegisterCallback<TEvent>(_ => onEvent());

        [Test]
        public void Given_ADisabledPayload_When_AnAncestorIsDisabled_Then_ThePayloadIsApplied()
        {
            // Arrange
            var (outer, leaf) = Mount("disabled:bg-hot");

            // Act — written outside any event dispatch, where the announcement arrives before the new state
            // has reached the leaf.
            outer.SetEnabled(false);

            // Assert
            Assert.That(leaf.ClassListContains("bg-hot"), Is.True);
        }

        [Test]
        public void Given_ADisabledPayloadApplied_When_TheAncestorIsEnabledAgain_Then_ThePayloadIsRemoved()
        {
            // Arrange
            var (outer, leaf) = Mount("disabled:bg-hot");
            outer.SetEnabled(false);
            var appliedWhileDisabled = leaf.ClassListContains("bg-hot");

            // Act
            outer.SetEnabled(true);

            // Assert — the applied half rides along, so a payload that never landed cannot read as removed.
            Assert.That((appliedWhileDisabled, leaf.ClassListContains("bg-hot")), Is.EqualTo((true, false)));
        }

        [Test]
        public void Given_AHostAlreadyDisabled_When_ADisabledPayloadMountsUnderIt_Then_ThePayloadIsApplied()
        {
            // Arrange
            var host = new VisualElement();
            host.SetEnabled(false);
            _window.rootVisualElement.Add(host);

            // Act
            _mounted = V.Mount(host, V.Div(name: "leaf", className: "disabled:bg-hot"));

            // Assert
            Assert.That(host.Q<VisualElement>("leaf").ClassListContains("bg-hot"), Is.True);
        }

        [Test]
        public void Given_AnElementWithAnotherVariant_When_ARenderAddsADisabledPayloadUnderADisabledHost_Then_ThePayloadIsApplied()
        {
            // Arrange — hover: already gives the element its manipulator, so the render updates rather than
            // creates it.
            var host = new VisualElement();
            host.SetEnabled(false);
            _window.rootVisualElement.Add(host);
            s_initialClass = "hover:bg-cold";
            _mounted = V.Mount(host, V.Component(RenderSwitchable));

            // Act
            s_setClass.Invoke("hover:bg-cold disabled:bg-hot");
            _mounted.Root.Reconciler.Context.BatchScheduler.DrainImmediateForTest();

            // Assert
            Assert.That(host.Q<VisualElement>("leaf").ClassListContains("bg-hot"), Is.True);
        }

        [Test]
        public void Given_AMountedDisabledPayload_When_ItsElementMovesUnderADisabledHost_Then_ThePayloadIsApplied()
        {
            // Arrange
            var (_, leaf) = Mount("disabled:bg-hot");
            var host = new VisualElement();
            host.SetEnabled(false);
            _window.rootVisualElement.Add(host);

            // Act
            host.Add(leaf);

            // Assert
            Assert.That(leaf.ClassListContains("bg-hot"), Is.True);
        }

        [Test]
        public void Given_ActiveAndDisabledWidths_When_BothHold_Then_TheDisabledWidthWins()
        {
            // Arrange — pressed first, so the active width is the one in place when disabled: arrives.
            var (outer, leaf) = Mount("active:w-[10px] disabled:w-[20px]");
            using (var evt = PointerDownEvent.GetPooled()) leaf.SimulateEvent(evt);

            // Act
            outer.SetEnabled(false);

            // Assert
            Assert.That(leaf.style.width.value.value, Is.EqualTo(20f));
        }

        [Test]
        public void Given_ADisabledInnerUnderDark_When_AnAncestorIsDisabled_Then_TheLeafIsApplied()
        {
            // Arrange — dark: holds the outer gate open; disabled: is the inner.
            var (outer, leaf) = Mount("dark:disabled:bg-hot");
            VelvetTheme.IsDark = true;

            // Act
            outer.SetEnabled(false);

            // Assert
            Assert.That(leaf.ClassListContains("bg-hot"), Is.True);
        }

        // GREEN_ON_BASE(characterization): the engine announcement DisabledVariantSignal reads, which the
        // base engine already makes; this is the case that fails where the engine stops making it.
        [Test]
        public void Given_AnAncestorWrittenOutsideADispatch_When_ItsEnabledSelfIsWritten_Then_TheWriteIsAnnouncedBeforeTheChildIsDisabled()
        {
            // Arrange
            var (outer, leaf) = Mount("bg-hot");
            var announced = false;
            var leafEnabledWhenAnnounced = false;
            var eventType = typeof(VisualElement).Assembly.GetType("UnityEngine.UIElements.PropertyChangedEvent");
            typeof(DisabledVariantTests)
                .GetMethod(nameof(RegisterProbe), BindingFlags.NonPublic | BindingFlags.Static)!
                .MakeGenericMethod(eventType)
                .Invoke(null, new object[]
                {
                    outer, (Action)(() =>
                    {
                        announced = true;
                        leafEnabledWhenAnnounced = leaf.enabledInHierarchy;
                    }),
                });

            // Act
            outer.SetEnabled(false);

            // Assert
            Assert.That((announced, leafEnabledWhenAnnounced), Is.EqualTo((true, true)));
        }
    }
}
