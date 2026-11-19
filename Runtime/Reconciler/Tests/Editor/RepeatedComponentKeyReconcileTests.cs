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
    /// <item>Repeated components keep their instances, in order, when an unrelated sibling is inserted ahead of
    /// them and when they change places.</item>
    /// <item>Repeated presence children keep their instances, in order, when an unrelated child is inserted ahead
    /// of them and when they change places.</item>
    /// </list>
    /// </summary>
    [TestFixture]
    internal sealed class RepeatedComponentKeyReconcileTests
    {
        private static readonly ComponentContext<string> ThemeContext = ComponentContext<string>.Create("default");
        private static readonly Dictionary<string, Action<int>> s_setters = new();
        private static readonly Dictionary<string, string> s_seen = new();
        private static bool s_markerThrows;
        private static Action<int> s_setPhase = _ => { };

        private readonly List<string> _warnings = new();
        private VisualElement _root;

        [SetUp]
        public void SetUp()
        {
            _root = new VisualElement();
            s_setters.Clear();
            s_seen.Clear();
            s_markerThrows = false;
            s_setPhase = _ => { };
            _warnings.Clear();
            Application.logMessageReceived += OnLog;
        }

        [TearDown]
        public void TearDown() => Application.logMessageReceived -= OnLog;

        private void OnLog(string message, string stackTrace, LogType type)
        {
            if (type == LogType.Warning) _warnings.Add(message);
        }

        private string LabelTexts() => string.Join(",", _root.Query<Label>().ToList().Select(label => label.text));

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
        private static VNode ScopeHost()
            => V.Div(children: new VNode[]
            {
                V.Component<string>(Reader, "outer-reader", key: "x"),
                V.Div(children: new VNode[]
                {
                    V.Provider(ThemeContext, "inner", new VNode[]
                    {
                        V.Component<string>(Reader, "inner-reader", key: "x"),
                    }),
                }),
            });

        [Component(Compiler = false)]
        private static VNode PresenceFragmentHost()
            => V.Div(children: new VNode[]
            {
                V.Provider(ThemeContext, "outer", new VNode[]
                {
                    V.AnimatePresence(children: new VNode[]
                    {
                        V.Fragment(new VNode[] { V.Component<string>(Reader, "reader", key: "r") }),
                    }),
                }),
            });

        [Component(Compiler = false)]
        private static VNode Holder(string label)
        {
            var (mounted, _) = Hooks.UseState(label);
            return V.Label(text: mounted + ">" + label);
        }

        [Component(Compiler = false)]
        private static VNode ShiftingPresenceHost()
        {
            var (phase, setPhase) = Hooks.UseState(0);
            s_setPhase = n => setPhase.Invoke(n);
            var first = V.Component<string>(Holder, "A" + phase, key: "x");
            var second = V.Component<string>(Holder, "B" + phase, key: "x");
            return V.AnimatePresence(children: phase switch
            {
                0 => new VNode[] { first, second },
                1 => new VNode[] { V.Label(text: "C", key: "y"), first, second },
                _ => new VNode[] { second, first },
            });
        }

        [Component(Compiler = false)]
        private static VNode ShiftingInlineHost()
        {
            var (phase, setPhase) = Hooks.UseState(0);
            s_setPhase = n => setPhase.Invoke(n);
            var first = V.Component<string>(Holder, "A" + phase, key: "x");
            var second = V.Component<string>(Holder, "B" + phase, key: "x");
            return V.Div(children: phase switch
            {
                0 => new VNode[] { first, second },
                1 => new VNode[] { V.Label(text: "C", key: "y"), first, second },
                _ => new VNode[] { second, first },
            });
        }

        [Component(Compiler = false)]
        private static VNode AcrossBoundaryHost()
            => V.Div(children: new VNode[]
            {
                V.Component<string>(Reader, "one", key: "s"),
                V.Component(MarkerBoundary, key: "boundary"),
                V.Component<string>(Reader, "two", key: "s"),
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
        public void Given_ARepeatedKeyUnderAComponentThatRepeatsItsSiblingsKey_When_EachLevelRendersAlone_Then_EachReadsItsNearestProvider()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(NestHost, key: "host"));

            // Act — the outer repeat first, then the repeat inside it; each walk starts from its own spine.
            Bump("b");
            mounted.FlushStateForTest();
            Bump("bb");
            mounted.FlushStateForTest();

            // Assert
            Assert.That($"{s_seen.GetValueOrDefault("b")},{s_seen.GetValueOrDefault("bb")}", Is.EqualTo("outer,inner"));
        }

        // GREEN_ON_BASE(characterization): the base resolves each of these two keys to its own fiber, since they
        // sit in separate containers; this pins that the repeat counting added to the spine does not merge them.
        [Test]
        public void Given_ARepeatedKeyInAnElementBesideAnEarlierComponentOfThatKey_When_ItRendersAlone_Then_ItReadsItsOwnProvider()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(ScopeHost, key: "host"));

            // Act
            Bump("inner-reader");
            mounted.FlushStateForTest();

            // Assert
            Assert.That(s_seen.GetValueOrDefault("inner-reader"), Is.EqualTo("inner"));
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

        [Test]
        public void Given_AFragmentChildOfAPresence_When_ItsComponentRendersAlone_Then_ItReadsTheProvidedValue()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(PresenceFragmentHost, key: "host"));

            // Act
            Bump("reader");
            mounted.FlushStateForTest();

            // Assert
            Assert.That(s_seen.GetValueOrDefault("reader"), Is.EqualTo("outer"));
        }

        [Test]
        public void Given_RepeatedPresenceChildren_When_AnUnrelatedChildIsInsertedAheadOfThem_Then_EachKeepsItsHookState()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(ShiftingPresenceHost, key: "host"));

            // Act
            s_setPhase(1);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(LabelTexts(), Is.EqualTo("C,A0>A1,B0>B1"));
        }

        [Test]
        public void Given_RepeatedPresenceChildren_When_TheyChangePlaces_Then_EachInstanceStaysInItsOccurrence()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(ShiftingPresenceHost, key: "host"));

            // Act
            s_setPhase(2);
            mounted.FlushStateForTest();

            // Assert — the first occurrence is still the instance mounted first, now given the other child's props.
            Assert.That(LabelTexts(), Is.EqualTo("A0>B2,B0>A2"));
        }

        [Test]
        public void Given_RepeatedComponents_When_AnUnrelatedSiblingIsInsertedAheadOfThem_Then_EachKeepsItsHookState()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(ShiftingInlineHost, key: "host"));

            // Act
            s_setPhase(1);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(LabelTexts(), Is.EqualTo("C,A0>A1,B0>B1"));
        }

        [Test]
        public void Given_RepeatedComponents_When_TheyChangePlaces_Then_EachInstanceStaysInItsOccurrence()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(ShiftingInlineHost, key: "host"));

            // Act
            s_setPhase(2);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(LabelTexts(), Is.EqualTo("A0>B2,B0>A2"));
        }

        // GREEN_ON_BASE(characterization): the base reports the repeat across a catching boundary too; this pins
        // that a catch between the repeats does not make the second one go unreported.
        [Test]
        public void Given_ARepeatedKeyAcrossABoundaryThatCatches_When_Mounted_Then_TheRepeatIsStillReported()
        {
            // Arrange
            s_markerThrows = true;

            // Act
            using var mounted = V.Mount(_root, V.Component(AcrossBoundaryHost, key: "host"), CaughtErrors.Unlogged);

            // Assert
            Assert.That(_warnings.Count(warning => warning.Contains("Duplicate component key")), Is.GreaterThan(0));
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
