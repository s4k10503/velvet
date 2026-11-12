using System;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;
using NUnit.Framework;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies which getters read off an object a scope is keyed on, by counting the calls the woven method makes to
    /// each: a getter the scope is keyed on runs once for the key and once in the scope, and one it is not keyed on
    /// runs in the scope alone.
    /// <list type="bullet">
    /// <item>A getter declared outside the framework, and one the compiler wrote for an automatic property of a framework
    /// type, is keyed on.</item>
    /// <item>A getter a framework type's author wrote is not, whether the type is under <c>System</c> or under
    /// <c>Mono</c>.</item>
    /// <item>A getter returning a value of a generic parameter is not keyed on, since the caller cannot name its type.</item>
    /// </list>
    /// </summary>
    [TestFixture]
    internal sealed class ReactiveScopeKeyedMemberTests
    {
        private static MethodDefinition WovenMethod(Type fixture, string methodName)
        {
            var assembly = ReactiveScopeTestSupport.ReadWoven(fixture);
            return ReactiveScopeTestSupport.MethodOf(assembly, fixture, methodName);
        }

        // -1 where the method carries no report, which no expected count is.
        private static int CallsTo(string methodName, Type declaring, string getter)
        {
            if (ReactiveScopeTestSupport.ReportOf(typeof(ReactiveScopeKeyedMemberTests), methodName) == null) return -1;
            var method = WovenMethod(typeof(ReactiveScopeKeyedMemberTests), methodName);
            using var module = method.Module;
            return method.Body.Instructions.Count(instruction =>
                (instruction.OpCode.Code == Code.Call || instruction.OpCode.Code == Code.Callvirt)
                && instruction.Operand is MethodReference callee
                && callee.DeclaringType.FullName == declaring.FullName
                && callee.Name == getter);
        }

        [Component]
        private static VNode ReadsTheLengthOfAString(string name, int other)
            => V.Div("length", V.Text(Convert.ToString(name.Length)), V.Text(Convert.ToString(other)));

        // GREEN_ON_BASE(characterization): the base keys a scope reading the length of a string on the string.
        // Returning true at the end of `IsKeyableGetter` keys it on a getter the framework's author wrote, which then
        // runs for the key as well.
        [Test]
        public void Given_ALabelReadingTheLengthOfAString_When_Compiled_Then_TheLengthIsReadOnce()
        {
            // Act
            var calls = CallsTo(nameof(ReadsTheLengthOfAString), typeof(string), "get_Length");

            // Assert
            Assert.That(calls, Is.EqualTo(1));
        }

        [Component]
        private static VNode ReadsAWrittenGetterOfACecilType(Mono.Cecil.WriterParameters parameters, int other)
            => V.Div("written", V.Text(Convert.ToString(parameters.WriteSymbols)), V.Text(Convert.ToString(other)));

        // GREEN_ON_BASE(characterization): the base keys a scope reading a getter Mono.Cecil's author wrote on the object.
        // Removing the `Mono.` clause from `IsFrameworkType` reads the type as one outside the framework, whose
        // getters are keyed on.
        [Test]
        public void Given_ALabelReadingAGetterWrittenOnAMonoType_When_Compiled_Then_TheGetterIsReadOnce()
        {
            // Act
            var calls = CallsTo(nameof(ReadsAWrittenGetterOfACecilType), typeof(Mono.Cecil.WriterParameters), "get_WriteSymbols");

            // Assert
            Assert.That(calls, Is.EqualTo(1));
        }

        [Component]
        private static VNode ReadsAnAutomaticPropertyOfACecilType(Mono.Cecil.WriterParameters parameters, int other)
            => V.Div("automatic", V.Text(Convert.ToString(parameters.DeterministicMvid)), V.Text(Convert.ToString(other)));

        // GREEN_ON_BASE(characterization): the base keys a scope reading an automatic property of a framework type on the property.
        // Reading `==` as `!=` where `IsKeyableGetter` looks for `CompilerGeneratedAttribute` passes over the only
        // attribute the getter carries and keys the scope on the object.
        [Test]
        public void Given_ALabelReadingAnAutomaticPropertyOfAFrameworkType_When_Compiled_Then_TheGetterIsReadForTheKeyToo()
        {
            // Act
            var calls = CallsTo(nameof(ReadsAnAutomaticPropertyOfACecilType), typeof(Mono.Cecil.WriterParameters), "get_DeterministicMvid");

            // Assert
            Assert.That(calls, Is.EqualTo(2));
        }

        private sealed class Holder<T>
        {
            public Holder(T value)
            {
                Value = value;
            }

            public T Value { get; }
        }

        [Component]
        private static VNode ReadsAGenericProperty(Holder<string> holder, int other)
            => V.Div("generic", V.Text(holder.Value), V.Text(Convert.ToString(other)));

        // GREEN_ON_BASE(characterization): the base keys a scope reading a property typed by a generic parameter on the object.
        // Removing the `IsStorable` clause from `IsKeyableGetter` keys it on the property, and the key then boxes the
        // value as the generic parameter, which the method has no binding for.
        [Test]
        public void Given_ALabelReadingAPropertyOfAGenericClass_When_Compiled_Then_NoInstructionNamesAGenericParameter()
        {
            // Arrange
            var method = WovenMethod(typeof(ReactiveScopeKeyedMemberTests), nameof(ReadsAGenericProperty));
            using var module = method.Module;

            // Act
            var open = ReactiveScopeTestSupport.ReportOf(typeof(ReactiveScopeKeyedMemberTests), nameof(ReadsAGenericProperty)) == null
                ? -1
                : method.Body.Instructions.Count(instruction => instruction.Operand is GenericParameter);

            // Assert
            Assert.That(open, Is.EqualTo(0));
        }
    }
}
