using System.Linq;
using Velvet.SourceGenerators.ReactiveScope;
using Xunit;

namespace Velvet.SourceGenerators.Tests
{
    /// <summary>
    /// Fixture tests for <see cref="ReactiveScopeAnalyzer"/>. 16 cases:
    /// (Pure / concrete deps detection 8) + (null fallback 5) + (Closure 3).
    /// </summary>
    public class ReactiveScopeAnalyzerTests
    {
        [Fact]
        public void Pure_01_SingleParameterReturn()
        {
            // title → {title}
            var r = ReactiveScopeAnalyzerTestHelper.AnalyzeReturnExpression(@"
public class C {
    public string Render(string title) => title.ToUpper();
}
", "Render");
            Assert.True(r.Dependencies.HasValue);
            Assert.Equal(new[] { "title" }, ReactiveScopeAnalyzerTestHelper.DependencyNames(r).OrderBy(x => x));
        }

        [Fact]
        public void Pure_02_MathAbsExpression()
        {
            // Math.Abs(count) + 1 → {count}
            var r = ReactiveScopeAnalyzerTestHelper.AnalyzeReturnExpression(@"
public class C {
    public int Render(int count) => System.Math.Abs(count) + 1;
}
", "Render");
            Assert.True(r.Dependencies.HasValue);
            Assert.Equal(new[] { "count" }, ReactiveScopeAnalyzerTestHelper.DependencyNames(r).OrderBy(x => x));
        }

        [Fact]
        public void Pure_03_LinqWhereCount()
        {
            // items.Where(x => x == filter).Count() → {items, filter}
            var r = ReactiveScopeAnalyzerTestHelper.AnalyzeReturnExpression(@"
using System.Linq;
public class C {
    public int Render(System.Collections.Generic.IEnumerable<string> items, string filter)
        => items.Where(x => x == filter).Count();
}
", "Render");
            Assert.True(r.Dependencies.HasValue);
            var names = ReactiveScopeAnalyzerTestHelper.DependencyNames(r);
            Assert.Contains("items", names);
            Assert.Contains("filter", names);
            Assert.DoesNotContain("x", names);
        }

        [Fact]
        public void Pure_04_DerivedLocalExpandsToBase()
        {
            // var y = a + b; return y; → {a, b}
            var r = ReactiveScopeAnalyzerTestHelper.AnalyzeReturnExpression(@"
public class C {
    public int Render(int a, int b) {
        var y = a + b;
        return y;
    }
}
", "Render");
            Assert.True(r.Dependencies.HasValue);
            Assert.Equal(new[] { "a", "b" }, ReactiveScopeAnalyzerTestHelper.DependencyNames(r).OrderBy(x => x));
        }

        [Fact]
        public void Pure_05_TernaryAllBranches()
        {
            // cond ? x : y → {cond, x, y}
            var r = ReactiveScopeAnalyzerTestHelper.AnalyzeReturnExpression(@"
public class C {
    public int Render(bool cond, int x, int y) => cond ? x : y;
}
", "Render");
            Assert.True(r.Dependencies.HasValue);
            Assert.Equal(new[] { "cond", "x", "y" }, ReactiveScopeAnalyzerTestHelper.DependencyNames(r).OrderBy(x => x));
        }

        [Fact]
        public void Pure_06_SwitchExpressionState()
        {
            // state switch { ... } → {state}
            var r = ReactiveScopeAnalyzerTestHelper.AnalyzeReturnExpression(@"
public class C {
    public int Render(int state) => state switch {
        0 => 0,
        1 => 1,
        _ => -1,
    };
}
", "Render");
            Assert.True(r.Dependencies.HasValue);
            Assert.Equal(new[] { "state" }, ReactiveScopeAnalyzerTestHelper.DependencyNames(r).OrderBy(x => x));
        }

        [Fact]
        public void Pure_07_ForEachIterationVariableResolvesToCollection()
        {
            // foreach (var it in items) total += it; return total; → {items}
            var r = ReactiveScopeAnalyzerTestHelper.AnalyzeReturnExpression(@"
public class C {
    public int Render(System.Collections.Generic.IEnumerable<int> items) {
        int total = 0;
        foreach (var it in items) {
            total = total + it;
        }
        return total;
    }
}
", "Render");
            Assert.True(r.Dependencies.HasValue);
            var names = ReactiveScopeAnalyzerTestHelper.DependencyNames(r);
            Assert.Contains("items", names);
            Assert.DoesNotContain("it", names);
        }

        [Fact]
        public void Pure_08_ConstAndReadOnlyExcluded()
        {
            // const int MyConst = 10; MyConst + value → {value}
            var r = ReactiveScopeAnalyzerTestHelper.AnalyzeReturnExpression(@"
public class C {
    public const int MyConst = 10;
    public int Render(int value) => MyConst + value;
}
", "Render");
            Assert.True(r.Dependencies.HasValue);
            var names = ReactiveScopeAnalyzerTestHelper.DependencyNames(r);
            Assert.Equal(new[] { "value" }, names.OrderBy(x => x));
        }

        [Fact]
        public void Fallback_09_NewRandomNext()
        {
            // new Random().Next() → null
            var r = ReactiveScopeAnalyzerTestHelper.AnalyzeReturnExpression(@"
public class C {
    public int Render() => new System.Random().Next();
}
", "Render");
            Assert.False(r.Dependencies.HasValue);
            Assert.NotEmpty(r.Reasons);
        }

        [Fact]
        public void Fallback_10_VirtualCall()
        {
            // c.GetValue() where GetValue is virtual → null
            var r = ReactiveScopeAnalyzerTestHelper.AnalyzeReturnExpression(@"
public class C {
    public virtual int GetValue() => 0;
}
public class D {
    public int Render(C c) => c.GetValue();
}
", "D.Render");
            Assert.False(r.Dependencies.HasValue);
            Assert.Contains(r.Reasons, x => x.Kind == ScopeDiagnosticKind.Virtual);
        }

        [Fact]
        public void Fallback_11_AbstractCall()
        {
            // c.GetValue() where GetValue is abstract → null
            var r = ReactiveScopeAnalyzerTestHelper.AnalyzeReturnExpression(@"
public abstract class C {
    public abstract int GetValue();
}
public class D {
    public int Render(C c) => c.GetValue();
}
", "D.Render");
            Assert.False(r.Dependencies.HasValue);
            Assert.Contains(r.Reasons, x => x.Kind == ScopeDiagnosticKind.Virtual);
        }

        [Fact]
        public void Fallback_12_DynamicAccess()
        {
            // dynamic d; d.Value → null
            var r = ReactiveScopeAnalyzerTestHelper.AnalyzeReturnExpression(@"
public class C {
    public object Render(dynamic d) => d.Value;
}
", "Render");
            Assert.False(r.Dependencies.HasValue);
            Assert.Contains(r.Reasons, x => x.Kind == ScopeDiagnosticKind.DynamicAccess);
        }

        [Fact]
        public void Fallback_13_DelegateInvocationUnknown()
        {
            // Func<int> f; f() → null (delegate Invoke is virtual and not method-local analyzable)
            var r = ReactiveScopeAnalyzerTestHelper.AnalyzeReturnExpression(@"
public class C {
    public int Render(System.Func<int> f) => f();
}
", "Render");
            Assert.False(r.Dependencies.HasValue);
            Assert.Contains(r.Reasons,
                x => x.Kind is ScopeDiagnosticKind.Virtual or ScopeDiagnosticKind.UnknownCall);
        }

        [Fact]
        public void Closure_14_OuterMutationIsImpure()
        {
            // () => _field = 1 → null (field mutation inside lambda)
            var r = ReactiveScopeAnalyzerTestHelper.AnalyzeReturnExpression(@"
public class C {
    public int _field;
    public System.Func<int> Render() => () => _field = 1;
}
", "Render");
            Assert.False(r.Dependencies.HasValue);
            Assert.Contains(r.Reasons, x => x.Kind == ScopeDiagnosticKind.Impure);
        }

        [Fact]
        public void Closure_15_ImmutableCaptureKeepsOffsetOnly()
        {
            // x => x + offset → {offset}
            var r = ReactiveScopeAnalyzerTestHelper.AnalyzeReturnExpression(@"
public class C {
    public System.Func<int, int> Render(int offset) => x => x + offset;
}
", "Render");
            Assert.True(r.Dependencies.HasValue);
            var names = ReactiveScopeAnalyzerTestHelper.DependencyNames(r);
            Assert.Contains("offset", names);
            Assert.DoesNotContain("x", names);
        }

        [Fact]
        public void Fallback_17_EventAssignment()
        {
            // e += handler inside a lambda → null (subscribing is a side effect)
            var r = ReactiveScopeAnalyzerTestHelper.AnalyzeReturnExpression(@"
public class C {
    public event System.Action? Evt;
    public System.Action Render(System.Action h) => () => Evt += h;
}
", "Render");
            Assert.False(r.Dependencies.HasValue);
            Assert.Contains(r.Reasons, x => x.Kind == ScopeDiagnosticKind.Impure);
        }

        [Fact]
        public void Fallback_18_DeconstructionIntoField()
        {
            // (_field, _) = (1, 2) inside a lambda → null (field assignment is a side effect)
            var r = ReactiveScopeAnalyzerTestHelper.AnalyzeReturnExpression(@"
public class C {
    public int _field;
    public System.Action Render() => () => (_field, var _) = (1, 2);
}
", "Render");
            Assert.False(r.Dependencies.HasValue);
            Assert.Contains(r.Reasons, x => x.Kind == ScopeDiagnosticKind.Impure);
        }

        [Fact]
        public void Fallback_19_CoalesceAssignmentIntoField()
        {
            // _field ??= ""x"" inside a lambda → null (null-coalescing assignment also writes to a field)
            var r = ReactiveScopeAnalyzerTestHelper.AnalyzeReturnExpression(@"
public class C {
    public string? _field;
    public System.Func<string> Render() => () => _field ??= ""x"";
}
", "Render");
            Assert.False(r.Dependencies.HasValue);
            Assert.Contains(r.Reasons, x => x.Kind == ScopeDiagnosticKind.Impure);
        }

        [Fact]
        public void Fallback_20_DynamicIndexerAccess()
        {
            // d[0] → null (dynamic indexer)
            var r = ReactiveScopeAnalyzerTestHelper.AnalyzeReturnExpression(@"
public class C {
    public object Render(dynamic d) => d[0];
}
", "Render");
            Assert.False(r.Dependencies.HasValue);
            Assert.Contains(r.Reasons, x => x.Kind == ScopeDiagnosticKind.DynamicAccess);
        }

        [Fact]
        public void Pure_21_ReadOnlyInstanceFieldExcluded()
        {
            // readonly int _max; _max + x → {x} (readonly is treated as immutable)
            var r = ReactiveScopeAnalyzerTestHelper.AnalyzeReturnExpression(@"
public class C {
    private readonly int _max = 10;
    public int Render(int x) => _max + x;
}
", "Render");
            Assert.True(r.Dependencies.HasValue);
            var names = ReactiveScopeAnalyzerTestHelper.DependencyNames(r);
            Assert.Equal(new[] { "x" }, names.OrderBy(n => n));
        }

        [Fact]
        public void Closure_16_NestedLambdaCollectsOuterCaptures()
        {
            // () => items.Count(i => i == tag) → {items, tag}
            var r = ReactiveScopeAnalyzerTestHelper.AnalyzeReturnExpression(@"
using System.Linq;
public class C {
    public System.Func<int> Render(System.Collections.Generic.IEnumerable<int> items, int tag)
        => () => items.Count(i => i == tag);
}
", "Render");
            Assert.True(r.Dependencies.HasValue);
            var names = ReactiveScopeAnalyzerTestHelper.DependencyNames(r);
            Assert.Contains("items", names);
            Assert.Contains("tag", names);
            Assert.DoesNotContain("i", names);
        }

        [Fact]
        public void AccessPath_01_ParameterSymbolReturnsParameterName()
        {
            var paths = ReactiveScopeAnalyzerTestHelper.ResolveAccessPaths(@"
public class C {
    public string Render(string state) => state.ToUpper();
}
", "Render");
            Assert.Equal(new[] { "state" }, paths);
        }

        [Fact]
        public void AccessPath_02_StatePropertyReconstructedAsDottedAccess()
        {
            var paths = ReactiveScopeAnalyzerTestHelper.ResolveAccessPaths(@"
public struct State { public int Level; public bool Locked; }
public class C {
    public int Render(State state) => state.Level + (state.Locked ? 1 : 0);
}
", "Render");
            Assert.Contains("state.Level", paths);
            Assert.Contains("state.Locked", paths);
        }

        [Fact]
        public void AccessPath_03_LocalExpandsToBaseSymbols()
        {
            // local y = a + b; return y + 1 → y is expanded, leaving {a, b} (ReactiveScopeAnalyzer behavior).
            // AccessPath resolves directly to the parameter names a, b.
            var paths = ReactiveScopeAnalyzerTestHelper.ResolveAccessPaths(@"
public class C {
    public int Render(int a, int b)
    {
        var y = a + b;
        return y + 1;
    }
}
", "Render");
            Assert.Equal(new[] { "a", "b" }, paths.OrderBy(p => p, System.StringComparer.Ordinal));
        }

        [Fact]
        public void AccessPath_04_UnresolvableMemberReturnsFallback()
        {
            // this._other.Foo pattern: _other resolves via this.Field, but Foo is a field on the Other type
            // and belongs to neither the render state type nor the component type, so it falls back.
            var paths = ReactiveScopeAnalyzerTestHelper.ResolveAccessPaths(@"
public class Other { public int Foo; }
public class C {
    private Other _other;
    public int Render(int x) {
        var f = _other.Foo;
        return f + x;
    }
}
", "Render");
            Assert.Contains("x", paths);
            Assert.Contains(paths, p => p.StartsWith("<fallback:", System.StringComparison.Ordinal));
        }
    }
}
