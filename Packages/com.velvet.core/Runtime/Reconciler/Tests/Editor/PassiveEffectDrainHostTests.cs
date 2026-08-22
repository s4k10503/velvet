using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins which element hosts the context-wide passive-effect drain. One scheduled callback serves the
    /// whole context, and the fiber that stages first is the one that registers it — routinely a descendant,
    /// since an outer component without UseEffect stages after the nested one that has it. Hosted on that
    /// fiber's own mount point, the callback stops being delivered as soon as its subtree leaves the panel,
    /// while the context latch stays set and blocks any replacement, so the scheduler stops running passive
    /// effects for the whole context rather than only for the removed subtree. Driven through the real panel
    /// scheduler: FlushEffectsForTest calls the drain directly and so cannot observe where it was registered.
    /// </summary>
    internal sealed class PassiveEffectDrainHostTests
    {
        private HeadlessEditorPanelHost _host;
        private MountedTree _mounted;

        private static StateUpdater<bool> s_setShowTransient;
        private static bool s_survivorEffectRan;

        [SetUp]
        public void SetUp()
        {
            _host = new HeadlessEditorPanelHost();
            s_setShowTransient = default;
            s_survivorEffectRan = false;
        }

        [TearDown]
        public void TearDown()
        {
            _mounted?.Dispose();
            _mounted = null;
            _host?.Dispose();
            _host = null;
        }

        // Rendered ahead of the surviving branch so this fiber stages the mount's first passive effect and
        // is therefore the one that registers the drain; its mount point is the wrapper removed with it.
        [Component]
        private static VNode Transient()
        {
            Hooks.UseEffect(() => () => { }, new object[] { "transient" });
            return V.Label(name: "transient-label");
        }

        [Component]
        private static VNode Survivor()
        {
            Hooks.UseEffect(() =>
            {
                s_survivorEffectRan = true;
                return () => { };
            }, new object[] { "survivor" });
            return V.Label(name: "survivor-label");
        }

        // Both branches carry a key so dropping the transient one detaches the wrapper this case holds a
        // reference to; the positional path would instead patch that wrapper into the surviving branch and
        // detach the trailing element, leaving the reference on-panel and the case asserting nothing.
        [Component]
        private static VNode Host()
        {
            var (showTransient, setShowTransient) = Hooks.UseState(true);
            s_setShowTransient = setShowTransient;
            return V.Div(name: "host", children: new VNode?[]
            {
                showTransient
                    ? V.Div(
                        key: "transient",
                        name: "transient-wrapper",
                        children: new VNode?[] { V.Component(Transient, key: "t") })
                    : null,
                V.Div(
                    key: "survivor",
                    name: "survivor-wrapper",
                    children: new VNode?[] { V.Component(Survivor, key: "s") }),
            });
        }

        [Test]
        public void Given_TheFirstStagingFibersSubtreeDetachesBeforeTheTick_When_ThePanelSchedulerTicks_Then_ASurvivingFibersPassiveEffectRuns()
        {
            // Arrange — mount on a real panel, then drop the transient branch synchronously, before any
            // tick has had the chance to run a passive effect.
            _mounted = V.Mount(_host.Root, V.Component(Host, key: "host"));
            var transientWrapper = _host.Root.Q<VisualElement>("transient-wrapper");
            s_setShowTransient.Invoke(false);
            _mounted.GetSchedulerForTest().DrainImmediateForTest();

            // Act
            EditorPanelTestHelpers.DriveSchedulerOnce(_host.Panel);

            // Assert — the detach is asserted with the effect rather than assumed: a reconcile that left the
            // wrapper attached would not have exercised the detach, and the effect would run either way.
            Assert.That(
                (transientWrapperAttached: transientWrapper.panel != null, survivorEffectRan: s_survivorEffectRan),
                Is.EqualTo((transientWrapperAttached: false, survivorEffectRan: true)));
        }
    }
}
