using System.Linq;
using Xunit;

namespace Velvet.SourceGenerators.Tests
{
    /// <summary>
    /// Pins which <c>Hooks.UseCallback</c> calls a C# 9 consumer can write without a type argument, against the
    /// stub <see cref="StubSurfaceDriftTests"/> holds to the runtime's signatures. A C# 9 lambda has no type of its
    /// own, so the type argument is inferred only where an overload names the delegate shape.
    /// <list type="bullet">
    /// <item>A parameterless <c>Action</c> lambda, a typed two-parameter <c>Action</c> lambda and a typed
    /// one-parameter <c>Func</c> lambda compile with deps and without, as does a parameterless method group.</item>
    /// <item>The explicit spellings a caller wrote before these overloads existed still compile to the delegate
    /// type they name: a single-parameter <c>Action</c> whose body binds against both parameter types, a null, and
    /// a lambda returning a delegate included.</item>
    /// </list>
    /// </summary>
    public sealed class UseCallbackInferenceTests
    {
        [Fact]
        public void Given_ACSharp9Consumer_When_ItOmitsTheTypeArgumentForEachNamedShape_Then_ItCompiles()
        {
            // Arrange
            const string source = @"
using System;
using Velvet;

public static class Consumer
{
    static void Click() { }
    static int Count() => 1;
    static void Pair(int a, string b) { }

    public static void Render()
    {
        var dep = 1;
        Action a = Hooks.UseCallback(() => Click(), dep);
        Action c = Hooks.UseCallback(Click, dep);
        Action<int, string> d = Hooks.UseCallback((int x, string y) => Pair(x, y), dep);
        Func<int, int> e = Hooks.UseCallback((int x) => x + dep, dep);
        Action f = Hooks.UseCallback(() => Click());
        Action<int, string> g = Hooks.UseCallback<int, string>(Pair);
    }
}";

            // Act
            var run = GeneratorTestHelper.RunAsCSharp9Consumer(source);

            // Assert
            Assert.Empty(run.CompilationErrors.Select(error => error.ToString()));
        }

        [Fact]
        public void Given_ACSharp9Consumer_When_ItWritesTheTypeArgumentExplicitly_Then_ItStillCompiles()
        {
            // Arrange — the Action<int> lambda's body binds with x as an int and as an Action<int> alike, which is
            // the shape a single-parameter Action overload would make ambiguous
            const string source = @"
using System;
using Velvet;

public static class Consumer
{
    static void Log(object value) { }
    static void Click() { }
    static int Count() => 1;
    static Action Next() => Click;

    public static void Render()
    {
        var dep = 1;
        Action a = Hooks.UseCallback<Action>(() => Click(), dep);
        Action<int> b = Hooks.UseCallback<Action<int>>(x => Log(x), dep);
        Func<int> c = Hooks.UseCallback<Func<int>>(() => Count(), dep);
        Action<string> d = Hooks.UseCallback<Action<string>>(s => Log(s));
        Action e = Hooks.UseCallback<Action>(() => Next(), dep);
        Action f = Hooks.UseCallback<Action>(null);
        Func<string> g = Hooks.UseCallback<Func<string>>(null, dep);
    }
}";

            // Act
            var run = GeneratorTestHelper.RunAsCSharp9Consumer(source);

            // Assert
            Assert.Empty(run.CompilationErrors.Select(error => error.ToString()));
        }
    }
}
