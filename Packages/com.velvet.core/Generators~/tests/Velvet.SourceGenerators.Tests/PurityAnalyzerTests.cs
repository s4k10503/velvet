using System.Linq;
using Velvet.SourceGenerators.PurityAnalysis;
using Xunit;

namespace Velvet.SourceGenerators.Tests
{
    public class PurityAnalyzerTests
    {
        [Theory]
        [InlineData("LocalArithmetic", @"
public class C {
    public int LocalArithmetic(int a, int b) {
        int x = a + b;
        return x;
    }
}
")]
        [InlineData("ReturnVNode", @"
public class C {
    public Velvet.VNode ReturnVNode() {
        return new Velvet.VNode();
    }
}
")]
        [InlineData("MathCall", @"
public class C {
    public int MathCall(int x) {
        return System.Math.Abs(x);
    }
}
")]
        [InlineData("SystemDiagnosticsPure", @"
public class C {
    [System.Diagnostics.Contracts.Pure]
    public int SystemDiagnosticsPure() {
        System.Console.WriteLine(""ignored"");
        return 0;
    }
}
")]
        [InlineData("VelvetPure", @"
public class C {
    [Velvet.Pure]
    public int VelvetPure() {
        System.Console.WriteLine(""ignored"");
        return 0;
    }
}
")]
        [InlineData("ReadOnlyField", @"
public class C {
    private readonly int _value = 42;
    public int ReadOnlyField() => _value;
}
")]
        [InlineData("SwitchExpression", @"
public class C {
    public int SwitchExpression(int x) => x switch {
        0 => 0,
        > 0 => 1,
        _ => -1,
    };
}
")]
        [InlineData("LinqCount", @"
using System.Linq;
public class C {
    public int LinqCount(System.Collections.Generic.IEnumerable<int> items)
        => items.Where(i => i > 0).Count();
}
")]
        [InlineData("StringSubstring", @"
public class C {
    public string StringSubstring(string s) => s.Substring(0, 1).ToUpper();
}
")]
        [InlineData("Ternary", @"
public class C {
    public int Ternary(int a) => a > 0 ? 1 : -1;
}
")]
        [InlineData("JetBrainsPure", @"
namespace JetBrains.Annotations { public sealed class PureAttribute : System.Attribute { } }
public class C {
    [JetBrains.Annotations.Pure]
    public int JetBrainsPure() {
        for (int i = 0; i < 10; i++) { }
        return 0;
    }
}
")]
        public void PureCases(string methodName, string source)
        {
            var result = PurityAnalyzerTestHelper.AnalyzeMethod(source, methodName);
            Assert.Equal(Purity.Pure, result.Purity);
        }

        [Theory]
        [InlineData("FieldAssign", "Assignment", @"
public class C {
    private int _field;
    public void FieldAssign(int x) { _field = x; }
}
")]
        [InlineData("OutParamAssign", "RefOutInParam", @"
public class C {
    public void OutParamAssign(out int value) { value = 42; }
}
")]
        [InlineData("ThrowException", "Throw", @"
public class C {
    public int ThrowException() { throw new System.Exception(); }
}
")]
        [InlineData("ForLoop", "Loop", @"
public class C {
    public int ForLoop() { for (int i = 0; i < 10; i++) { } return 0; }
}
")]
        [InlineData("ForEachLoop", "Loop", @"
public class C {
    public int ForEachLoop(int[] a) { foreach (var x in a) { } return 0; }
}
")]
        [InlineData("WhileLoop", "Loop", @"
public class C {
    public int WhileLoop() { int i = 0; while (i < 10) i++; return i; }
}
")]
        [InlineData("ConsoleWrite", "KnownImpureCall", @"
public class C {
    public void ConsoleWrite(int x) { System.Console.WriteLine(x); }
}
")]
        [InlineData("TaskRun", "KnownImpureCall", @"
public class C {
    public void TaskRun() { System.Threading.Tasks.Task.Run(() => { }); }
}
")]
        [InlineData("StringBuilderAppend", "KnownImpureCall", @"
public class C {
    public string StringBuilderAppend() {
        var sb = new System.Text.StringBuilder();
        sb.Append(""x"");
        return sb.ToString();
    }
}
")]
        [InlineData("ArrayElementAssign", "Assignment", @"
public class C {
    public void ArrayElementAssign(int[] arr) { arr[0] = 42; }
}
")]
        [InlineData("DateTimeNow", "KnownImpureCall", @"
public class C {
    public System.DateTime DateTimeNow() { return System.DateTime.Now; }
}
")]
        [InlineData("GuidNewGuid", "KnownImpureCall", @"
public class C {
    public System.Guid GuidNewGuid() { return System.Guid.NewGuid(); }
}
")]
        [InlineData("DateTimeOffsetNow", "KnownImpureCall", @"
public class C {
    public System.DateTimeOffset DateTimeOffsetNow() { return System.DateTimeOffset.Now; }
}
")]
        [InlineData("DateTimeOffsetUtcNow", "KnownImpureCall", @"
public class C {
    public System.DateTimeOffset DateTimeOffsetUtcNow() { return System.DateTimeOffset.UtcNow; }
}
")]
        [InlineData("StopwatchGetTimestamp", "KnownImpureCall", @"
public class C {
    public long StopwatchGetTimestamp() { return System.Diagnostics.Stopwatch.GetTimestamp(); }
}
")]
        [InlineData("StopwatchStartNew", "KnownImpureCall", @"
public class C {
    public System.Diagnostics.Stopwatch StopwatchStartNew() { return System.Diagnostics.Stopwatch.StartNew(); }
}
")]
        [InlineData("UnityRandomValue", "KnownImpureCall", @"
namespace UnityEngine { public static class Random { public static float value => 0f; } }
public class C {
    public float UnityRandomValue() { return UnityEngine.Random.value; }
}
")]
        [InlineData("UnityRandomRange", "KnownImpureCall", @"
namespace UnityEngine { public static class Random { public static int Range(int min, int max) => 0; } }
public class C {
    public int UnityRandomRange() { return UnityEngine.Random.Range(0, 10); }
}
")]
        [InlineData("UnityTimeDeltaTime", "KnownImpureCall", @"
namespace UnityEngine { public static class Time { public static float deltaTime => 0f; } }
public class C {
    public float UnityTimeDeltaTime() { return UnityEngine.Time.deltaTime; }
}
")]
        [InlineData("UnityApplicationIsPlaying", "KnownImpureCall", @"
namespace UnityEngine { public static class Application { public static bool isPlaying => false; } }
public class C {
    public bool UnityApplicationIsPlaying() { return UnityEngine.Application.isPlaying; }
}
")]
        [InlineData("StopwatchElapsedMilliseconds", "KnownImpureCall", @"
public class C {
    public long StopwatchElapsedMilliseconds(System.Diagnostics.Stopwatch sw) { return sw.ElapsedMilliseconds; }
}
")]
        public void ImpureCases(string methodName, string expectedKindName, string source)
        {
            var expectedKind = (ImpurityKind)System.Enum.Parse(typeof(ImpurityKind), expectedKindName);
            var result = PurityAnalyzerTestHelper.AnalyzeMethod(source, methodName);
            Assert.Equal(Purity.Impure, result.Purity);
            Assert.Contains(result.Reasons, r => r.Kind == expectedKind);
        }

        [Fact]
        public void Unknown_AbstractMethod()
        {
            var result = PurityAnalyzerTestHelper.AnalyzeMethod(@"
public abstract class C {
    public abstract int AbstractMethod();
}
", "AbstractMethod");
            Assert.Equal(Purity.Unknown, result.Purity);
        }

        [Fact]
        public void Unknown_VirtualMethod()
        {
            var result = PurityAnalyzerTestHelper.AnalyzeMethod(@"
public class C {
    public virtual int VirtualMethod() => 0;
}
", "VirtualMethod");
            Assert.Equal(Purity.Unknown, result.Purity);
        }

        [Fact]
        public void Unknown_OverrideMethod()
        {
            var result = PurityAnalyzerTestHelper.AnalyzeMethod(@"
public class B {
    public virtual int Run() => 0;
}
public class C : B {
    public override int Run() => 1;
}
", "C.Run");
            Assert.Equal(Purity.Unknown, result.Purity);
        }

        [Fact]
        public void Impure_UnknownCallFromGenericMember()
        {
            var result = PurityAnalyzerTestHelper.AnalyzeMethod(@"
public class C {
    public string GenericMember<T>(T value) { return value.ToString(); }
}
", "GenericMember");
            Assert.Equal(Purity.Impure, result.Purity);
            Assert.Contains(result.Reasons, r => r.Kind is ImpurityKind.VirtualCall or ImpurityKind.UnknownCall);
        }

        [Fact]
        public void Pure_ClosureCapturesImmutablePrimitive()
        {
            // int captures are freshening (read-only snapshot of a value type), not mutation.
            var result = PurityAnalyzerTestHelper.AnalyzeMethod(@"
public class C {
    public System.Func<int> ClosureCapture(int outer) {
        return () => outer + 1;
    }
}
", "ClosureCapture");
            Assert.Equal(Purity.Pure, result.Purity);
        }

        [Fact]
        public void Pure_ClosureCapturesString()
        {
            var result = PurityAnalyzerTestHelper.AnalyzeMethod(@"
public class C {
    public System.Func<int> ClosureCapture(string s) {
        return () => s.Length;
    }
}
", "ClosureCapture");
            Assert.Equal(Purity.Pure, result.Purity);
        }

        [Fact]
        public void Pure_ClosureCapturesEnum()
        {
            var result = PurityAnalyzerTestHelper.AnalyzeMethod(@"
public enum Color { Red, Green, Blue }
public class C {
    public System.Func<bool> ClosureCapture(Color c) {
        return () => c == Color.Red;
    }
}
", "ClosureCapture");
            Assert.Equal(Purity.Pure, result.Purity);
        }

        [Fact]
        public void Pure_ClosureCapturesNullableImmutable()
        {
            // Nullable<T> wrapping a primitive is treated as immutable (value-type wrapper, T immutable).
            // Reading the captured nullable directly avoids method calls that would otherwise be UnknownCall.
            var result = PurityAnalyzerTestHelper.AnalyzeMethod(@"
public class C {
    public System.Func<int?> ClosureCapture(int? outer) {
        return () => outer;
    }
}
", "ClosureCapture");
            Assert.Equal(Purity.Pure, result.Purity);
        }

        [Fact]
        public void Pure_ClosureCapturesReadonlyStruct()
        {
            var result = PurityAnalyzerTestHelper.AnalyzeMethod(@"
public readonly struct Point { public readonly int X; public readonly int Y; }
public class C {
    public System.Func<int> ClosureCapture(Point p) {
        return () => p.X + p.Y;
    }
}
", "ClosureCapture");
            Assert.Equal(Purity.Pure, result.Purity);
        }

        [Fact]
        public void Impure_ClosureCapturesMutableReferenceType()
        {
            // Capturing a reference-type variable that may be mutated through the captured slot is reported.
            var result = PurityAnalyzerTestHelper.AnalyzeMethod(@"
public class C {
    public System.Func<int> ClosureCapture(System.Collections.Generic.List<int> items) {
        return () => items.Count;
    }
}
", "ClosureCapture");
            Assert.Equal(Purity.Impure, result.Purity);
            Assert.Contains(result.Reasons, r => r.Kind == ImpurityKind.ClosureCapture);
        }

        [Fact]
        public void Pure_CallGraph_PropagatesPureCallee()
        {
            // Two-level call graph: caller invokes a Pure helper. The recursion absorbs the callee silently
            // and the caller is reported as Pure (no UnknownCall recorded).
            var result = PurityAnalyzerTestHelper.AnalyzeMethod(@"
public class C {
    private int Helper(int x) { return x + 1; }
    public int Caller(int n) { return Helper(n); }
}
", "Caller");
            Assert.Equal(Purity.Pure, result.Purity);
        }

        [Fact]
        public void Impure_CallGraph_PropagatesImpureCallee()
        {
            // Two-level call graph: caller invokes an Impure helper (assigns to a field).
            // Propagation surfaces the callee as KnownImpureCall on the caller.
            var result = PurityAnalyzerTestHelper.AnalyzeMethod(@"
public class C {
    private int _state;
    private int Helper(int x) { _state = x; return x + 1; }
    public int Caller(int n) { return Helper(n); }
}
", "Caller");
            Assert.Equal(Purity.Impure, result.Purity);
            Assert.Contains(result.Reasons, r => r.Kind == ImpurityKind.KnownImpureCall);
        }

        [Fact]
        public void Pure_CallGraph_HandlesSelfRecursionWithoutInfiniteLoop()
        {
            // Self-recursive Pure method must terminate via the visited set; result should remain Pure.
            var result = PurityAnalyzerTestHelper.AnalyzeMethod(@"
public class C {
    public int Fib(int n) { return n <= 1 ? n : Fib(n - 1) + Fib(n - 2); }
}
", "Fib");
            Assert.Equal(Purity.Pure, result.Purity);
        }

        [Fact]
        public void Pure_CallGraph_HandlesMutualRecursionWithoutInfiniteLoop()
        {
            // Mutual recursion (A → B → A) must terminate via the visited set; both methods stay Pure.
            var result = PurityAnalyzerTestHelper.AnalyzeMethod(@"
public class C {
    public int Even(int n) { return n == 0 ? 1 : Odd(n - 1); }
    public int Odd(int n)  { return n == 0 ? 0 : Even(n - 1); }
}
", "Even");
            Assert.Equal(Purity.Pure, result.Purity);
        }

        [Fact]
        public void Impure_ClosureCapturesNonReadonlyStruct()
        {
            // Non-readonly struct captures are conservatively treated as mutation candidates.
            var result = PurityAnalyzerTestHelper.AnalyzeMethod(@"
public struct Mutable { public int X; }
public class C {
    public System.Func<int> ClosureCapture(Mutable m) {
        return () => m.X;
    }
}
", "ClosureCapture");
            Assert.Equal(Purity.Impure, result.Purity);
            Assert.Contains(result.Reasons, r => r.Kind == ImpurityKind.ClosureCapture);
        }

        [Fact]
        public void Impure_UnityDebugLog()
        {
            var result = PurityAnalyzerTestHelper.AnalyzeMethod(@"
namespace UnityEngine {
    public static class Debug {
        public static void Log(object message) { }
    }
}
public class C {
    public void CallDebug() { UnityEngine.Debug.Log(""hello""); }
}
", "CallDebug");
            Assert.Equal(Purity.Impure, result.Purity);
            Assert.Contains(result.Reasons, r => r.Kind == ImpurityKind.KnownImpureCall);
        }

        [Fact]
        public void PureResult_HasNoReasons()
        {
            var result = PurityAnalyzerTestHelper.AnalyzeMethod(@"
public class C {
    public int Pure(int x) => x + 1;
}
", "Pure");
            Assert.Equal(Purity.Pure, result.Purity);
            Assert.Empty(result.Reasons);
        }

        [Fact]
        public void UnknownResult_HasNoReasons()
        {
            var result = PurityAnalyzerTestHelper.AnalyzeMethod(@"
public abstract class C {
    public abstract int A();
}
", "A");
            Assert.Equal(Purity.Unknown, result.Purity);
            Assert.Empty(result.Reasons);
        }

        [Fact]
        public void ImpureResult_HasSeveralReasonsAccumulated()
        {
            var result = PurityAnalyzerTestHelper.AnalyzeMethod(@"
public class C {
    private int _f;
    public int Multiple(int x) {
        _f = x;
        for (int i = 0; i < 3; i++) { }
        System.Console.WriteLine(x);
        return _f;
    }
}
", "Multiple");
            Assert.Equal(Purity.Impure, result.Purity);
            Assert.True(result.Reasons.Length >= 3);
            Assert.Contains(result.Reasons, r => r.Kind == ImpurityKind.Assignment);
            Assert.Contains(result.Reasons, r => r.Kind == ImpurityKind.Loop);
            Assert.Contains(result.Reasons, r => r.Kind == ImpurityKind.KnownImpureCall);
        }

        [Fact]
        public void Unknown_ExternMethod()
        {
            var result = PurityAnalyzerTestHelper.AnalyzeMethod(@"
public class C {
    [System.Runtime.InteropServices.DllImport(""kernel32"")]
    public static extern int GetCurrentThreadId();
}
", "GetCurrentThreadId");
            Assert.Equal(Purity.Unknown, result.Purity);
        }

        [Fact]
        public void Unknown_PartialDeclarationOnly()
        {
            var result = PurityAnalyzerTestHelper.AnalyzeMethod(@"
public partial class C {
    public partial int PartialOnly(int x);
}
", "PartialOnly");
            Assert.Equal(Purity.Unknown, result.Purity);
        }

        [Fact]
        public void Impure_NewRandomConstructor()
        {
            var result = PurityAnalyzerTestHelper.AnalyzeMethod(@"
public class C {
    public void NewRandom() { var r = new System.Random(); }
}
", "NewRandom");
            Assert.Equal(Purity.Impure, result.Purity);
            Assert.Contains(result.Reasons, r => r.Kind == ImpurityKind.KnownImpureCall);
        }
    }
}
