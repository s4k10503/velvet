using System;
using System.Reflection;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins that a read of RunningStyleAnimations whose delegate threw does not reach that delegate again: the one each
    /// case binds in its place allocates the exception it throws, so a read that still reached it would allocate.
    /// Reached by name, so that the fixture builds on a tree without the type.
    /// </summary>
    internal sealed class RunningStyleAnimationsTests
    {
        private const BindingFlags Hidden = BindingFlags.NonPublic | BindingFlags.Static;

        private static readonly Type s_type = typeof(V).Assembly.GetType("Velvet.RunningStyleAnimations");

        private static bool? s_read;
        private static object s_sink;

        private FieldInfo _replaced;
        private object _bound;
        private int _thrown;

        [TearDown]
        public void TearDown()
        {
            _replaced?.SetValue(null, _bound);
            _replaced = null;
            _bound = null;
            _thrown = 0;
        }

        private static TRead Reader<TRead>(string name) where TRead : Delegate
            => s_type?.GetMethod(name, Hidden) is { } method ? (TRead)Delegate.CreateDelegate(typeof(TRead), method) : null;

        private Exception Thrown()
        {
            _thrown++;
            return new InvalidOperationException();
        }

        // The blocks a read allocates once the delegate under it has thrown on the read before; -1 where there is no
        // delegate to replace, or where the replacement was reached other than once.
        private int BlocksOfAReadAfterAThrow(string field, Delegate throwing, Action read)
        {
            _replaced = s_type?.GetField(field, Hidden);
            if (_replaced == null || read == null)
            {
                return -1;
            }
            _bound = _replaced.GetValue(null);
            _replaced.SetValue(null, throwing);
            read();
            var blocks = GCAllocationProbe.MedianBlocksDuring(read);
            return _thrown == 1 ? blocks : -1;
        }

        [Test]
        public void Given_ARunningAnimationFlagWhoseDelegateThrew_When_ItIsReadAgain_Then_TheReadAllocatesNothing()
        {
            // Arrange
            var any = Reader<Func<VisualElement, bool?>>("Any");
            var element = new VisualElement();
            var canary = GCAllocationProbe.MedianBlocksDuring(() => s_sink = new object());

            // Act
            var blocks = BlocksOfAReadAfterAThrow("s_any", (Func<VisualElement, bool>)(_ => throw Thrown()),
                any == null ? null : () => s_read = any(element));

            // Assert — -1 where the probe sees no allocation at all.
            Assert.That(canary > 0 ? blocks : -1, Is.EqualTo(0));
        }

        [Test]
        public void Given_ARunningAnimationTestWhoseDelegateThrew_When_ItIsReadAgain_Then_TheReadAllocatesNothing()
        {
            // Arrange
            var of = Reader<Func<VisualElement, int, bool?>>("Of");
            var element = new VisualElement();
            var canary = GCAllocationProbe.MedianBlocksDuring(() => s_sink = new object());

            // Act
            var blocks = BlocksOfAReadAfterAThrow("s_of", (Func<VisualElement, int, bool>)((_, _) => throw Thrown()),
                of == null ? null : () => s_read = of(element, 0));

            // Assert — -1 where the probe sees no allocation at all.
            Assert.That(canary > 0 ? blocks : -1, Is.EqualTo(0));
        }
    }
}
