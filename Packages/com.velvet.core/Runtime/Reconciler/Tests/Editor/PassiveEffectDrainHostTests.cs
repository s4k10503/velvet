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

        private static bool s_survivorEffectRan;

        [SetUp]
        public void SetUp()
        {
            _host = new HeadlessEditorPanelHost();
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
        // is therefore the one that registers the drain; its mount point is the wrapper the case detaches.
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

        // The case detaches the transient wrapper itself: a render dropping it would run the mount's pending passive
        // effects before it rendered (FiberBatchScheduler.Drain), ahead of the tick this case reads.
        [Component]
        private static VNode Host()
            => V.Div(name: "host", children: new VNode?[]
            {
                V.Div(name: "transient-wrapper", children: new VNode?[] { V.Component(Transient, key: "t") }),
                V.Div(name: "survivor-wrapper", children: new VNode?[] { V.Component(Survivor, key: "s") }),
            });

        // GREEN_ON_BASE(characterization): the base hosts the drain on the scheduler's anchor as well.
        [Test]
        public void Given_TheFirstStagingFibersSubtreeDetachesBeforeTheTick_When_ThePanelSchedulerTicks_Then_ASurvivingFibersPassiveEffectRuns()
        {
            // Arrange — mount on a real panel, then take the transient branch off it before any tick has had the
            // chance to run a passive effect.
            _mounted = V.Mount(_host.Root, V.Component(Host, key: "host"));
            _host.Root.Q<VisualElement>("transient-wrapper").RemoveFromHierarchy();

            // Act
            EditorPanelTestHelpers.DriveSchedulerOnce(_host.Panel);

            // Assert
            Assert.That(s_survivorEffectRan, Is.True);
        }
    }
}
