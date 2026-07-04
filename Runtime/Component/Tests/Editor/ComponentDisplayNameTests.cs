using System;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies how <c>[Component(DisplayName = "...")]</c> names a component in hook-rule violation
    /// messages.
    /// <list type="bullet">
    /// <item>A non-empty <c>DisplayName</c> overrides the component name embedded in the violation message,
    /// so the message reads <c>{DisplayName}: ...</c>.</item>
    /// <item>An empty <c>DisplayName</c> falls back to the default <c>{DeclaringType}.{MethodName}</c> form.</item>
    /// </list>
    /// </summary>
    /// <remarks>
    /// Uses the <c>[Component] static VNode</c> + <c>V.Mount</c> pattern. Each probe component changes the
    /// type of a UseState slot between renders, which violates the slot-type invariant and raises the named
    /// message. Exceptions thrown from Render() are caught by the renderer and emitted via
    /// <c>Debug.LogException</c>, so the message is asserted with <see cref="LogAssert.Expect(LogType, Regex)"/>
    /// rather than <c>Assert.Throws</c>. Probe state is reset in <see cref="SetUp"/>.
    /// </remarks>
    [TestFixture]
    internal sealed class ComponentDisplayNameTests
    {
        private VisualElement _root;

        [SetUp]
        public void SetUp()
        {
            _root = new VisualElement();
            DisplayNameProbeState.Reset();
        }

        [Test]
        public void Given_ComponentWithDisplayName_When_HookTypeChanges_Then_MessageUsesDisplayName()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(CustomNamedComponent.Render, key: "named"));
            Assume.That(DisplayNameProbeState.SetMode, Is.Not.Null,
                "Precondition: the first render wired SetMode so the hook-type switch can be triggered");
            DisplayNameProbeState.UseIntSlot = false;
            LogAssert.Expect(LogType.Exception, new Regex(@"MyFancyName: UseState type changed"));

            // Act
            DisplayNameProbeState.SetMode.Invoke(true);
            mounted.FlushStateForTest();

            // Assert — LogAssert.Expect verifies the message names the component by its DisplayName
        }

        [Test]
        public void Given_ComponentWithEmptyDisplayName_When_HookTypeChanges_Then_MessageUsesDefaultName()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(EmptyDisplayNameComponent.Render, key: "empty"));
            Assume.That(DisplayNameProbeState.SetMode, Is.Not.Null,
                "Precondition: the first render wired SetMode so the hook-type switch can be triggered");
            DisplayNameProbeState.UseIntSlot = false;
            LogAssert.Expect(LogType.Exception, new Regex(@"EmptyDisplayNameComponent\.Render: UseState type changed"));

            // Act
            DisplayNameProbeState.SetMode.Invoke(true);
            mounted.FlushStateForTest();

            // Assert — LogAssert.Expect verifies the message falls back to DeclaringType.MethodName
        }
    }

    internal static class DisplayNameProbeState
    {
        public static bool UseIntSlot;
        public static Action<bool> SetMode;

        public static void Reset()
        {
            UseIntSlot = true;
            SetMode = null;
        }
    }

    internal static class DisplayNameProbeShared
    {
        public static VNode ProbeBody(string label)
        {
            var (_, setMode) = Hooks.UseState(false);
            DisplayNameProbeState.SetMode = setMode;
            if (DisplayNameProbeState.UseIntSlot)
                Hooks.UseState<int>(0);
            else
                Hooks.UseState<string>("changed");
            return V.Label(text: label);
        }
    }

    internal static class CustomNamedComponent
    {
        [Component(DisplayName = "MyFancyName")]
        public static VNode Render() => DisplayNameProbeShared.ProbeBody("named");
    }

    internal static class EmptyDisplayNameComponent
    {
        [Component(DisplayName = "")]
        public static VNode Render() => DisplayNameProbeShared.ProbeBody("empty");
    }
}
