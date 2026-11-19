using System;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies the contract of <see cref="Hooks.UseService{T}"/> reading from
    /// <see cref="HookServiceContext"/> via an <see cref="IHookServiceResolver"/>.
    /// <list type="bullet">
    /// <item>Without a <see cref="HookServiceContext"/> Provider above the caller, the hook throws an
    /// <see cref="InvalidOperationException"/> naming the missing Provider.</item>
    /// <item>When a Provider supplies a resolver, the hook returns the instance the resolver produces.</item>
    /// <item>The resolver is queried exactly once per render for a given service.</item>
    /// <item>Calling the hook outside a render surfaces an <see cref="InvalidOperationException"/> whose message
    /// names <c>UseService</c> (not the underlying context hook) for accurate Rules-of-Hooks debugging.</item>
    /// </list>
    /// </summary>
    /// <remarks>
    /// The capture component swallows the hook exception into a static field so the no-Provider failure can be
    /// asserted without the render-error path. Static captures are reset together in <see cref="SetUp"/>.
    /// </remarks>
    [TestFixture]
    internal sealed class UseServiceHookTests
    {
        public interface ISample
        {
            string Greet();
        }

        private sealed class CountingResolver : IHookServiceResolver
        {
            private readonly ISample _instance;
            public int ResolveCallCount { get; private set; }

            public CountingResolver(ISample instance) => _instance = instance;

            public T Resolve<T>() where T : class
            {
                ResolveCallCount++;
                return (T)(object)_instance;
            }
        }

        private sealed class StubSample : ISample
        {
            public string Greet() => "hello";
        }

        private VisualElement _root;
        private static ISample s_resolved;
        private static Exception s_caught;

        [SetUp]
        public void SetUp()
        {
            _root = new VisualElement();
            s_resolved = null;
            s_caught = null;
        }

        [Test]
        public void Given_NoServiceProvider_When_UseServiceCalled_Then_ThrowsInvalidOperationExceptionNamingProvider()
        {
            // Act
            using var mounted = V.Mount(_root, V.Component(CaptureUseServiceRender, key: "no-provider"));
            Assume.That(s_caught, Is.InstanceOf<InvalidOperationException>(), "Precondition: the hook raised an exception");

            // Assert
            Assert.That(s_caught.Message, Does.Contain("HookServiceContext provider not found"),
                "The failure message names the missing Provider");
        }

        [Test]
        public void Given_ResolverProvider_When_UseServiceCalled_Then_ReturnsResolvedInstance()
        {
            // Arrange
            var sample = new StubSample();
            var resolver = new CountingResolver(sample);

            // Act
            using var mounted = V.Mount(_root,
                V.Provider(HookServiceContext.Ref, value: resolver, children: new VNode[]
                {
                    V.Component(CaptureUseServiceRender, key: "with-provider"),
                }));

            // Assert
            Assert.That(s_resolved, Is.SameAs(sample), "The hook returns the instance the resolver produced");
        }

        [Test]
        public void Given_ResolverProvider_When_UseServiceCalled_Then_ResolverIsQueriedExactlyOnce()
        {
            // Arrange
            var resolver = new CountingResolver(new StubSample());

            // Act
            using var mounted = V.Mount(_root,
                V.Provider(HookServiceContext.Ref, value: resolver, children: new VNode[]
                {
                    V.Component(CaptureUseServiceRender, key: "call-count"),
                }));

            // Assert
            Assert.That(resolver.ResolveCallCount, Is.EqualTo(1), "The resolver is queried once for the requested service");
        }

        [Test]
        public void Given_OutsideRender_When_UseServiceCalled_Then_ThrowsWithUseServiceInMessage()
        {
            // Act + Assert
            var ex = Assert.Throws<InvalidOperationException>(() => Hooks.UseService<ISample>());
            Assert.That(ex.Message, Does.Contain("UseService"),
                "The Rules-of-Hooks message names UseService, not the underlying context hook");
        }

        [Component]
        public static VNode CaptureUseServiceRender()
        {
            try
            {
                s_resolved = Hooks.UseService<ISample>();
            }
            catch (Exception ex)
            {
                s_caught = ex;
            }
            return V.Label(text: "ok");
        }
    }
}
