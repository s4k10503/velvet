using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies where an error goes that a layout effect, a ref setup or a passive effect throws while its commit
    /// runs, as React's captureCommitPhaseError sends it.
    /// <list type="bullet">
    /// <item>The commit goes on: the effects and ref setups after the throwing one, its siblings' among them, still
    /// run.</item>
    /// <item>The nearest boundary above renders its fallback in a follow-up commit, after those, and not inside the
    /// commit that threw.</item>
    /// <item>With no boundary above, the root V.Mount made renders nothing and reports the error through
    /// <see cref="MountOptions.OnUncaughtError"/>, which logs it by default.</item>
    /// </list>
    /// </summary>
    [TestFixture]
    internal sealed class CommitPhaseErrorCaptureTests
    {
        private static readonly List<string> s_log = new();

        private VisualElement _root;

        [SetUp]
        public void SetUp()
        {
            _root = new VisualElement();
            s_log.Clear();
        }

        [Test]
        public void Given_ABoundaryOverTwoSiblingsWhoseFirstLayoutEffectThrows_When_Mounted_Then_TheSecondsLayoutEffectRunsBeforeTheFallbacks()
        {
            // Act
            using var mounted = V.Mount(_root, V.Component(LayoutBoundary, key: "boundary"), CaughtErrors.Unlogged);

            // Assert
            Assert.That(string.Join(", ", s_log), Is.EqualTo("a layout, b layout, fallback layout"));
        }

        [Test]
        public void Given_ABoundaryOverTwoSiblingsWhoseFirstRefSetupThrows_When_Mounted_Then_TheSecondsRefSetupRunsBeforeTheFallbacksLayoutEffect()
        {
            // Act
            using var mounted = V.Mount(_root, V.Component(RefBoundary, key: "boundary"), CaughtErrors.Unlogged);

            // Assert
            Assert.That(string.Join(", ", s_log), Is.EqualTo("a ref, b ref, fallback layout"));
        }

        [Test]
        public void Given_ABoundaryOverTwoSiblingsWhoseFirstPassiveEffectThrows_When_TheEffectsRun_Then_TheSecondsPassiveEffectRunsBeforeTheFallbacksLayoutEffect()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(PassiveBoundary, key: "boundary"), CaughtErrors.Unlogged);

            // Act
            mounted.FlushEffectsForTest();

            // Assert
            Assert.That(string.Join(", ", s_log), Is.EqualTo("a passive, b passive, fallback layout"));
        }

        [Test]
        public void Given_NoBoundary_When_ALayoutEffectThrowsBesideASibling_Then_TheSiblingsLayoutEffectRunsAndTheTargetHoldsNothing()
        {
            // Arrange
            LogAssert.Expect(LogType.Exception, "InvalidOperationException: a boom");

            // Act
            using var mounted = V.Mount(_root, V.Div(children: new VNode[]
            {
                V.Component(ThrowingLayoutRender, "a", key: "a"),
                V.Component(LoggingLayoutRender, "b", key: "b"),
            }));

            // Assert
            Assert.That($"{string.Join(", ", s_log)} | {_root.childCount}", Is.EqualTo("a layout, b layout | 0"));
        }

        [Component(IsErrorBoundary = true)]
        private static VNode LayoutBoundary()
        {
            Hooks.UseFallback(_ => V.Component(FallbackRender, key: "fallback"));
            return V.Fragment(children: new VNode[]
            {
                V.Component(ThrowingLayoutRender, "a", key: "a"),
                V.Component(LoggingLayoutRender, "b", key: "b"),
            });
        }

        [Component(IsErrorBoundary = true)]
        private static VNode RefBoundary()
        {
            Hooks.UseFallback(_ => V.Component(FallbackRender, key: "fallback"));
            return V.Fragment(children: new VNode[]
            {
                V.Component(ThrowingRefRender, key: "a"),
                V.Component(LoggingRefRender, key: "b"),
            });
        }

        [Component(IsErrorBoundary = true)]
        private static VNode PassiveBoundary()
        {
            Hooks.UseFallback(_ => V.Component(FallbackRender, key: "fallback"));
            return V.Fragment(children: new VNode[]
            {
                V.Component(ThrowingPassiveRender, "a", key: "a"),
                V.Component(LoggingPassiveRender, "b", key: "b"),
            });
        }

        [Component]
        private static VNode FallbackRender()
        {
            Hooks.UseLayoutEffect((Func<Action>)(() =>
            {
                s_log.Add("fallback layout");
                return null;
            }), Array.Empty<object>());
            return V.Label(name: "fallback", text: "fallback");
        }

        [Component]
        private static VNode ThrowingLayoutRender(string name)
        {
            Hooks.UseLayoutEffect((Func<Action>)(() =>
            {
                s_log.Add(name + " layout");
                throw new InvalidOperationException(name + " boom");
            }), Array.Empty<object>());
            return V.Label(text: name);
        }

        [Component]
        private static VNode LoggingLayoutRender(string name)
        {
            Hooks.UseLayoutEffect((Func<Action>)(() =>
            {
                s_log.Add(name + " layout");
                return null;
            }), Array.Empty<object>());
            return V.Label(text: name);
        }

        [Component]
        private static VNode ThrowingPassiveRender(string name)
        {
            Hooks.UseEffect((Func<Action>)(() =>
            {
                s_log.Add(name + " passive");
                throw new InvalidOperationException(name + " boom");
            }), Array.Empty<object>());
            return V.Label(text: name);
        }

        [Component]
        private static VNode LoggingPassiveRender(string name)
        {
            Hooks.UseEffect((Func<Action>)(() =>
            {
                s_log.Add(name + " passive");
                return null;
            }), Array.Empty<object>());
            return V.Label(text: name);
        }

        [Component]
        private static VNode ThrowingRefRender()
            => V.Label(text: "a", refCallback: _ =>
            {
                s_log.Add("a ref");
                throw new InvalidOperationException("a boom");
            });

        [Component]
        private static VNode LoggingRefRender()
            => V.Label(text: "b", refCallback: _ =>
            {
                s_log.Add("b ref");
                return null;
            });
    }
}
