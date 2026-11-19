using System;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies how a <see cref="V.Provider{T}"/> placed directly inside a <see cref="V.AnimatePresence"/>
    /// (the canonical "Provider-as-keyed-entry" pattern) behaves.
    /// <list type="bullet">
    ///   <item>A consumer mounted under the Provider observes the Provider value on initial render.</item>
    ///   <item>Every consumer under the same Provider observes the same value.</item>
    ///   <item>A Provider stack resolves last-in-first-out, so the closest Provider value wins.</item>
    ///   <item>A Fragment between the Provider and its consumer is flattened transparently.</item>
    ///   <item>AnimatePresence is DOM-less, so a Provider keyed entry resolves to one consumer leaf in the parent.</item>
    ///   <item>The Provider inside DOM-less AnimatePresence is transparent — it emits no wrapper element.</item>
    ///   <item>When the Provider value changes between renders, the consumer observes the new value and re-renders.</item>
    ///   <item>When the Provider boundary disappears between renders, patching fails on the type mismatch and
    ///   the consumer is re-mounted observing the outer context default.</item>
    ///   <item>A nested AnimatePresence under a Provider mounts its keyed Component with the outer value pushed.</item>
    ///   <item>Finding the first Motion descendant walks through the transparent Provider wrapper.</item>
    ///   <item>Two Providers patch in place when their Context key matches and require a remount when it differs.</item>
    /// </list>
    /// </summary>
    [TestFixture]
    internal sealed class AnimatePresenceProviderTests
    {
        private static readonly ComponentContext<string> ThemeContext = ComponentContext<string>.Create("light");
        private static readonly ComponentContext<int> CountContext = ComponentContext<int>.Create(0);

        private static string s_lastObservedTheme;
        private static int s_lastObservedCount;
        private static int s_themeReaderRenderCount;
        private static string s_initialTheme;
        private static Action<string> s_setTheme;
        private static bool s_includeProvider;
        private static Action<bool> s_setIncludeProvider;
        private static string s_collectedThemes;

        private VisualElement _root;

        [SetUp]
        public void SetUp()
        {
            _root = new VisualElement();
            s_lastObservedTheme = null;
            s_lastObservedCount = 0;
            s_themeReaderRenderCount = 0;
            s_initialTheme = null;
            s_setTheme = null;
            s_includeProvider = true;
            s_setIncludeProvider = null;
            s_collectedThemes = string.Empty;
        }

        [Component]
        private static VNode ThemeReader()
        {
            var theme = Hooks.UseContext(ThemeContext);
            s_lastObservedTheme = theme;
            s_themeReaderRenderCount++;
            return V.Label(text: theme);
        }

        [Component]
        private static VNode ThemeCollector()
        {
            var theme = Hooks.UseContext(ThemeContext);
            s_collectedThemes += theme + ";";
            return V.Label(text: theme);
        }

        [Component]
        private static VNode ThemePlusCountReader()
        {
            var theme = Hooks.UseContext(ThemeContext);
            var count = Hooks.UseContext(CountContext);
            s_lastObservedTheme = theme;
            s_lastObservedCount = count;
            return V.Label(text: $"{theme}:{count}");
        }

        [Test]
        public void Given_AnimatePresenceWithProviderWrappingComponent_When_Mount_Then_ConsumerObservesProviderValue()
        {
            // Act
            using var mounted = V.Mount(_root,
                V.AnimatePresence(children: new VNode[]
                {
                    V.Provider(ThemeContext, "dark", new VNode[]
                    {
                        V.Component(ThemeReader, key: "reader"),
                    }),
                }));

            // Assert
            Assert.That(s_lastObservedTheme, Is.EqualTo("dark"),
                "Consumer mounted inside a Provider-wrapped AnimatePresence child observes the Provider value");
        }

        [Test]
        public void Given_AnimatePresenceWithProvider_When_Mount_Then_PresenceContainerHoldsOneKeyedEntry()
        {
            // Act
            using var mounted = V.Mount(_root,
                V.AnimatePresence(children: new VNode[]
                {
                    V.Provider(ThemeContext, "dark", new VNode[]
                    {
                        V.Component(ThemeReader, key: "reader"),
                    }),
                }));

            // Assert — DOM-less: AnimatePresence and the Provider are both transparent, so the Provider's
            // single consumer leaf is a direct child of the parent (no presence container, no provider wrapper).
            Assert.That(_root.childCount, Is.EqualTo(1),
                "One keyed Provider entry resolves to one consumer leaf directly in the parent");
        }

        [Test]
        public void Given_AnimatePresenceWithProvider_When_Mount_Then_ProviderIsTransparent()
        {
            // Act
            using var mounted = V.Mount(_root,
                V.AnimatePresence(children: new VNode[]
                {
                    V.Provider(ThemeContext, "dark", new VNode[]
                    {
                        V.Component(ThemeReader, key: "reader"),
                    }),
                }));

            // Assert — DOM-less parity: a Provider inside a DOM-less AnimatePresence is inline-expanded
            // (transparent), emitting no wrapper element; its consumer attaches directly to the parent.
            Assert.That(_root.Q(className: FiberNodeFactory.ContextProviderClassName), Is.Null,
                "A Provider inside DOM-less AnimatePresence emits no wrapper");
        }

        [Test]
        public void Given_ProviderWrappingMultipleConsumers_When_Mount_Then_AllObserveSameValue()
        {
            // Act
            using var mounted = V.Mount(_root,
                V.AnimatePresence(children: new VNode[]
                {
                    V.Provider(ThemeContext, "azure", new VNode[]
                    {
                        V.Component(ThemeCollector, key: "c1"),
                        V.Component(ThemeCollector, key: "c2"),
                    }),
                }));

            // Assert
            Assert.That(s_collectedThemes, Is.EqualTo("azure;azure;"),
                "All consumers inside the same Provider observe the Provider's value");
        }

        [Test]
        public void Given_NestedProviderChainInsideAnimatePresence_When_Mount_Then_InnerValueWins()
        {
            // Act
            using var mounted = V.Mount(_root,
                V.AnimatePresence(children: new VNode[]
                {
                    V.Provider(ThemeContext, "outer", new VNode[]
                    {
                        V.Provider(ThemeContext, "inner", new VNode[]
                        {
                            V.Component(ThemeReader, key: "reader"),
                        }),
                    }),
                }));

            // Assert
            Assert.That(s_lastObservedTheme, Is.EqualTo("inner"),
                "The closest Provider wins: the Provider stack resolves last-in-first-out");
        }

        [Test]
        public void Given_FragmentInsideProviderInsideAnimatePresence_When_Mount_Then_ConsumerObservesValue()
        {
            // Act
            using var mounted = V.Mount(_root,
                V.AnimatePresence(children: new VNode[]
                {
                    V.Provider(ThemeContext, "tea", new VNode[]
                    {
                        V.Fragment(new VNode[]
                        {
                            V.Component(ThemeReader, key: "reader"),
                        }),
                    }),
                }));

            // Assert
            Assert.That(s_lastObservedTheme, Is.EqualTo("tea"),
                "A Fragment inside a Provider is flattened transparently");
        }

        [Component]
        private static VNode AnimatePresenceProviderHost()
        {
            var (theme, setTheme) = Hooks.UseState(s_initialTheme);
            s_setTheme = setTheme;
            return V.AnimatePresence(children: new VNode[]
            {
                V.Provider(ThemeContext, theme, new VNode[]
                {
                    V.Component(ThemeReader, key: "reader"),
                }),
            });
        }

        [Test]
        public void Given_ProviderInsideAnimatePresence_When_ValueChanges_Then_ConsumerObservesNewValue()
        {
            // Arrange
            s_initialTheme = "light";
            using var mounted = V.Mount(_root, V.Component(AnimatePresenceProviderHost, key: "host"));
            Assume.That(s_lastObservedTheme, Is.EqualTo("light"), "Precondition: the consumer first observed the initial value");

            // Act
            s_setTheme.Invoke("dark");
            mounted.FlushStateForTest();

            // Assert
            Assert.That(s_lastObservedTheme, Is.EqualTo("dark"),
                "A consumer inside AnimatePresence's Provider entry observes the new value when the Provider value changes");
        }

        [Test]
        public void Given_ProviderInsideAnimatePresence_When_ValueChanges_Then_ConsumerReRenders()
        {
            // Arrange
            s_initialTheme = "light";
            using var mounted = V.Mount(_root, V.Component(AnimatePresenceProviderHost, key: "host"));
            var initialRenderCount = s_themeReaderRenderCount;

            // Act
            s_setTheme.Invoke("dark");
            mounted.FlushStateForTest();

            // Assert
            Assert.That(s_themeReaderRenderCount, Is.GreaterThan(initialRenderCount),
                "Changing the Provider value re-renders the consumer inside AnimatePresence's keyed entry");
        }

        [Component]
        private static VNode AnimatePresenceConditionalProviderHost()
        {
            var (includeProvider, setIncludeProvider) = Hooks.UseState(s_includeProvider);
            s_setIncludeProvider = setIncludeProvider;
            VNode child = includeProvider
                ? V.Provider(ThemeContext, "boxed", new VNode[]
                {
                    V.Component(ThemeReader, key: "reader"),
                })
                : V.Component(ThemeReader, key: "reader");
            return V.AnimatePresence(children: new VNode[] { child });
        }

        [Test]
        public void Given_ProviderBoundaryDisappearsBetweenRenders_When_Patch_Then_ConsumerRemountsAtOuterContext()
        {
            // Arrange
            s_includeProvider = true;
            using var mounted = V.Mount(_root, V.Component(AnimatePresenceConditionalProviderHost, key: "host"));
            Assume.That(s_lastObservedTheme, Is.EqualTo("boxed"), "Precondition: the consumer first observed the Provider value");

            // Act
            s_setIncludeProvider.Invoke(false);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(s_lastObservedTheme, Is.EqualTo("light"),
                "When the Provider boundary disappears, CanPatch fails (Provider → Component type mismatch) and the consumer is re-mounted observing the outer context default");
        }

        [Test]
        public void Given_NestedAnimatePresenceInsideProvider_When_Mount_Then_InnerObservesOuterProvider()
        {
            // Act
            using var mounted = V.Mount(_root,
                V.AnimatePresence(children: new VNode[]
                {
                    V.Provider(ThemeContext, "ambient", new VNode[]
                    {
                        V.AnimatePresence(children: new VNode[]
                        {
                            V.Component(ThemeReader, key: "inner-reader"),
                        }),
                    }),
                }));

            // Assert
            Assert.That(s_lastObservedTheme, Is.EqualTo("ambient"),
                "Inner AnimatePresence's keyed Component mounts with the outer Provider's context pushed");
        }

        [Test]
        public void Given_ProviderWrappingMotion_When_FindFirstMotionDescendant_Then_ReturnsDescendantMotion()
        {
            // Arrange
            var motion = V.Motion(key: "m", transition: StyleTransition.FadeSlideUp);
            var providerWithMotion = V.Provider(ThemeContext, "v", new VNode[] { motion });

            // Act
            var resolved = FiberNodeFactory.FindFirstMotionDescendant(providerWithMotion);

            // Assert
            Assert.That(resolved, Is.SameAs(motion),
                "FindFirstMotionDescendant walks transparent wrappers (Provider) to surface the descendant Motion, so AnimatePresence's enter/exit dispatch reads Transition + OnEnterComplete from the same node in a single walk");
        }

        [Test]
        public void Given_TwoProvidersWithSameContext_When_CanPatchCompared_Then_Returns_True()
        {
            // Arrange
            var oldProvider = V.Provider(ThemeContext, "old", new VNode[]
            {
                V.Component(ThemeReader, key: "reader"),
            });
            var newProvider = V.Provider(ThemeContext, "new", new VNode[]
            {
                V.Component(ThemeReader, key: "reader"),
            });

            // Act
            var canPatch = ReconcileKeying.CanPatch(oldProvider, newProvider);

            // Assert
            Assert.That(canPatch, Is.True, "Same Context key — patch in place");
        }

        [Test]
        public void Given_TwoProvidersWithDifferentContext_When_CanPatchCompared_Then_Returns_False()
        {
            // Arrange
            var oldProvider = V.Provider(ThemeContext, "v", new VNode[]
            {
                V.Component(ThemeReader, key: "reader"),
            });
            var newProvider = V.Provider(CountContext, 42, new VNode[]
            {
                V.Component(ThemeReader, key: "reader"),
            });

            // Act
            var canPatch = ReconcileKeying.CanPatch(oldProvider, newProvider);

            // Assert
            Assert.That(canPatch, Is.False,
                "Different Context key — remount required so consumer subtree observes the new context environment");
        }
    }
}
