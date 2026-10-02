using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine.UIElements;

namespace Velvet.Tests
{
    /// <summary>
    /// Resolves every <see cref="EngineMember"/> against the editor running the suite, so a Unity upgrade that
    /// renames, retypes or removes one fails here, naming it, rather than leaving the runtime's fallback to hide it.
    /// The rest pin that a member of another shape does not resolve, which is what makes a pass mean the shape held.
    /// </summary>
    [TestFixture]
    internal sealed class EngineMemberResolutionTests
    {
        private const string UIElementsNamespace = "UnityEngine.UIElements.";

        private static IEnumerable<TestCaseData> Declared()
            => EngineMember.Declared().Select(member => new TestCaseData(member).SetName(
                "Given_ADeclaredEngineMember_When_ResolvedInThisEditor_Then_ItIsFoundWithItsDeclaredShape("
                + $"{member.TypeName.Substring(UIElementsNamespace.Length)}.{member.Name.Split('.').Last()})"));

        [TestCaseSource(nameof(Declared))]
        public void Given_ADeclaredEngineMember_When_ResolvedInThisEditor_Then_ItIsFoundWithItsDeclaredShape(
            EngineMember member)
        {
            // Arrange — nothing beyond the declaration.

            // Act
            var resolved = member.Resolve();

            // Assert
            Assert.That(resolved, Is.Not.Null,
                $"{member} no longer resolves in this editor; read the member's current declaration and update "
                + "both the declaration and every read of it");
        }

        // Each case below resolves a control beside the declaration under test, differing only in the one term the
        // case is named for, so a pass says that term is what was refused.

        [Test]
        public void Given_AFieldDeclaredWithAnotherType_When_Resolved_Then_NothingIsFound()
        {
            // Arrange
            var control = Declare("Field", "UnityEngine.UIElements.StyleSheet", "imports",
                "UnityEngine.UIElements.StyleSheet+ImportStruct[]");
            var member = Declare("Field", "UnityEngine.UIElements.StyleSheet", "imports", "System.Object[]");

            // Act
            var resolved = (control.Resolve() != null, member.Resolve() != null);

            // Assert
            Assert.That(resolved, Is.EqualTo((true, false)));
        }

        [Test]
        public void Given_APropertyDeclaredWithAnotherType_When_Resolved_Then_NothingIsFound()
        {
            // Arrange
            var control = Declare("ReadProperty", "UnityEngine.UIElements.VisualElement", "isCompositeRoot",
                "System.Boolean");
            var member = Declare("ReadProperty", "UnityEngine.UIElements.VisualElement", "isCompositeRoot",
                "System.Int32");

            // Act
            var resolved = (control.Resolve() != null, member.Resolve() != null);

            // Assert
            Assert.That(resolved, Is.EqualTo((true, false)));
        }

        [Test]
        public void Given_AGetterOnlyPropertyDeclaredWritable_When_Resolved_Then_NothingIsFound()
        {
            // Arrange — VisualElement.panel has a getter and no setter.
            var control = Declare("ReadProperty", "UnityEngine.UIElements.VisualElement", "panel",
                "UnityEngine.UIElements.IPanel");
            var member = Declare("WrittenProperty", "UnityEngine.UIElements.VisualElement", "panel",
                "UnityEngine.UIElements.IPanel");

            // Act
            var resolved = (control.Resolve() != null, member.Resolve() != null);

            // Assert
            Assert.That(resolved, Is.EqualTo((true, false)));
        }

        [Test]
        public void Given_APropertyDeclaredOnASubclassOfItsDeclaringType_When_Resolved_Then_NothingIsFound()
        {
            // Arrange — the linker keeps a member on the type that declares it, so a declaration naming a subclass
            // would keep nothing.
            var control = Declare("WrittenProperty", "UnityEngine.UIElements.VisualElement", "pseudoStates",
                "UnityEngine.UIElements.PseudoStates");
            var member = Declare("WrittenProperty", "UnityEngine.UIElements.Button", "pseudoStates",
                "UnityEngine.UIElements.PseudoStates");

            // Act
            var resolved = (control.Resolve() != null, member.Resolve() != null);

            // Assert
            Assert.That(resolved, Is.EqualTo((true, false)));
        }

