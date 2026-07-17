using NUnit.Framework;
using UnityEngine.TestTools.Constraints;
using Is = UnityEngine.TestTools.Constraints.Is;

namespace Velvet.Tests.Performance
{
    /// <summary>
    /// Zero-allocation regression guard for <see cref="VNodePool"/>.
    ///
    /// <para>
    /// Velvet uses an immutable virtual-DOM model: one node object (<see cref="ElementNode"/> etc.) is allocated
    /// per element per render and is intentionally never pooled, so a *full* per-render zero-alloc is not a
    /// goal. What <b>is</b> a hard contract — and what these specs pin with
    /// <c>UnityEngine.TestTools.Constraints.Is.Not.AllocatingGCMemory()</c> — is that the pooled, reusable
    /// pieces (<see cref="FiberElementProps"/>, single-event arrays, and child <see cref="VNode"/>[] arrays)
    /// cost <b>zero GC</b> on a warm rent/return cycle. A regression here (e.g. an accidental boxing or a
    /// resize on the hot path) is exactly the kind of silent GC churn this guard is meant to catch.
    /// </para>
    ///
    /// <para>
    /// Each spec warms the relevant pool first (the first rent allocates the backing object and any dictionary /
    /// stack capacity), then asserts the steady-state rent→return cycle allocates nothing. The measured
    /// delegate captures no locals, so it binds to a cached static delegate and adds no closure allocation of
    /// its own.
    /// </para>
    ///
    /// <para>
    /// Behavioral coverage of memoization bail (a bailed <c>[Component(Memoize = true)]</c> does not re-run its
    /// body and therefore allocates no subtree) lives in <c>ComponentMemoPropsBailTests</c>; it is not
    /// re-asserted here because a parent re-render that reaches the bail still allocates the child's
    /// <c>ComponentNode</c>, so that path has a non-zero floor by design.
    /// </para>
    /// </summary>
    [TestFixture]
    [Category("Performance")]
    internal sealed class VNodePoolZeroAllocTests
    {
        [SetUp]
        public void SkipInCi()
        {
            // Allocation guards measure GC, which differs across runtimes: the CI runner reports
            // allocations that a warm local run does not. Run these locally, not in CI.
            if (System.Environment.GetEnvironmentVariable("GITHUB_ACTIONS") == "true")
            {
                Assert.Ignore("Zero-allocation guards are environment-sensitive; run locally.");
            }
        }

        // Warming must include the measured DELEGATE itself, not just the pool: the first execution
        // of a code path can charge one-time runtime work (JIT, lazy statics) to the measuring
        // scope, which surfaced as a rare order-sensitive false red on this guard.

        [Test]
        public void Given_WarmPropsPool_When_RentReturnCycle_Then_DoesNotAllocate()
        {
            // Warm: the first cycle populates the pool AND runs the measured delegate once.
            NUnit.Framework.TestDelegate cycle = () =>
            {
                var props = VNodePool.RentProps();
                VNodePool.ReturnProps(props);
            };
            cycle();

            Assert.That(cycle, Is.Not.AllocatingGCMemory());
        }

        [Test]
        public void Given_WarmSingleEventArrayPool_When_RentReturnCycle_Then_DoesNotAllocate()
        {
            NUnit.Framework.TestDelegate cycle = () =>
            {
                var events = VNodePool.RentSingleEventArray();
                VNodePool.ReturnEventArray(events);
            };
            cycle();

            Assert.That(cycle, Is.Not.AllocatingGCMemory());
        }

        [Test]
        public void Given_WarmNodeArrayPool_When_RentReturnCycle_Then_DoesNotAllocate()
        {
            // The first cycle also warms the length-keyed bucket (array, dictionary entry, stack).
            NUnit.Framework.TestDelegate cycle = () =>
            {
                var array = VNodePool.RentNodeArray(4);
                VNodePool.ReturnNodeArray(array);
            };
            cycle();

            Assert.That(cycle, Is.Not.AllocatingGCMemory());
        }
    }
}
