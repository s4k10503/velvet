using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies how a component that renders alone finds the context around it when a sibling shares its
    /// key, and what an error boundary's fallback counts of the key its failed output took.
    /// <list type="bullet">
    /// <item>A repeat of a key reads the Provider enclosing it, whether it is the second of two siblings, comes
    /// after an element, or sits under a component that is itself a repeat of its sibling's key.</item>
    /// <item>A child of an AnimatePresence reads the Provider around the presence when an unkeyed Fragment
    /// stands ahead of it.</item>
    /// <item>A fallback that uses a key its failed output also used is not reported as a repeat.</item>
    /// <item>An AnimatePresence whose children have distinct keys reports no repeat.</item>
    /// </list>
    /// </summary>
    [TestFixture]
    internal sealed class RepeatedComponentKeyReconcileTests
    {
        private static readonly ComponentContext<string> ThemeContext = ComponentContext<string>.Create("default");
        private static readonly Dictionary<string, Action<int>> s_setters = new();
        private static readonly Dictionary<string, string> s_seen = new();
        private static bool s_markerThrows;

        private readonly List<string> _warnings = new();
        private VisualElement _root;

        [SetUp]
        public void SetUp()
        {
            _root = new VisualElement();
            s_setters.Clear();
            s_seen.Clear();
            s_markerThrows = false;
            _warnings.Clear();
            Application.logMessageReceived += OnLog;
        }

        [TearDown]
        public void TearDown() => Application.logMessageReceived -= OnLog;

        private void OnLog(string message, string stackTrace, LogType type)
        {
            if (type == LogType.Warning) _warnings.Add(message);
        }

        private static void Bump(string who)
        {
            s_seen.Remove(who);
            if (s_setters.TryGetValue(who, out var set)) set(1);
        }

        [Component(Compiler = false)]
        private static VNode Reader(string who)
        {
            var (_, set) = Hooks.UseState(0);
            s_setters[who] = n => set.Invoke(n);
            s_seen[who] = Hooks.UseContext(ThemeContext);
            return V.Label(text: who);
        }

        [Component(Compiler = false)]
        private static VNode PairHost()
            => V.Div(children: new VNode[]
            {
                V.Provider(ThemeContext, "outer", new VNode[]
                {
                    V.Component<string>(Reader, "first", key: "x"),
                    V.Component<string>(Reader, "second", key: "x"),
                }),
            });

        [Component(Compiler = false)]
        private static VNode PairAfterElementHost()
            => V.Div(children: new VNode[]
            {
                V.Provider(ThemeContext, "outer", new VNode[]
                {
                    V.Component<string>(Reader, "first", key: "x"),
                    V.Div(children: new VNode[] { V.Label(text: "between") }),
                    V.Component<string>(Reader, "second", key: "x"),
                }),
            });

        [Component(Compiler = false)]
        private static VNode Nest(string path)
        {
            var (_, set) = Hooks.UseState(0);
            s_setters[path] = n => set.Invoke(n);
            s_seen[path] = Hooks.UseContext(ThemeContext);
            if (path.Length >= 2) return V.Label(text: path);
            return V.Provider(ThemeContext, "inner", new VNode[]
            {
                V.Component<string>(Nest, path + "a", key: "x"),
                V.Component<string>(Nest, path + "b", key: "x"),
            });
        }

        [Component(Compiler = false)]
        private static VNode NestHost()
            => V.Provider(ThemeContext, "outer", new VNode[]
            {
                V.Component<string>(Nest, "a", key: "x"),
                V.Component<string>(Nest, "b", key: "x"),
            });

        [Component(Compiler = false)]
        private static VNode PresenceHost()
            => V.Div(children: new VNode[]
            {
                V.Provider(ThemeContext, "outer", new VNode[]
                {
                    V.AnimatePresence(children: new VNode[]
                    {
                        V.Fragment(new VNode[] { V.Label(text: "fragment") }),
                        V.Component<string>(Reader, "reader"),
                    }),
                }),
            });

        [Component(Compiler = false)]
        private static VNode Marker(string who)
        {
            if (s_markerThrows)
            {
                s_markerThrows = false;
                throw new InvalidOperationException("Marker threw once");
            }
            return V.Label(text: who);
        }

        [Component(Compiler = false, IsErrorBoundary = true)]
        private static VNode MarkerBoundary()
        {
            Hooks.UseFallback(_ => V.Component<string>(Marker, "fallback", key: "k"));
            return V.Component<string>(Marker, "failed", key: "k");
        }

        [Component(Compiler = false)]
        private static VNode MarkerBoundaryHost()
            => V.Div(children: new VNode[] { V.Component(MarkerBoundary, key: "boundary") });

        [Test]
        public void Given_TwoSiblingsWithTheSameKeyUnderAProvider_When_TheSecondRendersAlone_Then_ItReadsTheProvidedValue()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(PairHost, key: "host"));

            // Act
            Bump("second");
            mounted.FlushStateForTest();

            // Assert
            Assert.That(s_seen.GetValueOrDefault("second"), Is.EqualTo("outer"));
        }

        [Test]
        public void Given_ARepeatedKeyAfterAnElement_When_TheRepeatRendersAlone_Then_ItReadsTheProvidedValue()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(PairAfterElementHost, key: "host"));

            // Act
            Bump("second");
            mounted.FlushStateForTest();

            // Assert
            Assert.That(s_seen.GetValueOrDefault("second"), Is.EqualTo("outer"));
        }

        [Test]
        public void Given_ARepeatedKeyUnderAComponentThatRepeatsItsSiblingsKey_When_TheInnerRepeatRendersAlone_Then_ItReadsTheNearestProvider()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(NestHost, key: "host"));

            // Act
            Bump("bb");
            mounted.FlushStateForTest();

            // Assert
            Assert.That(s_seen.GetValueOrDefault("bb"), Is.EqualTo("inner"));
        }

        [Test]
        public void Given_AnUnkeyedFragmentAheadOfAPresenceChild_When_TheChildRendersAlone_Then_ItReadsTheProvidedValue()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(PresenceHost, key: "host"));

            // Act
            Bump("reader");
            mounted.FlushStateForTest();

            // Assert
            Assert.That(s_seen.GetValueOrDefault("reader"), Is.EqualTo("outer"));
        }

        // GREEN_ON_BASE(characterization): the base reports no repeat for this arrangement either; this pins that
        // it stays unreported now that repeats are counted per walk.
        [Test]
        public void Given_AFallbackUsingTheKeyItsFailedOutputUsed_When_TheBoundaryCatches_Then_NoRepeatIsReported()
        {
            // Arrange
            s_markerThrows = true;

            // Act
            using var mounted = V.Mount(_root, V.Component(MarkerBoundaryHost, key: "host"), CaughtErrors.Unlogged);

            // Assert
            Assert.That(_warnings.Count(warning => warning.Contains("Duplicate component key")), Is.EqualTo(0));
        }

        // GREEN_ON_BASE(characterization): the base reports nothing for distinct keys either; this pins that the
        // repeat report added for a presence child stays silent when no key repeats.
        [Test]
        public void Given_PresenceChildrenWithDistinctKeys_When_Mounted_Then_NoRepeatIsReported()
        {
            // Arrange
            var tree = V.AnimatePresence(children: new VNode[]
            {
                V.Label(text: "a", key: "a"),
                V.Label(text: "b", key: "b"),
            });

            // Act
            using var mounted = V.Mount(_root, tree);

            // Assert
            Assert.That(_warnings.Count(warning => warning.Contains("Both children render")), Is.EqualTo(0));
        }
    }
}
