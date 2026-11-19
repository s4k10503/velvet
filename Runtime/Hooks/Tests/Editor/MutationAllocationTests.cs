using System;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies what a mutation's round trip does not allocate: an event for a cache no listener follows, a settling
    /// source for a mutation nothing resumes, and the passive effect of a render whose options equal those the handle
    /// last applied.
    /// </summary>
    [TestFixture]
    [Category("Performance")]
    internal sealed class MutationAllocationTests
    {
        private static readonly Func<TimeSpan> Clock = static () => TimeSpan.Zero;

        private static QueryClient s_client = null!;
        private static MutationOptions<int, int> s_options = null!;
        private static StateUpdater<int> s_rerender;
        private MutationCache _cache = null!;
        private VisualElement _root = null!;

        [SetUp]
        public void SetUp()
        {
            _root = new VisualElement();
            _cache = new MutationCache();
            s_client = new QueryClient();
            s_options = new MutationOptions<int, int>(MutationFn: static (v, _) => VelvetTask.FromResult(v * 2));
            s_rerender = default;
        }

        // A built mutation starts the cache's frame loop, which ends on the first frame after Clear.
        [TearDown]
        public void TearDown()
        {
            _cache.Clear();
            s_client.Clear();
            VelvetTaskFrameDriverTestExtensions.DrainEditorUpdateForTest();
        }

        // GREEN_ON_BASE(refactor): the probe already counts a 16-byte array allocation on the base, which is all this case asserts.
        [Test]
        public void Given_ADelegateAllocatingAKnownArray_When_Probed_Then_TheProbeCountsIt()
        {
            // Act
            var blocks = GCAllocationCanary.BlocksForKnownArray();

            // Assert
            Assert.That(blocks, Is.GreaterThan(0));
        }

        [Test]
        public void Given_ACacheNoListenerFollows_When_AnObserverLeavesAMutationAndRejoinsIt_Then_NothingIsAllocated()
        {
            // Arrange
            var mutation = _cache.Build(new MutationDefaults(), Clock);
            var observer = new MutationObserver(mutation.Options);
            mutation.AddObserver(observer);
            void LeaveAndRejoin()
            {
                mutation.RemoveObserver(observer);
                mutation.AddObserver(observer);
            }
            for (var i = 0; i < 16; i++)
            {
                LeaveAndRejoin();
            }

            // Act
            var blocks = GCAllocationProbe.MedianBlocksDuring(LeaveAndRejoin);

            // Assert
            Assert.That(blocks, Is.EqualTo(0));
        }

        [Test]
        public void Given_AMutationNothingHasResumed_When_Constructed_Then_ItAllocatesNoMoreThanItselfAndItsObserverList()
        {
            // Arrange
            var options = new MutationDefaults();
            void Construct() => GC.KeepAlive(new Mutation(_cache, 1, options, Clock));
            for (var i = 0; i < 16; i++)
            {
                Construct();
            }

            // Act
            var blocks = GCAllocationProbe.MedianBlocksDuring(Construct);

            // Assert
            Assert.That(blocks, Is.LessThanOrEqualTo(2));
        }

        [Test]
        public void Given_AMountedHandle_When_ARenderPassesARecordEqualToTheCommittedOne_Then_ItCostsWhatPassingTheCommittedRecordDoes()
        {
            // Arrange — each record is built outside the window, so the two counts differ only by what the hook does
            // with it.
            using var mounted = V.Mount(_root, V.Component(MutationRender, key: "mutation"));
            mounted.FlushEffectsForTest();
            var committed = s_options;
            void Rerender()
            {
                s_rerender.Invoke(static v => v + 1);
                mounted.FlushStateForTest();
                mounted.FlushEffectsForTest();
            }
            int Blocks(Action pass)
            {
                for (var i = 0; i < 16; i++)
                {
                    pass();
                    Rerender();
                }
                return GCAllocationProbe.MedianBlocksDuring(pass, Rerender);
            }

            // Act — the committed record first, since where an equal record commits, `committed` stops being the
            // one the handle applied.
            var same = Blocks(() => s_options = committed);
            var equal = Blocks(() => s_options = committed with { });

            // Assert — read only over a re-render the probe charged, since two zeros agree whatever the hook does.
            Assert.That(same > 0 ? equal : -1, Is.EqualTo(same));
        }

        [Component(Compiler = false)]
        private static VNode MutationRender()
        {
            var (_, rerender) = Hooks.UseState(0);
            s_rerender = rerender;
            var mutation = Hooks.UseMutation(s_options, s_client);
            return V.Label(text: mutation.Status.ToString());
        }
    }
}