        [Test]
        public void Given_AConstructedTypeDeclaredWithAnotherBase_When_Resolved_Then_NothingIsFound()
        {
            // Arrange
            var control = Declare("ConstructedType", "UnityEngine.UIElements.PropertyChangedEvent",
                "UnityEngine.UIElements.EventBase`1[UnityEngine.UIElements.PropertyChangedEvent]");
            var member = Declare("ConstructedType", "UnityEngine.UIElements.PropertyChangedEvent", "System.Object");

            // Act
            var resolved = (control.Resolve() != null, member.Resolve() != null);

            // Assert
            Assert.That(resolved, Is.EqualTo((true, false)));
        }

        [Test]
        public void Given_AConstructedTypeWithNoPublicParameterlessConstructor_When_Resolved_Then_NothingIsFound()
        {
            // Arrange — Focusable's parameterless constructor is protected; Button's is public.
            var control = Declare("ConstructedType", "UnityEngine.UIElements.Button",
                "UnityEngine.UIElements.TextElement");
            var member = Declare("ConstructedType", "UnityEngine.UIElements.Focusable",
                "UnityEngine.UIElements.CallbackEventHandler");

            // Act
            var resolved = (control.Resolve() != null, member.Resolve() != null);

            // Assert
            Assert.That(resolved, Is.EqualTo((true, false)));
        }

        [Test]
        public void Given_AMemberOfATypeTheAssemblyDoesNotHold_When_Resolved_Then_NothingIsFound()
        {
            // Arrange
            var control = Declare("Field", "UnityEngine.UIElements.StyleSheet", "imports",
                "UnityEngine.UIElements.StyleSheet+ImportStruct[]");
            var member = Declare("Field", "UnityEngine.UIElements.NoSuchType", "imports",
                "UnityEngine.UIElements.StyleSheet+ImportStruct[]");

            // Act
            var resolved = (control.Resolve() != null, member.Resolve() != null);

            // Assert
            Assert.That(resolved, Is.EqualTo((true, false)));
        }

        [Test]
        public void Given_AMethodDeclaredWithAByValueParameterWhereTheEngineTakesItByRef_When_Resolved_Then_NothingIsFound()
        {
            // Arrange — StyleCache.TryGetValue(long, out ComputedStyle) has an overload on (int, out StyleVariableContext).
            var control = Declare("Method", "UnityEngine.UIElements.StyleCache", "TryGetValue", "System.Boolean",
                new[] { "System.Int64", "UnityEngine.UIElements.ComputedStyle&" });
            var member = Declare("Method", "UnityEngine.UIElements.StyleCache", "TryGetValue", "System.Boolean",
                new[] { "System.Int64", "UnityEngine.UIElements.ComputedStyle" });

            // Act
            var resolved = (control.Resolve() != null, member.Resolve() != null);

            // Assert
            Assert.That(resolved, Is.EqualTo((true, false)));
        }

        [Test]
        public void Given_AMethodDeclaredWithAnotherReturnType_When_Resolved_Then_NothingIsFound()
        {
            // Arrange
            var control = Declare("Method", "UnityEngine.UIElements.StyleCache", "TryGetValue", "System.Boolean",
                new[] { "System.Int32", "UnityEngine.UIElements.StyleVariableContext&" });
            var member = Declare("Method", "UnityEngine.UIElements.StyleCache", "TryGetValue", "System.Void",
                new[] { "System.Int32", "UnityEngine.UIElements.StyleVariableContext&" });

            // Act
            var resolved = (control.Resolve() != null, member.Resolve() != null);

            // Assert
            Assert.That(resolved, Is.EqualTo((true, false)));
        }

        // The factories are private so that no declaration can sit outside EngineMember; a test reaches them here.
        private static EngineMember Declare(string factory, params object[] arguments)
        {
            var method = typeof(EngineMember).GetMethod(factory, BindingFlags.NonPublic | BindingFlags.Static)!;
            return (EngineMember)method.Invoke(null, new object[] { typeof(VisualElement).Assembly }.Concat(arguments).ToArray());
        }
    }
}
