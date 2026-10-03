using System;
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

        [TestCase(typeof(bool), "System.Boolean", false)]
        [TestCase(typeof(List<int>), "System.Collections.Generic.List`1[System.Int32]", false)]
        [TestCase(typeof(Dictionary<string, List<int>>), "System.Collections.Generic.Dictionary`2[System.String,System.Collections.Generic.List`1[System.Int32]]", false)]
        [TestCase(typeof(int[]), "System.Int32[]", false)]
        [TestCase(typeof(int), "System.Int32&", true)]
        public void Given_ATypeWithAChangedDisplayString_When_TheDeclaredShapeIsChecked_Then_MetadataStillMatches(Type type, string shape, bool byRef)
        {
            // Arrange
            var displayed = new ChangedDisplayType(byRef ? type.MakeByRefType() : type);

            // Act
            var matches = MatchesShape(displayed, shape);

            // Assert
            Assert.That((displayed.ToString() != shape, matches), Is.EqualTo((true, true)));
        }

        [TestCase(typeof(List<int>), "System.Collections.Generic.List`1[System.Int32]", "System.Collections.Generic.List`1[System.String]")]
        [TestCase(typeof(Dictionary<string, List<int>>), "System.Collections.Generic.Dictionary`2[System.String,System.Collections.Generic.List`1[System.Int32]]", "System.Collections.Generic.Dictionary`2[System.String,System.Collections.Generic.List`1[System.String]]")]
        [TestCase(typeof(Dictionary<string, int>), "System.Collections.Generic.Dictionary`2[System.String,System.Int32]", "System.Collections.Generic.Dictionary`2[System.String]")]
        public void Given_AChangedGenericArgument_When_TheGenericShapeIsChecked_Then_OnlyTheControlMatches(Type type, string control, string changed)
        {
            // Arrange / Act
            var matches = (MatchesShape(type, control), MatchesShape(type, changed));

            // Assert
            Assert.That(matches, Is.EqualTo((true, false)));
        }

        [Test]
        public void Given_AnArrayWithAnotherRank_When_TheDeclaredShapeIsChecked_Then_OnlyTheVectorMatches()
        {
            // Arrange
            var shape = "System.Int32[]";

            // Act
            var matches = (MatchesShape(typeof(int[]), shape), MatchesShape(typeof(int[,]), shape),
                MatchesShape(typeof(int).MakeArrayType(1), shape));

            // Assert
            Assert.That(matches, Is.EqualTo((true, false, false)));
        }

        [Test]
        public void Given_ANonEnumWithTheDeclaredName_When_AnEnumShapeIsChecked_Then_OnlyTheEnumMatches()
        {
            // Arrange
            var enumType = typeof(MemberTypes);
            var nonEnumType = typeof(int);

            // Act
            var matches = (MatchesShape(enumType, "enum:" + enumType.FullName),
                MatchesShape(nonEnumType, "enum:" + nonEnumType.FullName));

            // Assert
            Assert.That(matches, Is.EqualTo((true, false)));
        }

        [TestCase("Field", "UnityEngine.UIElements.PseudoStates", "Focus", "enum:UnityEngine.UIElements.PseudoStates", "enum:System.Reflection.MemberTypes")]
        [TestCase("Field", "UnityEngine.UIElements.FocusController", "m_FocusedElements", "System.Collections.Generic.List`1[UnityEngine.UIElements.FocusController+FocusedElement]", "System.Collections.Generic.List`1[UnityEngine.UIElements.FocusController+FocusedElement)")]
        [TestCase("Field", "UnityEngine.UIElements.FocusController", "m_FocusedElements", "System.Collections.Generic.List`1[UnityEngine.UIElements.FocusController+FocusedElement]", "System.Collections.Generic.HashSet`1[UnityEngine.UIElements.FocusController+FocusedElement]")]
        [TestCase("ReadProperty", "UnityEngine.UIElements.VisualElement", "isCompositeRoot", "System.Boolean", "System.Boolean[System.Boolean]")]
        public void Given_AChangedShapeDiscriminator_When_ADeclaredMemberIsResolved_Then_OnlyTheControlMatches(
            string factory, string type, string name, string controlShape, string changedShape)
        {
            // Arrange
            var control = Declare(factory, type, name, controlShape);
            var changed = Declare(factory, type, name, changedShape);

            // Act
            var matches = (control.Resolve() != null, changed.Resolve() != null);

            // Assert
            Assert.That(matches, Is.EqualTo((true, false)));
        }

        [Test]
        public void Given_AByRefWithAnotherElementType_When_ADeclaredMethodIsResolved_Then_OnlyTheControlMatches()
        {
            // Arrange
            var control = Declare("Method", "UnityEngine.UIElements.StyleCache", "TryGetValue", "System.Boolean",
                new[] { "System.Int64", "UnityEngine.UIElements.ComputedStyle&" });
            var changed = Declare("Method", "UnityEngine.UIElements.StyleCache", "TryGetValue", "System.Boolean",
                new[] { "System.Int64", "UnityEngine.UIElements.StyleVariableContext&" });

            // Act
            var matches = (control.Resolve() != null, changed.Resolve() != null);

            // Assert
            Assert.That(matches, Is.EqualTo((true, false)));
        }

        [Test]
        public void Given_ARootTypeDeclaredWithABaseShape_When_Resolved_Then_OnlyTheControlWithThatBaseMatches()
        {
            // Arrange
            var constructible = typeof(ArgumentException);
            var control = DeclareIn(constructible.Assembly, "ConstructedType", constructible.FullName,
                constructible.BaseType.FullName);
            var changed = DeclareIn(typeof(object).Assembly, "ConstructedType", typeof(object).FullName,
                constructible.BaseType.FullName);

            // Act
            var matches = (control.Resolve() != null, changed.Resolve() != null);

            // Assert
            Assert.That(matches, Is.EqualTo((true, false)));
        }

        [Test]
        public void Given_AParameterlessMethod_When_ADeclaredMethodIsResolved_Then_OnlyTheEmptyParameterListMatches()
        {
            // Arrange
            var control = Declare("Method", "UnityEngine.UIElements.VisualElement", "Clear", "System.Void", Array.Empty<string>());
            var changed = Declare("Method", "UnityEngine.UIElements.VisualElement", "Clear", "System.Void", new[] { "System.Int32" });

            // Act
            var matches = (control.Resolve() != null, changed.Resolve() != null);

            // Assert
            Assert.That(matches, Is.EqualTo((true, false)));
        }

        [Test]
        public void Given_ANestedGenericParameter_When_ADeclaredMethodIsResolved_Then_ItsCommaStaysInsideTheArgument()
        {
            // Arrange
            var type = typeof(List<Dictionary<string, int>>);
            var parameters = new[] { "System.Int32", "System.Int32",
                "System.Collections.Generic.Dictionary`2[System.String,System.Int32]",
                "System.Collections.Generic.IComparer`1[System.Collections.Generic.Dictionary`2[System.String,System.Int32]]" };
            var control = DeclareIn(type.Assembly, "Method", type.FullName, nameof(List<int>.BinarySearch), "System.Int32", parameters);
            var changed = DeclareIn(type.Assembly, "Method", type.FullName, nameof(List<int>.BinarySearch), "System.Int32",
                new[] { parameters[0], parameters[1], "System.Collections.Generic.Dictionary`2[System.String,System.String]", parameters[3] });

            // Act
            var matches = (control.Resolve() != null, changed.Resolve() != null);

            // Assert
            Assert.That(matches, Is.EqualTo((true, false)));
        }

        [Test]
        public void Given_AnOpenGenericReturnType_When_ADeclaredMethodIsResolved_Then_OnlyTheClosedControlMatches()
        {
            // Arrange
            var closed = typeof(List<int>);
            var open = typeof(List<>);
            var parameters = new[] { "System.Int32", "System.Int32" };
            var control = DeclareIn(closed.Assembly, "Method", closed.FullName, nameof(List<int>.GetRange),
                "System.Collections.Generic.List`1[System.Int32]", parameters);
            var changed = DeclareIn(open.Assembly, "Method", open.FullName, nameof(List<int>.GetRange), open.FullName, parameters);

            // Act
            var matches = (control.Resolve() != null, changed.Resolve() != null);

            // Assert
            Assert.That(matches, Is.EqualTo((true, false)));
        }

        private static bool MatchesShape(Type type, string shape)
            => typeof(EngineMember).GetMethod("MatchesType", BindingFlags.NonPublic | BindingFlags.Static)
                ?.Invoke(null, new object[] { type, shape }) is true;

        private sealed class ChangedDisplayType : TypeDelegator
        {
            public ChangedDisplayType(Type type) : base(type) { }

            public override string ToString() => "different display spelling";
            public override bool IsGenericType => typeImpl.IsGenericType;
            public override bool IsSZArray => typeImpl.IsSZArray;
            public override Type GetGenericTypeDefinition() => typeImpl.GetGenericTypeDefinition();
            public override Type[] GetGenericArguments() => typeImpl.GetGenericArguments();
        }

        // The factories are private so that no declaration can sit outside EngineMember; a test reaches them here.
        private static EngineMember Declare(string factory, params object[] arguments)
            => DeclareIn(typeof(VisualElement).Assembly, factory, arguments);

        private static EngineMember DeclareIn(Assembly assembly, string factory, params object[] arguments)
        {
            var method = typeof(EngineMember).GetMethod(factory, BindingFlags.NonPublic | BindingFlags.Static)!;
            return (EngineMember)method.Invoke(null, new object[] { assembly }.Concat(arguments).ToArray());
        }
    }
}
