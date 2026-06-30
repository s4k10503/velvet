using NUnit.Framework;
using UnityEngine.UIElements;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies the contract of <see cref="Ref{T}"/> as a reference holder.
    /// <list type="bullet">
    /// <item>The type parameter is constrained to <c>class</c> only, so a handle interface unrelated to
    /// <see cref="VisualElement"/> is a valid target type.</item>
    /// <item>A freshly constructed ref holds <c>null</c> in <see cref="Ref{T}.Current"/> until a value is set.</item>
    /// <item><see cref="Ref{T}.SetElement"/> stores a <see cref="VisualElement"/>-derived value into
    /// <see cref="Ref{T}.Current"/>, preserving instance identity.</item>
    /// <item>Through the type-erased <see cref="IHookRefSetter"/> facade, <c>Set(value)</c> publishes the value
    /// and <c>Set(null)</c> clears it, so the core can drive the ref without knowing the concrete type.</item>
    /// </list>
    /// </summary>
    [TestFixture]
    internal sealed class RefTypeConstraintTests
    {
        private interface IFakeHandle
        {
            void Focus();
        }

        private sealed class FakeHandle : IFakeHandle
        {
            public void Focus() { }
        }

        [Test]
        public void Given_NonVisualElementHandleType_When_Constructed_Then_CurrentIsNull()
        {
            // Act
            var handleRef = new Ref<IFakeHandle>();

            // Assert
            Assert.That(handleRef.Current, Is.Null,
                "A ref over a non-VisualElement handle type is valid and starts empty");
        }

        [Test]
        public void Given_VisualElementType_When_SetElementCalled_Then_CurrentHoldsSameInstance()
        {
            // Arrange
            var elementRef = new Ref<TextField>();
            var element = new TextField();

            // Act
            elementRef.SetElement(element);

            // Assert
            Assert.That(elementRef.Current, Is.SameAs(element),
                "SetElement publishes the VisualElement-derived value by identity");
        }

        [Test]
        public void Given_RefViewedAsSetter_When_SetWithValue_Then_CurrentHoldsThatValue()
        {
            // Arrange
            var handleRef = new Ref<IFakeHandle>();
            IHookRefSetter setter = handleRef;
            var fake = new FakeHandle();

            // Act
            setter.Set(fake);

            // Assert
            Assert.That(handleRef.Current, Is.SameAs(fake),
                "The type-erased setter publishes the value into Current");
        }

        [Test]
        public void Given_RefHoldingValue_When_SetNullViaSetter_Then_CurrentIsCleared()
        {
            // Arrange
            var handleRef = new Ref<IFakeHandle>();
            IHookRefSetter setter = handleRef;
            setter.Set(new FakeHandle());
            Assume.That(handleRef.Current, Is.Not.Null, "Precondition: the ref holds a value before clearing");

            // Act
            setter.Set(null);

            // Assert
            Assert.That(handleRef.Current, Is.Null, "Set(null) through the setter clears Current");
        }
    }
}
