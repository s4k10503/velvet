using System;
using System.Reflection;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies the <c>disabled:</c> variant — its payload holds while the element or an ancestor has
    /// <c>enabledSelf</c> off, the condition UI Toolkit's <c>:disabled</c> pseudo-class matches — and its
    /// <c>group-disabled:</c> / <c>peer-disabled:</c> forms, which read the marked source the same way. Mounted in a
    /// real panel.
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

        // The callbacks for UI Toolkit's internal enabledSelf announcement on element's bubble list, read by
        // reflection: its registry and the event type are both internal. Counting only these matters because
        // the host also carries one pending scheduled item's attach/detach pair for every render, which the
        // EditMode panel scheduler never runs to release: measured, the host's bubble list grew by one such
        // pair at each of the three renders here and kept them after the payload was dropped.
        private static int AnnouncementCallbackCount(VisualElement element)
        {
            var registry = typeof(CallbackEventHandler)
                .GetField("m_CallbackRegistry", BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(element);
            if (registry == null)
            {
                return 0;
            }
            var dynamicList = registry.GetType()
                .GetField("m_BubbleUpCallbacks", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!
                .GetValue(registry)!;
            var list = dynamicList.GetType()
                .GetMethod("GetCallbackListForReading", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!
                .Invoke(dynamicList, null)!;
            var count = (int)list.GetType().GetProperty("Count")!.GetValue(list)!;
            var item = list.GetType().GetProperty("Item")!;
            var eventType = typeof(VisualElement).Assembly.GetType("UnityEngine.UIElements.PropertyChangedEvent")!;
            var typeId = (long)typeof(EventBase<>).MakeGenericType(eventType)
                .GetMethod("TypeId", BindingFlags.Static | BindingFlags.Public)!
                .Invoke(null, null)!;
            var matches = 0;
            for (var i = 0; i < count; i++)
            {
                var functor = item.GetValue(list, new object[] { i })!;
                if ((long)functor.GetType().GetField("eventTypeId")!.GetValue(functor)! == typeId)
                {
                    matches++;
                }
            }
            return matches;
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

            // Assert — the leaf's own :disabled pseudo-state rides along: that is the condition the variant
            // mirrors, so the two are read together.
            Assert.That((leaf.hasDisabledPseudoState, leaf.ClassListContains("bg-hot")), Is.EqualTo((true, true)));
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
        public void Given_ActiveAndDisabledWidths_When_TheDisabledOneIsReleasedWhileStillPressed_Then_TheActiveWidthReturns()
        {
            // Arrange — pressed first, so the active width is the one in place when disabled: arrives.
            var (outer, leaf) = Mount("active:w-[10px] disabled:w-[20px]");
            using (var evt = PointerDownEvent.GetPooled()) leaf.SimulateEvent(evt);
            outer.SetEnabled(false);
            var widthWhileBothHold = leaf.style.width.value.value;

            // Act
            outer.SetEnabled(true);

            // Assert — two layers of their own: the disabled width wins while both hold, and releasing it hands
            // the property back to the active one rather than to the base.
            Assert.That((widthWhileBothHold, leaf.style.width.value.value), Is.EqualTo((20f, 10f)));
        }

        [Test]
        public void Given_ADisabledInnerUnderDark_When_AnAncestorIsDisabled_Then_TheLeafIsApplied()
        {
            // Arrange — dark: holds the outer gate open; disabled: is the inner.
            var (outer, leaf) = Mount("dark:disabled:bg-hot");
            VelvetTheme.IsDark = true;
            var appliedBeforeDisable = leaf.ClassListContains("bg-hot");

            // Act
            outer.SetEnabled(false);

            // Assert — off while only dark holds, so an inner reading anything but the disabled state fails.
            Assert.That((appliedBeforeDisable, leaf.ClassListContains("bg-hot")), Is.EqualTo((false, true)));
        }

        private (VisualElement Group, VisualElement Leaf) MountInGroup(string leafClassName)
        {
            _mounted = V.Mount(_window.rootVisualElement,
                V.Div(name: "group", className: "group", children: new VNode?[]
                {
                    V.Div(name: "leaf", className: leafClassName),
                }));
            var root = _window.rootVisualElement;
            return (root.Q<VisualElement>("group"), root.Q<VisualElement>("leaf"));
        }

        private (VisualElement Peer, VisualElement Leaf) MountAfterPeer(string leafClassName)
        {
            _mounted = V.Mount(_window.rootVisualElement,
                V.Div(children: new VNode?[]
                {
                    V.Div(name: "peer", className: "peer"),
                    V.Div(name: "leaf", className: leafClassName),
                }));
            var root = _window.rootVisualElement;
            return (root.Q<VisualElement>("peer"), root.Q<VisualElement>("leaf"));
        }

        [Test]
        public void Given_AGroupDisabledPayload_When_TheGroupIsDisabled_Then_ThePayloadIsApplied()
        {
            // Arrange
            var (group, leaf) = MountInGroup("group-disabled:bg-hot");

            // Act
            group.SetEnabled(false);

            // Assert
            Assert.That(leaf.ClassListContains("bg-hot"), Is.True);
        }

        [Test]
        public void Given_APeerDisabledPayload_When_ThePeerIsDisabled_Then_ThePayloadIsAppliedToTheEnabledSibling()
        {
            // Arrange
            var (peer, leaf) = MountAfterPeer("peer-disabled:bg-hot");

            // Act
            peer.SetEnabled(false);

            // Assert — the sibling stays enabled, so only the peer's state can have put the payload there.
            Assert.That((leaf.enabledInHierarchy, leaf.ClassListContains("bg-hot")), Is.EqualTo((true, true)));
        }

        [Test]
        public void Given_APeerDisabledPayloadApplied_When_ThePeerIsEnabledAgain_Then_ThePayloadIsRemoved()
        {
            // Arrange
            var (peer, leaf) = MountAfterPeer("peer-disabled:bg-hot");
            peer.SetEnabled(false);
            var appliedWhileDisabled = leaf.ClassListContains("bg-hot");

            // Act
            peer.SetEnabled(true);

            // Assert
            Assert.That((appliedWhileDisabled, leaf.ClassListContains("bg-hot")), Is.EqualTo((true, false)));
        }

        [Test]
        public void Given_GroupActiveAndGroupDisabledWidths_When_TheDisabledOneIsReleasedWhileStillPressed_Then_TheActiveWidthReturns()
        {
            // Arrange — the group is pressed first, so its active width is in place when the disabled one arrives.
            var (group, leaf) = MountInGroup("group-active:w-[10px] group-disabled:w-[20px]");
            using (var evt = PointerDownEvent.GetPooled()) group.SimulateEvent(evt);
            group.SetEnabled(false);
            var widthWhileBothHold = leaf.style.width.value.value;

            // Act
            group.SetEnabled(true);

            // Assert
            Assert.That((widthWhileBothHold, leaf.style.width.value.value), Is.EqualTo((20f, 10f)));
        }

        [Test]
        public void Given_APeerDisabledInnerUnderDark_When_ThePeerIsDisabled_Then_TheLeafIsApplied()
        {
            // Arrange — dark: holds the outer gate open; peer-disabled: is the inner.
            var (peer, leaf) = MountAfterPeer("dark:peer-disabled:bg-hot");
            VelvetTheme.IsDark = true;
            var appliedBeforeDisable = leaf.ClassListContains("bg-hot");

            // Act
            peer.SetEnabled(false);

            // Assert
            Assert.That((appliedBeforeDisable, leaf.ClassListContains("bg-hot")), Is.EqualTo((false, true)));
        }

        [Test]
        public void Given_ADisabledHoverPayloadUnderThePointer_When_AClickHandlerDisablesTheElement_Then_TheLeafIsApplied()
        {
            // Arrange — the pointer already rests on the element; nothing re-sends PointerOver when it is
            // disabled, since the element under the pointer does not change.
            var (_, leaf) = Mount("disabled:hover:bg-hot");
            using (var over = PointerOverEvent.GetPooled()) leaf.SimulateEvent(over);
            leaf.RegisterCallback<ClickEvent>(_ => leaf.SetEnabled(false));

            // Act — dispatched through the panel, so the write lands inside a real event dispatch.
            using (var click = ClickEvent.GetPooled())
            {
                click.target = leaf;
                leaf.SendEvent(click);
            }

            // Assert
            Assert.That(leaf.ClassListContains("bg-hot"), Is.True);
        }

        [Test]
        public void Given_ADisabledPayload_When_AClickHandlerDisablesTheAncestor_Then_ThePayloadIsApplied()
        {
            // Arrange
            var (outer, leaf) = Mount("disabled:bg-hot");
            leaf.RegisterCallback<ClickEvent>(_ => outer.SetEnabled(false));

            // Act — inside the dispatch the announcement is queued behind it, so it arrives with the ancestor
            // already disabled.
            using (var click = ClickEvent.GetPooled())
            {
                click.target = leaf;
                leaf.SendEvent(click);
            }

            // Assert
            Assert.That(leaf.ClassListContains("bg-hot"), Is.True);
        }

        [Test]
        public void Given_ADisabledPayloadUnderAHost_When_ARenderDropsIt_Then_TheHostKeepsNoRegistrationOfIt()
        {
            // Arrange
            var host = new VisualElement();
            _window.rootVisualElement.Add(host);
            s_initialClass = "bg-cold";
            _mounted = V.Mount(host, V.Component(RenderSwitchable));
            var before = AnnouncementCallbackCount(host);
            s_setClass.Invoke("disabled:bg-hot");
            _mounted.Root.Reconciler.Context.BatchScheduler.DrainImmediateForTest();
            var whileWatched = AnnouncementCallbackCount(host);

            // Act
            s_setClass.Invoke("bg-cold");
            _mounted.Root.Reconciler.Context.BatchScheduler.DrainImmediateForTest();

            // Assert — the count while watched rides along, so a host that was never registered on cannot read
            // as released.
            Assert.That((whileWatched > before, AnnouncementCallbackCount(host) == before), Is.EqualTo((true, true)));
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
