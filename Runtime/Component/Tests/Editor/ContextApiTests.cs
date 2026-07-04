using System;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies the contract of the context API: <see cref="ComponentContext{T}"/> +
    /// <see cref="V.Provider"/> + <see cref="Hooks.UseContext"/>.
    /// <list type="bullet">
    /// <item><see cref="Hooks.UseContext"/> returns the context's default value when no enclosing Provider
    /// supplies one, and the provided value when a Provider is present above the consumer.</item>
    /// <item>A Provider value propagates into a consumer nested arbitrarily deep, across element subtrees
    /// and across the inner reconcile boundary of intermediate components.</item>
    /// <item>When a Provider value changes, every consumer reading it re-renders with the new value,
    /// including consumers nested below intermediate components.</item>
    /// <item>With nested Providers of the same context, the closest enclosing Provider value masks the
    /// outer one.</item>
    /// <item>Providers of distinct context types are independent: a consumer reads each type's value
    /// without interference, and a multi-context consumer sees all provided values.</item>
    /// <item>A Provider inline-expands its children with no wrapper element, so mixed element and component
    /// children land directly under the mount point in order.</item>
    /// <item><see cref="ComponentContext{T}.Create"/> without an explicit default uses the type's default
    /// (<c>0</c> for int, <c>null</c> for string), exposed as <see cref="ComponentContext{T}.DefaultValue"/>.</item>
    /// <item>A wrapper-emitting node (such as Suspense) between a Provider and a consumer does not break
    /// propagation: the consumer still observes the Provider value.</item>
    /// </list>
    /// </summary>
    /// <remarks>
    /// Uses the <c>[Component] static VNode</c> + <c>V.Mount</c> + static-field exposure pattern; the leaf
    /// consumer records the last value it read into a static field. Per-region static fields are reset in
    /// <see cref="SetUp"/> via <c>Reset{Region}()</c> helpers.
    /// </remarks>
    [TestFixture]
    internal sealed class ContextApiTests
    {
        private static readonly ComponentContext<string> ThemeContext = ComponentContext<string>.Create("light");
        private static readonly ComponentContext<int> CountContext = ComponentContext<int>.Create(0);

        private VisualElement _root;

        [SetUp]
        public void SetUp()
        {
            _root = new VisualElement();
            ResetThemeDisplay();
            ResetSingleTheme();
            ResetUpdatableTheme();
            ResetUpdatableNested();
            ResetNestedTheme();
            ResetMultiContext();
            ResetTypeSafety();
            ResetMixedChildren();
            ResetSuspenseProvider();
        }

        [Test]
        public void Given_NoProvider_When_ConsumerReadsContext_Then_ReturnsDefaultValue()
        {
            // Act
            using var mounted = V.Mount(_root, V.Component(ThemeDisplayRender, key: "display"));

            // Assert
            Assert.That(s_themeDisplayLastRendered, Is.EqualTo("light"),
                "UseContext returns the context default when no Provider is set");
        }

        [Test]
        public void Given_Provider_When_ConsumerReadsContext_Then_ReturnsProvidedValue()
        {
            // Arrange
            s_singleThemeValue = "dark";

            // Act
            using var mounted = V.Mount(_root, V.Component(SingleThemeProviderRender, key: "host"));

            // Assert
            Assert.That(s_themeDisplayLastRendered, Is.EqualTo("dark"),
                "UseContext returns the enclosing Provider value");
        }

        [Test]
        public void Given_OuterProvider_When_ConsumerNestedBelowIntermediateComponent_Then_PropagatesValue()
        {
            // Act
            using var mounted = V.Mount(_root,
                V.Provider(ThemeContext, "olive", new VNode[]
                {
                    V.Component(IntermediateContainerRender, key: "intermediate"),
                }));

            // Assert
            Assert.That(s_themeDisplayLastRendered, Is.EqualTo("olive"),
                "An outer Provider value propagates into a consumer nested below an intermediate component");
        }

        [Test]
        public void Given_OuterProviderValueChanges_When_ConsumerNestedBelowIntermediateComponent_Then_PropagatesNewValue()
        {
            // Arrange
            s_updatableNestedInitial = "olive";
            using var mounted = V.Mount(_root, V.Component(UpdatableNestedProviderRender, key: "host"));
            Assume.That(s_themeDisplayLastRendered, Is.EqualTo("olive"),
                "Precondition: the nested consumer reads the initial Provider value on mount");

            // Act
            s_updatableNestedSetTheme.Invoke("teal");
            mounted.FlushStateForTest();

            // Assert
            Assert.That(s_themeDisplayLastRendered, Is.EqualTo("teal"),
                "A Provider value change propagates into the nested consumer");
        }

        [Test]
        public void Given_NestedProvidersOfSameContext_When_ConsumerReads_Then_NearestValueWins()
        {
            // Arrange
            s_nestedOuter = "dark";
            s_nestedInner = "blue";

            // Act
            using var mounted = V.Mount(_root, V.Component(NestedThemeProviderRender, key: "host"));

            // Assert
            Assert.That(s_themeDisplayLastRendered, Is.EqualTo("blue"),
                "The closest enclosing Provider value masks the outer one");
        }

        [Test]
        public void Given_ProviderValueChanges_When_DirectConsumerReads_Then_ConsumerReRendersWithNewValue()
        {
            // Arrange
            s_updatableInitialTheme = "light";
            using var mounted = V.Mount(_root, V.Component(UpdatableThemeProviderRender, key: "host"));
            Assume.That(s_themeDisplayLastRendered, Is.EqualTo("light"),
                "Precondition: the consumer reads the initial value on mount");

            // Act
            s_updatableSetTheme.Invoke("dark");
            mounted.FlushStateForTest();

            // Assert
            Assert.That(s_themeDisplayLastRendered, Is.EqualTo("dark"),
                "A consumer re-renders with the new value after a Provider value change");
        }

        [Test]
        public void Given_TwoContextProviders_When_MultiConsumerReadsBoth_Then_SeesEachValue()
        {
            // Arrange
            s_multiTheme = "dark";
            s_multiCount = 42;
            using var mounted = V.Mount(_root, V.Component(MultiContextProviderRender, key: "host"));

            // Act
            var label = _root.Q<Label>();

            // Assert
            Assert.That(label?.text, Is.EqualTo("dark:42"),
                "A consumer reading two contexts sees the value provided for each");
        }

        [Test]
        public void Given_DistinctContextTypes_When_SeparateConsumersRead_Then_ValuesDoNotInterfere()
        {
            // Arrange
            s_typeSafetyTheme = "red";
            s_typeSafetyCount = 99;
            using var mounted = V.Mount(_root, V.Component(TypeSafetyProviderRender, key: "host"));

            // Act
            var labels = _root.Query<Label>().ToList();
            Assume.That(labels.Count, Is.EqualTo(2), "Precondition: both consumers rendered a label");

            // Assert
            Assert.That(
                (labels[0].text, labels[1].text),
                Is.EqualTo(("red", "99")),
                "Each consumer reads only its own context type's value");
        }

        [Test]
        public void Given_ProviderWithMixedChildren_When_Mounted_Then_AllChildrenLandUnderMountPointInOrder()
        {
            // Arrange
            s_mixedChildrenTheme = "ocean";

            // Act
            using var mounted = V.Mount(_root, V.Component(MixedChildrenProviderRender, key: "host"));

            // Assert
            Assume.That(_root.childCount, Is.EqualTo(3),
                "Precondition: the wrapper-less host and Provider place exactly the three children under the root");
            var header = _root.ElementAt(0) as Label;
            var footer = _root.ElementAt(2) as Label;
            Assume.That(header, Is.Not.Null, "Precondition: the first child is the header Label");
            Assume.That(footer, Is.Not.Null, "Precondition: the third child is the footer Label");
            Assert.That(
                (header.text, footer.text),
                Is.EqualTo(("Header", "Footer")),
                "The element children sit directly under the root in their declared order");
        }

        [Test]
        public void Given_ContextCreatedWithoutDefault_When_Inspected_Then_DefaultValueIsTypeDefault()
        {
            // Arrange
            var ctxInt = ComponentContext<int>.Create();
            var ctxStr = ComponentContext<string>.Create();

            // Act + Assert
            Assert.That(
                (ctxInt.DefaultValue, ctxStr.DefaultValue),
                Is.EqualTo((0, (string)null)),
                "Create without an explicit default uses the type default (0 for int, null for string)");
        }

        [Test]
        public void Given_ProviderWrapsSuspenseWrapsConsumer_When_Mounted_Then_ConsumerReadsProviderValue()
        {
            // Arrange
            s_suspenseProviderTheme = "forest";

            // Act
            using var mounted = V.Mount(_root, V.Component(SuspenseProviderHostRender, key: "host"));

            // Assert
            Assert.That(s_themeDisplayLastRendered, Is.EqualTo("forest"),
                "A wrapper-emitting Suspense node between the Provider and consumer does not break propagation");
        }

        #region Consumer components (leaf that calls UseContext)

        private static string s_themeDisplayLastRendered;

        private static void ResetThemeDisplay()
        {
            s_themeDisplayLastRendered = null;
        }

        [Component]
        private static VNode ThemeDisplayRender()
        {
            var theme = Hooks.UseContext(ThemeContext);
            s_themeDisplayLastRendered = theme;
            return V.Label(text: theme);
        }

        [Component]
        private static VNode CountDisplayRender()
        {
            var count = Hooks.UseContext(CountContext);
            return V.Label(text: count.ToString());
        }

        [Component]
        private static VNode MultiContextRender()
        {
            var theme = Hooks.UseContext(ThemeContext);
            var count = Hooks.UseContext(CountContext);
            return V.Label(text: $"{theme}:{count}");
        }

        #endregion

        #region SingleTheme provider host

        private static string s_singleThemeValue;

        private static void ResetSingleTheme()
        {
            s_singleThemeValue = null;
        }

        [Component]
        private static VNode SingleThemeProviderRender()
            => V.Provider(ThemeContext, s_singleThemeValue, new VNode[]
            {
                V.Component(ThemeDisplayRender, key: "display"),
            });

        // Intermediate component that nests the consumer below an element without a Provider, exercising
        // propagation across the inner reconcile boundary.
        [Component]
        private static VNode IntermediateContainerRender()
            => V.Div(
                name: "intermediate-container",
                children: new VNode[]
                {
                    V.Component(ThemeDisplayRender, key: "display"),
                });

        #endregion

        #region UpdatableTheme provider host

        private static string s_updatableInitialTheme;
        private static Action<string> s_updatableSetTheme;

        private static void ResetUpdatableTheme()
        {
            s_updatableInitialTheme = null;
            s_updatableSetTheme = null;
        }

        [Component]
        private static VNode UpdatableThemeProviderRender()
        {
            var (theme, setTheme) = Hooks.UseState(s_updatableInitialTheme);
            s_updatableSetTheme = setTheme;
            return V.Provider(ThemeContext, theme, new VNode[]
            {
                V.Component(ThemeDisplayRender, key: "display"),
            });
        }

        #endregion

        #region UpdatableNested provider host (outer Provider + nested consumer)

        private static string s_updatableNestedInitial;
        private static Action<string> s_updatableNestedSetTheme;

        private static void ResetUpdatableNested()
        {
            s_updatableNestedInitial = null;
            s_updatableNestedSetTheme = null;
        }

        [Component]
        private static VNode UpdatableNestedProviderRender()
        {
            var (theme, setTheme) = Hooks.UseState(s_updatableNestedInitial);
            s_updatableNestedSetTheme = setTheme;
            return V.Provider(ThemeContext, theme, new VNode[]
            {
                V.Component(IntermediateContainerRender, key: "intermediate"),
            });
        }

        #endregion

        #region NestedTheme provider host (outer + inner Provider)

        private static string s_nestedOuter;
        private static string s_nestedInner;

        private static void ResetNestedTheme()
        {
            s_nestedOuter = null;
            s_nestedInner = null;
        }

        [Component]
        private static VNode NestedThemeProviderRender()
            => V.Provider(ThemeContext, s_nestedOuter, new VNode[]
            {
                V.Provider(ThemeContext, s_nestedInner, new VNode[]
                {
                    V.Component(ThemeDisplayRender, key: "display"),
                }),
            });

        #endregion

        #region MultiContext provider host (Theme + Count + MultiContextRender)

        private static string s_multiTheme;
        private static int s_multiCount;

        private static void ResetMultiContext()
        {
            s_multiTheme = null;
            s_multiCount = 0;
        }

        [Component]
        private static VNode MultiContextProviderRender()
            => V.Provider(ThemeContext, s_multiTheme, new VNode[]
            {
                V.Provider(CountContext, s_multiCount, new VNode[]
                {
                    V.Component(MultiContextRender, key: "multi"),
                }),
            });

        #endregion

        #region TypeSafety provider host (Theme + Count + 2 separate consumers)

        private static string s_typeSafetyTheme;
        private static int s_typeSafetyCount;

        private static void ResetTypeSafety()
        {
            s_typeSafetyTheme = null;
            s_typeSafetyCount = 0;
        }

        [Component]
        private static VNode TypeSafetyProviderRender()
            => V.Provider(ThemeContext, s_typeSafetyTheme, new VNode[]
            {
                V.Provider(CountContext, s_typeSafetyCount, new VNode[]
                {
                    V.Component(ThemeDisplayRender, key: "theme"),
                    V.Component(CountDisplayRender, key: "count"),
                }),
            });

        #endregion

        #region MixedChildren provider host (Label + Component + Label)

        private static string s_mixedChildrenTheme;

        private static void ResetMixedChildren()
        {
            s_mixedChildrenTheme = null;
        }

        [Component]
        private static VNode MixedChildrenProviderRender()
            => V.Provider(ThemeContext, s_mixedChildrenTheme, new VNode[]
            {
                V.Label(text: "Header"),
                V.Component(ThemeDisplayRender, key: "theme"),
                V.Label(text: "Footer"),
            });

        #endregion

        #region SuspenseProvider host (Provider wraps Suspense wraps Component)

        private static string s_suspenseProviderTheme;

        private static void ResetSuspenseProvider()
        {
            s_suspenseProviderTheme = null;
        }

        [Component]
        private static VNode SuspenseProviderHostRender()
            => V.Provider(ThemeContext, s_suspenseProviderTheme, new VNode[]
            {
                V.Suspense(
                    fallback: V.Label(text: "loading"),
                    children: new VNode[]
                    {
                        V.Component(ThemeDisplayRender, key: "inner"),
                    }),
            });

        #endregion
    }
}
