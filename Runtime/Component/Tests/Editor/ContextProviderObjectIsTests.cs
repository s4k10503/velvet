using System;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies how a Provider decides whether a new value is a change, and the underlying
    /// <see cref="ObjectIs"/> equality predicate it relies on.
    /// <list type="bullet">
    /// <item>A Provider notifies its consumers only when the new value is not equal to the previous one
    /// under <see cref="ObjectIs"/>: a fresh reference-type instance with identical content counts as a
    /// change, while reapplying the exact same reference is a no-op.</item>
    /// <item>Equality is identity-based, not structural — a record's value semantics do not make two
    /// distinct instances equal for Provider change detection.</item>
    /// <item>Floating-point values compare by raw bit pattern: <c>NaN</c> equals itself (replacing NaN with
    /// NaN is a no-op) and <c>+0</c> does not equal <c>-0</c> (a sign flip of zero is a change).</item>
    /// <item><see cref="ObjectIs.AreEqual{T}"/> compares reference types (and null) by reference, float and
    /// double by raw bits, and other value types by their default equality so a boxed primitive is not
    /// treated as a fresh identity on every call.</item>
    /// </list>
    /// </summary>
    /// <remarks>
    /// Provider-propagation clauses use the <c>[Component] static VNode</c> + <c>V.Mount</c> +
    /// static-field exposure pattern, counting consumer renders to observe whether a change propagated.
    /// Per-region static fields are reset in <see cref="SetUp"/>. The predicate-level clauses call
    /// <see cref="ObjectIs"/> directly.
    /// </remarks>
    [TestFixture]
    internal sealed class ContextProviderObjectIsTests
    {
        private sealed record ThemeRecord(string Name);

        private static readonly ComponentContext<ThemeRecord> ThemeRecordContext =
            ComponentContext<ThemeRecord>.Create(new ThemeRecord("default"));

        private static readonly ComponentContext<float> FloatContext =
            ComponentContext<float>.Create(0f);

        private VisualElement _root;

        [SetUp]
        public void SetUp()
        {
            _root = new VisualElement();
            ResetRecordHost();
            ResetFloatHost();
        }

        #region Provider change detection

        [Test]
        public void Given_RecordProvider_When_NewInstanceWithSameContent_Then_ConsumerReRenders()
        {
            // Arrange
            s_recordInitial = new ThemeRecord("dark");
            using var mounted = V.Mount(_root, V.Component(RecordProviderHostRender, key: "host"));
            Assume.That(s_recordRenderCount, Is.EqualTo(1), "Precondition: the consumer rendered once on mount");

            // Act
            s_recordSet.Invoke(new ThemeRecord("dark"));
            mounted.FlushStateForTest();

            // Assert
            Assert.That(s_recordRenderCount, Is.EqualTo(2),
                "A new reference-type instance with identical content propagates as a change (identity, not structure)");
        }

        [Test]
        public void Given_RecordProvider_When_SameReferenceReapplied_Then_ConsumerDoesNotReRender()
        {
            // Arrange
            var pinned = new ThemeRecord("dark");
            s_recordInitial = pinned;
            using var mounted = V.Mount(_root, V.Component(RecordProviderHostRender, key: "host"));
            Assume.That(s_recordRenderCount, Is.EqualTo(1), "Precondition: the consumer rendered once on mount");

            // Act
            s_recordSet.Invoke(pinned);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(s_recordRenderCount, Is.EqualTo(1),
                "Reapplying the same reference is a no-op and skips consumer re-notification");
        }

        [Test]
        public void Given_FloatProvider_When_NaNReplacesNaN_Then_ConsumerDoesNotReRender()
        {
            // Arrange
            s_floatInitial = float.NaN;
            using var mounted = V.Mount(_root, V.Component(FloatProviderHostRender, key: "host"));
            Assume.That(s_floatRenderCount, Is.EqualTo(1), "Precondition: the consumer rendered once on mount");

            // Act
            s_floatSet.Invoke(float.NaN);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(s_floatRenderCount, Is.EqualTo(1),
                "NaN equals itself by bit pattern, so replacing NaN with NaN is not a change");
        }

        [Test]
        public void Given_FloatProvider_When_PositiveZeroReplacedWithNegativeZero_Then_ConsumerReRenders()
        {
            // Arrange
            s_floatInitial = +0f;
            using var mounted = V.Mount(_root, V.Component(FloatProviderHostRender, key: "host"));
            Assume.That(s_floatRenderCount, Is.EqualTo(1), "Precondition: the consumer rendered once on mount");

            // Act
            s_floatSet.Invoke(-0f);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(s_floatRenderCount, Is.EqualTo(2),
                "+0 and -0 differ by bit pattern, so flipping the sign of zero propagates as a change");
        }

        #endregion

        #region ObjectIs predicate

        private static readonly ThemeRecord SharedReference = new("x");

        private static System.Collections.IEnumerable ReferenceEqualityCases()
        {
            yield return new TestCaseData(SharedReference, SharedReference, true)
                .SetName("Given_ReferenceTypes_When_Compared_Then_SameReferenceIsEqual")
                .SetDescription("A reference equals itself");
            yield return new TestCaseData(new ThemeRecord("x"), new ThemeRecord("x"), false)
                .SetName("Given_ReferenceTypes_When_DistinctInstancesWithSameContent_Then_NotEqual")
                .SetDescription("Distinct reference instances are not equal even when their content matches");
            yield return new TestCaseData(null, null, true)
                .SetName("Given_ReferenceTypes_When_BothNull_Then_Equal")
                .SetDescription("Two nulls are equal");
            yield return new TestCaseData(new ThemeRecord("x"), null, false)
                .SetName("Given_ReferenceTypes_When_OneIsNull_Then_NotEqual")
                .SetDescription("A reference and null are not equal");
        }

        [TestCaseSource(nameof(ReferenceEqualityCases))]
        public void Given_ReferenceTypes_When_Compared_Then_EqualByIdentity(
            object a, object b, bool expected)
        {
            // Act + Assert
            Assert.That(ObjectIs.AreEqual(a, b), Is.EqualTo(expected),
                TestContext.CurrentContext.Test.Properties.Get("Description")?.ToString());
        }

        [Test]
        public void Given_FloatNaN_When_ComparedWithItself_Then_Equal()
        {
            // Act + Assert
            Assert.That(
                (ObjectIs.AreEqual(float.NaN, float.NaN), ObjectIs.AreEqual(double.NaN, double.NaN)),
                Is.EqualTo((true, true)),
                "NaN equals itself for both float and double under bit-pattern comparison");
        }

        [Test]
        public void Given_PositiveAndNegativeZero_When_Compared_Then_NotEqual()
        {
            // Act + Assert
            Assert.That(
                (ObjectIs.AreEqual(+0f, -0f), ObjectIs.AreEqual(+0d, -0d)),
                Is.EqualTo((false, false)),
                "+0 and -0 differ by bit pattern for both float and double");
        }

        [Test]
        public void Given_DistinctFloatValues_When_Compared_Then_EqualOnlyWhenIdentical()
        {
            // Act + Assert
            Assert.That(
                (ObjectIs.AreEqual(1.5f, 1.5f), ObjectIs.AreEqual(1.5f, 2.5f)),
                Is.EqualTo((true, false)),
                "Ordinary float values compare equal exactly when their bit patterns match");
        }

        [Test]
        public void Given_NonFloatingPointValueTypes_When_Compared_Then_UseDefaultEquality()
        {
            // Act + Assert
            Assert.That(
                (ObjectIs.AreEqual(42, 42), ObjectIs.AreEqual(42, 43)),
                Is.EqualTo((true, false)),
                "Integer value types fall back to default equality so a boxed call site is not a fresh identity");
        }

        #endregion

        #region Record provider host

        private static ThemeRecord s_recordInitial;
        private static Action<ThemeRecord> s_recordSet;
        private static int s_recordRenderCount;

        private static void ResetRecordHost()
        {
            s_recordInitial = null;
            s_recordSet = null;
            s_recordRenderCount = 0;
        }

        [Component]
        private static VNode RecordProviderHostRender()
        {
            var (value, setValue) = Hooks.UseState(s_recordInitial);
            s_recordSet = setValue;
            return V.Provider(ThemeRecordContext, value, new VNode[]
            {
                V.Component(RecordConsumerRender, key: "consumer"),
            });
        }

        [Component]
        private static VNode RecordConsumerRender()
        {
            var value = Hooks.UseContext(ThemeRecordContext);
            s_recordRenderCount++;
            return V.Label(text: value?.Name ?? "<null>");
        }

        #endregion

        #region Float provider host

        private static float s_floatInitial;
        private static Action<float> s_floatSet;
        private static int s_floatRenderCount;

        private static void ResetFloatHost()
        {
            s_floatInitial = 0f;
            s_floatSet = null;
            s_floatRenderCount = 0;
        }

        [Component]
        private static VNode FloatProviderHostRender()
        {
            var (value, setValue) = Hooks.UseState(s_floatInitial);
            s_floatSet = setValue;
            return V.Provider(FloatContext, value, new VNode[]
            {
                V.Component(FloatConsumerRender, key: "consumer"),
            });
        }

        [Component]
        private static VNode FloatConsumerRender()
        {
            var value = Hooks.UseContext(FloatContext);
            s_floatRenderCount++;
            return V.Label(text: value.ToString());
        }

        #endregion
    }
}
