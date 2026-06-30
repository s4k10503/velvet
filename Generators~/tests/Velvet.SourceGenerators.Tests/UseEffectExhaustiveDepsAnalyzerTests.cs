using System.Linq;
using System.Threading.Tasks;
using Velvet.SourceGenerators.AutoDeps;
using Velvet.SourceGenerators.CodeFixes;
using Xunit;

namespace Velvet.SourceGenerators.Tests
{
    /// <summary>Tests for <see cref="UseEffectExhaustiveDepsAnalyzer"/>.</summary>
    /// <remarks>
    /// All test sources use the <c>() =&gt; () =&gt; { ... }</c> nested-lambda shape so they typecheck against
    /// the Velvet stub's <c>UseEffect(Func&lt;Action&gt; factory, object[] deps)</c> signature; the inner
    /// lambda is the cleanup action. The analyzer descends into the outer lambda's tree and inspects every
    /// captured local regardless of nesting depth.
    /// </remarks>
    public sealed class UseEffectExhaustiveDepsAnalyzerTests
    {
        [Fact]
        public void Reports_Vel100_When_Lambda_Captures_Local_Missing_From_Deps()
        {
            const string source = @"
namespace MyApp.Pages
{
    public static class HomePage
    {
        public static void Render()
        {
            var local = 5;
            global::Velvet.Hooks.UseEffect(() => () => System.Console.WriteLine(local), new object[] { });
        }
    }
}";
            var diagnostics = GeneratorTestHelper.RunAnalyzer(source, new UseEffectExhaustiveDepsAnalyzer());
            var vel100 = diagnostics.Where(d => d.Id == "VEL100").ToList();
            Assert.Single(vel100);
            Assert.Contains("local", vel100[0].GetMessage());
        }

        [Fact]
        public void Reports_Vel100_When_Deps_Is_ArrayEmpty_And_Lambda_Captures_Local()
        {
            const string source = @"
namespace MyApp.Pages
{
    public static class HomePage
    {
        public static void Render()
        {
            var local = 5;
            global::Velvet.Hooks.UseEffect(() => () => System.Console.WriteLine(local), System.Array.Empty<object>());
        }
    }
}";
            var diagnostics = GeneratorTestHelper.RunAnalyzer(source, new UseEffectExhaustiveDepsAnalyzer());
            var vel100 = diagnostics.Where(d => d.Id == "VEL100").ToList();
            Assert.Single(vel100);
            Assert.Contains("local", vel100[0].GetMessage());
        }

        [Fact]
        public void Reports_Vel100_When_Deps_Is_ZeroLengthArray_And_Lambda_Captures_Local()
        {
            const string source = @"
namespace MyApp.Pages
{
    public static class HomePage
    {
        public static void Render()
        {
            var local = 5;
            global::Velvet.Hooks.UseEffect(() => () => System.Console.WriteLine(local), new object[0]);
        }
    }
}";
            var diagnostics = GeneratorTestHelper.RunAnalyzer(source, new UseEffectExhaustiveDepsAnalyzer());
            var vel100 = diagnostics.Where(d => d.Id == "VEL100").ToList();
            Assert.Single(vel100);
            Assert.Contains("local", vel100[0].GetMessage());
        }

        [Fact]
        public void DoesNotReport_When_Deps_Is_NonZeroSizedArrayWithoutInitializer()
        {
            // A sized array with unknown elements (`new object[n]`, n != 0) is genuinely unanalyzable — the
            // analyzer must stay silent (false-positive-free), unlike the empty forms above.
            const string source = @"
namespace MyApp.Pages
{
    public static class HomePage
    {
        public static void Render()
        {
            var local = 5;
            var deps = new object[3];
            global::Velvet.Hooks.UseEffect(() => () => System.Console.WriteLine(local), new object[3]);
        }
    }
}";
            var diagnostics = GeneratorTestHelper.RunAnalyzer(source, new UseEffectExhaustiveDepsAnalyzer());
            Assert.Empty(diagnostics.Where(d => d.Id == "VEL100"));
        }

        [Fact]
        public void DoesNotReport_When_Captured_Local_Is_In_Deps()
        {
            const string source = @"
namespace MyApp.Pages
{
    public static class HomePage
    {
        public static void Render()
        {
            var count = 5;
            global::Velvet.Hooks.UseEffect(() => () => System.Console.WriteLine(count), new object[] { count });
        }
    }
}";
            var diagnostics = GeneratorTestHelper.RunAnalyzer(source, new UseEffectExhaustiveDepsAnalyzer());
            Assert.Empty(diagnostics.Where(d => d.Id == "VEL100"));
        }

        [Fact]
        public void DoesNotReport_When_Captured_Local_Is_Action_Setter_From_Hook()
        {
            // Stable hook returns (Action / Action<T>) are exempted: the setter from UseState is a stable
            // reference across renders and need not be in deps.
            const string source = @"
namespace MyApp.Pages
{
    public static class HomePage
    {
        public static void Render()
        {
            var (value, setValue) = global::Velvet.Hooks.UseState(0);
            global::Velvet.Hooks.UseEffect(() => () => setValue(1), new object[] { });
        }
    }
}";
            var diagnostics = GeneratorTestHelper.RunAnalyzer(source, new UseEffectExhaustiveDepsAnalyzer());
            Assert.Empty(diagnostics.Where(d => d.Id == "VEL100"));
        }

        [Fact]
        public void DoesNotReport_When_Captured_Local_Is_MutableRef_From_UseMutableRef()
        {
            // MutableRef<T> from UseMutableRef is a stable instance across re-renders, mirroring the
            // Velvet.Ref<> exemption. Capturing it inside a UseEffect lambda must not produce VEL100.
            const string source = @"
namespace MyApp.Pages
{
    public static class HomePage
    {
        public static void Render()
        {
            var counter = global::Velvet.Hooks.UseMutableRef(0);
            global::Velvet.Hooks.UseEffect(() => () => System.Console.WriteLine(counter.Current), new object[] { });
        }
    }
}";
            var diagnostics = GeneratorTestHelper.RunAnalyzer(source, new UseEffectExhaustiveDepsAnalyzer());
            Assert.Empty(diagnostics.Where(d => d.Id == "VEL100"));
        }

        [Fact]
        public void DoesNotReport_When_Deps_Form_Is_Unsupported()
        {
            // Conservative: pass the deps array as a variable (not an array initializer literal). The analyzer
            // cannot statically verify the contents, so it falls back to silence (false-negative-tolerant).
            const string source = @"
namespace MyApp.Pages
{
    public static class HomePage
    {
        public static void Render()
        {
            var local = 5;
            object[] deps = new object[] { };
            global::Velvet.Hooks.UseEffect(() => () => System.Console.WriteLine(local), deps);
        }
    }
}";
            var diagnostics = GeneratorTestHelper.RunAnalyzer(source, new UseEffectExhaustiveDepsAnalyzer());
            Assert.Empty(diagnostics.Where(d => d.Id == "VEL100"));
        }

        [Fact]
        public void DoesNotReport_When_Method_Is_NotVelvet_Hooks_UseEffect()
        {
            // Same-named user method must not trigger the analyzer.
            const string source = @"
namespace MyApp.Pages
{
    public static class FakeHooks
    {
        public static void UseEffect(System.Func<System.Action> factory, object[] deps) { }
    }
    public static class HomePage
    {
        public static void Render()
        {
            var local = 5;
            FakeHooks.UseEffect(() => () => System.Console.WriteLine(local), new object[] { });
        }
    }
}";
            var diagnostics = GeneratorTestHelper.RunAnalyzer(source, new UseEffectExhaustiveDepsAnalyzer());
            Assert.Empty(diagnostics.Where(d => d.Id == "VEL100"));
        }

        [Fact]
        public void Reports_When_UseCallback_Lambda_Captures_Local_Missing_From_Deps()
        {
            // exhaustive-deps must extend beyond UseEffect: UseCallback is a deps-comparing hook too.
            const string source = @"
namespace MyApp.Pages
{
    public static class HomePage
    {
        public static void Render()
        {
            var local = 5;
            global::Velvet.Hooks.UseCallback<System.Action>(() => System.Console.WriteLine(local), new object[] { });
        }
    }
}";
            var diagnostics = GeneratorTestHelper.RunAnalyzer(source, new UseEffectExhaustiveDepsAnalyzer());
            var vel100 = diagnostics.Where(d => d.Id == "VEL100").ToList();
            Assert.Single(vel100);
            Assert.Contains("local", vel100[0].GetMessage());
        }

        [Fact]
        public void DoesNotReport_When_UseCallback_Captured_Local_Is_In_Deps()
        {
            const string source = @"
namespace MyApp.Pages
{
    public static class HomePage
    {
        public static void Render()
        {
            var count = 5;
            global::Velvet.Hooks.UseCallback<System.Action>(() => System.Console.WriteLine(count), new object[] { count });
        }
    }
}";
            var diagnostics = GeneratorTestHelper.RunAnalyzer(source, new UseEffectExhaustiveDepsAnalyzer());
            Assert.Empty(diagnostics.Where(d => d.Id == "VEL100"));
        }

        [Fact]
        public void Reports_When_VMemo_Factory_Captures_Local_Missing_From_Deps()
        {
            // V.Memoized is Velvet's memoized-value primitive (factory + deps). Exhaustive-deps covers it just
            // like the Hooks-side deps-comparing methods. The analyzer accepts the call only when its
            // descriptor's ContainingTypeFullName matches (Velvet.V here).
            const string source = @"
namespace MyApp.Pages
{
    public static class HomePage
    {
        public static global::Velvet.VNode Render()
        {
            var label = ""home"";
            return global::Velvet.V.Memoized(() => { System.Console.WriteLine(label); return new global::Velvet.VNode(); }, new object[] { });
        }
    }
}";
            var diagnostics = GeneratorTestHelper.RunAnalyzer(source, new UseEffectExhaustiveDepsAnalyzer());
            var vel100 = diagnostics.Where(d => d.Id == "VEL100").ToList();
            Assert.Single(vel100);
            Assert.Contains("label", vel100[0].GetMessage());
        }

        [Fact]
        public void DoesNotReport_When_VMemo_Factory_Captured_Local_Is_In_Deps()
        {
            const string source = @"
namespace MyApp.Pages
{
    public static class HomePage
    {
        public static global::Velvet.VNode Render()
        {
            var label = ""home"";
            return global::Velvet.V.Memoized(() => { System.Console.WriteLine(label); return new global::Velvet.VNode(); }, new object[] { label });
        }
    }
}";
            var diagnostics = GeneratorTestHelper.RunAnalyzer(source, new UseEffectExhaustiveDepsAnalyzer());
            Assert.Empty(diagnostics.Where(d => d.Id == "VEL100"));
        }

        [Fact]
        public void Reports_When_UseMemo_Factory_Captures_Local_Missing_From_Deps()
        {
            // UseMemo is Velvet.Hooks's value-memoization primitive (factory + params deps). Exhaustive-deps
            // covers it like the other deps-comparing Hooks methods; the descriptor binds to Velvet.Hooks.
            const string source = @"
namespace MyApp.Pages
{
    public static class HomePage
    {
        public static global::Velvet.VNode Render()
        {
            var label = ""home"";
            return global::Velvet.Hooks.UseMemo(() => { System.Console.WriteLine(label); return new global::Velvet.VNode(); }, new object[] { });
        }
    }
}";
            var diagnostics = GeneratorTestHelper.RunAnalyzer(source, new UseEffectExhaustiveDepsAnalyzer());
            var vel100 = diagnostics.Where(d => d.Id == "VEL100").ToList();
            Assert.Single(vel100);
            Assert.Contains("label", vel100[0].GetMessage());
        }

        [Fact]
        public void DoesNotReport_When_UseMemo_Factory_Captured_Local_Is_In_Deps()
        {
            const string source = @"
namespace MyApp.Pages
{
    public static class HomePage
    {
        public static global::Velvet.VNode Render()
        {
            var label = ""home"";
            return global::Velvet.Hooks.UseMemo(() => { System.Console.WriteLine(label); return new global::Velvet.VNode(); }, new object[] { label });
        }
    }
}";
            var diagnostics = GeneratorTestHelper.RunAnalyzer(source, new UseEffectExhaustiveDepsAnalyzer());
            Assert.Empty(diagnostics.Where(d => d.Id == "VEL100"));
        }

        [Fact]
        public void Reports_When_VMemoWithKey_Factory_Captures_Local_Missing_From_Deps()
        {
            // V.MemoizedWithKey shifts factory to arg index 1 (key at 0); the descriptor must locate the
            // lambda at the correct position or the analyzer would miss the closure entirely.
            const string source = @"
namespace MyApp.Pages
{
    public static class HomePage
    {
        public static global::Velvet.VNode Render()
        {
            var label = ""home"";
            return global::Velvet.V.MemoizedWithKey(""k"", () => { System.Console.WriteLine(label); return new global::Velvet.VNode(); }, new object[] { });
        }
    }
}";
            var diagnostics = GeneratorTestHelper.RunAnalyzer(source, new UseEffectExhaustiveDepsAnalyzer());
            var vel100 = diagnostics.Where(d => d.Id == "VEL100").ToList();
            Assert.Single(vel100);
            Assert.Contains("label", vel100[0].GetMessage());
        }

        [Fact]
        public void DoesNotReport_When_VMemoWithKey_Factory_Captured_Local_Is_In_Deps()
        {
            // Symmetric guard for the swap-of-FactoryArgIndex/DepsArgIndex regression: if the descriptor
            // accidentally read deps from arg 1 (key) instead of arg 2, the listed `label` dep would
            // appear missing and this test would fail.
            const string source = @"
namespace MyApp.Pages
{
    public static class HomePage
    {
        public static global::Velvet.VNode Render()
        {
            var label = ""home"";
            return global::Velvet.V.MemoizedWithKey(""k"", () => { System.Console.WriteLine(label); return new global::Velvet.VNode(); }, new object[] { label });
        }
    }
}";
            var diagnostics = GeneratorTestHelper.RunAnalyzer(source, new UseEffectExhaustiveDepsAnalyzer());
            Assert.Empty(diagnostics.Where(d => d.Id == "VEL100"));
        }

        [Fact]
        public void DoesNotReport_When_Deps_Are_Member_Access_Covering_Captured_Root()
        {
            // Deps written as `state.SaveSlots` cover the captured root `state` in the body, so the root
            // and its member-access deps are unified. Without this unification, every
            // production V.MemoizedWithKey call that lists slice deps would trip VEL100 on the captured
            // root local — the analyzer's identifier-only filter previously dropped MemberAccessExpression
            // deps entirely.
            const string source = @"
namespace MyApp.Pages
{
    public static class State { public int SaveSlots; public bool IsInteractable; }
    public static class HomePage
    {
        public static global::Velvet.VNode Render()
        {
            var state = new State();
            return global::Velvet.V.MemoizedWithKey(""save-slot-list"",
                () => { System.Console.WriteLine(state.SaveSlots + "":"" + state.IsInteractable); return new global::Velvet.VNode(); },
                state.SaveSlots, state.IsInteractable);
        }
    }
}";
            var diagnostics = GeneratorTestHelper.RunAnalyzer(source, new UseEffectExhaustiveDepsAnalyzer());
            Assert.Empty(diagnostics.Where(d => d.Id == "VEL100"));
        }

        [Fact]
        public void Reports_When_UseLayoutEffect_Lambda_Captures_Local_Missing_From_Deps()
        {
            const string source = @"
namespace MyApp.Pages
{
    public static class HomePage
    {
        public static void Render()
        {
            var local = 5;
            global::Velvet.Hooks.UseLayoutEffect(() => () => System.Console.WriteLine(local), new object[] { });
        }
    }
}";
            var diagnostics = GeneratorTestHelper.RunAnalyzer(source, new UseEffectExhaustiveDepsAnalyzer());
            var vel100 = diagnostics.Where(d => d.Id == "VEL100").ToList();
            Assert.Single(vel100);
            Assert.Contains("local", vel100[0].GetMessage());
        }

        [Fact]
        public void Reports_When_UseImperativeHandle_Factory_Captures_Local_Missing_From_Deps()
        {
            // UseImperativeHandle's deps-comparing argument is the factory at index 1 (refTarget is index 0).
            const string source = @"
namespace MyApp.Pages
{
    public static class HomePage
    {
        public static void Render()
        {
            var local = 5;
            global::Velvet.Hooks.UseImperativeHandle<object>(null, () => (object)local, new object[] { });
        }
    }
}";
            var diagnostics = GeneratorTestHelper.RunAnalyzer(source, new UseEffectExhaustiveDepsAnalyzer());
            var vel100 = diagnostics.Where(d => d.Id == "VEL100").ToList();
            Assert.Single(vel100);
            Assert.Contains("local", vel100[0].GetMessage());
        }

        [Fact]
        public void Reports_When_UseCallback_Deps_Are_Loose_Params_Missing_The_Capture()
        {
            // params-style deps (UseCallback(cb, a, c)) carry the deps as loose arguments rather than an array
            // literal. Two-or-more loose args are unambiguously element-wise, so the analyzer reads them as the
            // deps set; a captured 'b' missing from {a, c} is flagged. (A single loose arg is ambiguous with the
            // array-by-reference form and is intentionally not analyzed.)
            const string source = @"
namespace MyApp.Pages
{
    public static class HomePage
    {
        public static void Render()
        {
            var a = 1;
            var b = 2;
            var c = 3;
            global::Velvet.Hooks.UseCallback<System.Action>(() => { System.Console.WriteLine(a); System.Console.WriteLine(b); System.Console.WriteLine(c); }, a, c);
        }
    }
}";
            var diagnostics = GeneratorTestHelper.RunAnalyzer(source, new UseEffectExhaustiveDepsAnalyzer());
            var vel100 = diagnostics.Where(d => d.Id == "VEL100").ToList();
            Assert.Single(vel100);
            Assert.Contains("b", vel100[0].GetMessage());
        }

        [Fact]
        public void DoesNotReport_When_UseCallback_Deps_Are_A_Single_Loose_Arg()
        {
            // A single trailing argument to a params hook is ambiguous: it could be one loose dependency or the
            // whole deps array passed by reference. The analyzer bails (false-positive-free) rather than guess.
            const string source = @"
namespace MyApp.Pages
{
    public static class HomePage
    {
        public static void Render()
        {
            var a = 1;
            var b = 2;
            global::Velvet.Hooks.UseCallback<System.Action>(() => { System.Console.WriteLine(a); System.Console.WriteLine(b); }, a);
        }
    }
}";
            var diagnostics = GeneratorTestHelper.RunAnalyzer(source, new UseEffectExhaustiveDepsAnalyzer());
            Assert.Empty(diagnostics.Where(d => d.Id == "VEL100"));
        }

        [Fact]
        public void DoesNotReport_When_UseCallback_Loose_Params_Contain_All_Captures()
        {
            const string source = @"
namespace MyApp.Pages
{
    public static class HomePage
    {
        public static void Render()
        {
            var a = 1;
            var b = 2;
            global::Velvet.Hooks.UseCallback<System.Action>(() => { System.Console.WriteLine(a); System.Console.WriteLine(b); }, a, b);
        }
    }
}";
            var diagnostics = GeneratorTestHelper.RunAnalyzer(source, new UseEffectExhaustiveDepsAnalyzer());
            Assert.Empty(diagnostics.Where(d => d.Id == "VEL100"));
        }

        [Fact]
        public void DoesNotReport_When_Captured_Local_Is_Dispatch_From_UseReducer()
        {
            // The dispatch function returned by UseReducer is a stable reference, exempt like UseState's setter.
            const string source = @"
namespace MyApp.Pages
{
    public static class HomePage
    {
        public static void Render()
        {
            var (state, dispatch) = global::Velvet.Hooks.UseReducer<int, int>((s, a) => s + a, 0);
            global::Velvet.Hooks.UseEffect(() => () => dispatch(1), new object[] { });
        }
    }
}";
            var diagnostics = GeneratorTestHelper.RunAnalyzer(source, new UseEffectExhaustiveDepsAnalyzer());
            Assert.Empty(diagnostics.Where(d => d.Id == "VEL100"));
        }

        [Fact]
        public void Reports_When_Captured_Action_Local_Is_Not_A_Hook_Return()
        {
            // A local Action assigned from a non-hook source is NOT stable: exemption must be origin-based,
            // not type-based. Only UseState / UseReducer / UseRef returns are exempt.
            const string source = @"
namespace MyApp.Pages
{
    public static class HomePage
    {
        public static void Render()
        {
            System.Action callback = () => { };
            global::Velvet.Hooks.UseEffect(() => () => callback(), new object[] { });
        }
    }
}";
            var diagnostics = GeneratorTestHelper.RunAnalyzer(source, new UseEffectExhaustiveDepsAnalyzer());
            var vel100 = diagnostics.Where(d => d.Id == "VEL100").ToList();
            Assert.Single(vel100);
            Assert.Contains("callback", vel100[0].GetMessage());
        }

        [Fact]
        public void DoesNotReport_When_Captured_Symbol_Is_A_Parameter()
        {
            // Method parameters are not locals; the capture collector only inspects ILocalSymbol captures, so a
            // captured parameter is never flagged regardless of its type.
            const string source = @"
namespace MyApp.Pages
{
    public static class HomePage
    {
        public static void Render(System.Action callback)
        {
            global::Velvet.Hooks.UseEffect(() => () => callback(), new object[] { });
        }
    }
}";
            var diagnostics = GeneratorTestHelper.RunAnalyzer(source, new UseEffectExhaustiveDepsAnalyzer());
            Assert.Empty(diagnostics.Where(d => d.Id == "VEL100"));
        }

        [Fact]
        public void Reports_When_UseState_Value_Element_Is_Missing_From_Deps()
        {
            // The value element (tuple position 0) of UseState is NOT stable — it changes between renders, so a
            // captured value local missing from deps must still be flagged. Only the setter (position 1) is exempt.
            const string source = @"
namespace MyApp.Pages
{
    public static class HomePage
    {
        public static void Render()
        {
            var (count, setCount) = global::Velvet.Hooks.UseState(0);
            global::Velvet.Hooks.UseEffect(() => () => System.Console.WriteLine(count), new object[] { });
        }
    }
}";
            var diagnostics = GeneratorTestHelper.RunAnalyzer(source, new UseEffectExhaustiveDepsAnalyzer());
            var vel100 = diagnostics.Where(d => d.Id == "VEL100").ToList();
            Assert.Single(vel100);
            Assert.Contains("count", vel100[0].GetMessage());
        }

        [Fact]
        public void Reports_When_UseState_ActionTyped_Value_Is_Missing_From_Deps()
        {
            // Isolates the slot-position logic from the obsolete type-based exemption: the value element (slot 0)
            // is typed System.Action, which the removed type-based exemption would have wrongly treated as stable.
            // The slot-aware exemption must still flag it, so a regression back to type-based logic fails here.
            const string source = @"
namespace MyApp.Pages
{
    public static class HomePage
    {
        public static void Render()
        {
            var (onTick, setOnTick) = global::Velvet.Hooks.UseState<System.Action>(null);
            global::Velvet.Hooks.UseEffect(() => () => onTick(), new object[] { });
        }
    }
}";
            var diagnostics = GeneratorTestHelper.RunAnalyzer(source, new UseEffectExhaustiveDepsAnalyzer());
            var vel100 = diagnostics.Where(d => d.Id == "VEL100").ToList();
            Assert.Single(vel100);
            Assert.Contains("onTick", vel100[0].GetMessage());
        }

        [Fact]
        public void DoesNotReport_When_UseState_Setter_Element_Is_Missing_From_Deps()
        {
            // The setter element (tuple position 1) of UseState is stable across renders and need not be in deps,
            // even when captured alongside a value that is in deps.
            const string source = @"
namespace MyApp.Pages
{
    public static class HomePage
    {
        public static void Render()
        {
            var (count, setCount) = global::Velvet.Hooks.UseState(0);
            global::Velvet.Hooks.UseEffect(() => () => { System.Console.WriteLine(count); setCount(1); }, new object[] { count });
        }
    }
}";
            var diagnostics = GeneratorTestHelper.RunAnalyzer(source, new UseEffectExhaustiveDepsAnalyzer());
            Assert.Empty(diagnostics.Where(d => d.Id == "VEL100"));
        }

        [Fact]
        public void Reports_When_UseReducer_State_Element_Is_Missing_From_Deps()
        {
            // The state element (tuple position 0) of UseReducer changes between renders and is not exempt; only
            // the dispatch (position 1) is stable.
            const string source = @"
namespace MyApp.Pages
{
    public static class HomePage
    {
        public static void Render()
        {
            var (state, dispatch) = global::Velvet.Hooks.UseReducer<int, int>((s, a) => s + a, 0);
            global::Velvet.Hooks.UseEffect(() => () => System.Console.WriteLine(state), new object[] { });
        }
    }
}";
            var diagnostics = GeneratorTestHelper.RunAnalyzer(source, new UseEffectExhaustiveDepsAnalyzer());
            var vel100 = diagnostics.Where(d => d.Id == "VEL100").ToList();
            Assert.Single(vel100);
            Assert.Contains("state", vel100[0].GetMessage());
        }

        [Fact]
        public void DoesNotReport_When_UseLayoutEffect_Captured_Local_Is_In_Deps()
        {
            const string source = @"
namespace MyApp.Pages
{
    public static class HomePage
    {
        public static void Render()
        {
            var count = 5;
            global::Velvet.Hooks.UseLayoutEffect(() => () => System.Console.WriteLine(count), new object[] { count });
        }
    }
}";
            var diagnostics = GeneratorTestHelper.RunAnalyzer(source, new UseEffectExhaustiveDepsAnalyzer());
            Assert.Empty(diagnostics.Where(d => d.Id == "VEL100"));
        }

        [Fact]
        public void DoesNotReport_When_UseImperativeHandle_Factory_Capture_Is_In_Deps()
        {
            // UseImperativeHandle's deps slot is index 2 (refTarget @0, factory @1); a capture listed there is fine.
            const string source = @"
namespace MyApp.Pages
{
    public static class HomePage
    {
        public static void Render()
        {
            var local = 5;
            global::Velvet.Hooks.UseImperativeHandle<object>(null, () => (object)local, new object[] { local });
        }
    }
}";
            var diagnostics = GeneratorTestHelper.RunAnalyzer(source, new UseEffectExhaustiveDepsAnalyzer());
            Assert.Empty(diagnostics.Where(d => d.Id == "VEL100"));
        }

        [Fact]
        public void DoesNotReport_When_UseCallback_Params_Array_Literal_Contains_All_Captures()
        {
            // A populated array literal passed to a params deps hook must be read element-by-element.
            const string source = @"
namespace MyApp.Pages
{
    public static class HomePage
    {
        public static void Render()
        {
            var a = 1;
            var b = 2;
            global::Velvet.Hooks.UseCallback<System.Action>(() => { System.Console.WriteLine(a); System.Console.WriteLine(b); }, new object[] { a, b });
        }
    }
}";
            var diagnostics = GeneratorTestHelper.RunAnalyzer(source, new UseEffectExhaustiveDepsAnalyzer());
            Assert.Empty(diagnostics.Where(d => d.Id == "VEL100"));
        }

        [Fact]
        public void DoesNotReport_When_Params_Hook_Deps_Are_An_Array_Variable()
        {
            // A params hook receiving a single non-literal argument (the deps array passed by reference) is
            // unanalyzable; the analyzer must stay silent rather than treat the variable name as one loose dep.
            const string source = @"
namespace MyApp.Pages
{
    public static class HomePage
    {
        public static void Render()
        {
            var a = 1;
            var b = 2;
            object[] deps = new object[] { a, b };
            global::Velvet.Hooks.UseCallback<System.Action>(() => { System.Console.WriteLine(a); System.Console.WriteLine(b); }, deps);
        }
    }
}";
            var diagnostics = GeneratorTestHelper.RunAnalyzer(source, new UseEffectExhaustiveDepsAnalyzer());
            Assert.Empty(diagnostics.Where(d => d.Id == "VEL100"));
        }

        [Fact]
        public async Task CodeFix_PreservesIndentation_For_MultiLine_Deps()
        {
            // Multi-line deps initializer must keep its newline + indentation when a new element is appended.
            const string source = @"
namespace MyApp.Pages
{
    public static class HomePage
    {
        public static void Render()
        {
            var a = 1;
            var b = 2;
            var c = 3;
            global::Velvet.Hooks.UseEffect(() => () => System.Console.WriteLine($""{a}+{b}+{c}""), new object[]
            {
                a,
                b,
            });
        }
    }
}";
            var fixedText = await CodeFixTestHelper.ApplyCodeFixAsync(
                source,
                new UseEffectExhaustiveDepsAnalyzer(),
                new Vel100FillMissingDepsCodeFixProvider(),
                codeActionTitle: "Add missing local to hook deps array",
                expectedDiagnosticId: "VEL100");
            // The new dep `c` lands on its own line (multi-line trivia preserved). Single-line `, c }` would
            // indicate trivia loss. SeparatedList.Add does not append a trailing comma to the new tail element.
            var normalized = fixedText.Replace("\r\n", "\n");
            Assert.Contains("                a,\n", normalized);
            Assert.Contains("                b,\n", normalized);
            Assert.Contains("                c\n", normalized);
        }

        [Fact]
        public void Reports_When_Lambda_Captures_Instance_Field_Missing_From_Deps()
        {
            // Exhaustive-deps must extend beyond locals: an instance field read through the component closure
            // is a reactive value and must appear in deps, just like instance state bound to the render
            // closure.
            const string source = @"
namespace MyApp.Pages
{
    public sealed class HomePage
    {
        private int _count = 0;
        public void Render()
        {
            global::Velvet.Hooks.UseEffect(() => () => System.Console.WriteLine(_count), new object[] { });
        }
    }
}";
            var diagnostics = GeneratorTestHelper.RunAnalyzer(source, new UseEffectExhaustiveDepsAnalyzer());
            var vel100 = diagnostics.Where(d => d.Id == "VEL100").ToList();
            Assert.Single(vel100);
            Assert.Contains("_count", vel100[0].GetMessage());
        }

        [Fact]
        public void Reports_When_Lambda_Captures_Instance_Property_Missing_From_Deps()
        {
            // Instance properties read through the closure are reactive too and must be flagged when absent
            // from deps.
            const string source = @"
namespace MyApp.Pages
{
    public sealed class HomePage
    {
        private int Count { get; set; }
        public void Render()
        {
            global::Velvet.Hooks.UseEffect(() => () => System.Console.WriteLine(Count), new object[] { });
        }
    }
}";
            var diagnostics = GeneratorTestHelper.RunAnalyzer(source, new UseEffectExhaustiveDepsAnalyzer());
            var vel100 = diagnostics.Where(d => d.Id == "VEL100").ToList();
            Assert.Single(vel100);
            Assert.Contains("Count", vel100[0].GetMessage());
        }

        [Fact]
        public void DoesNotReport_When_Instance_Field_Is_In_Deps()
        {
            // An instance field listed in deps is correctly tracked and must not be flagged.
            const string source = @"
namespace MyApp.Pages
{
    public sealed class HomePage
    {
        private int _count = 0;
        public void Render()
        {
            global::Velvet.Hooks.UseEffect(() => () => System.Console.WriteLine(_count), new object[] { _count });
        }
    }
}";
            var diagnostics = GeneratorTestHelper.RunAnalyzer(source, new UseEffectExhaustiveDepsAnalyzer());
            Assert.Empty(diagnostics.Where(d => d.Id == "VEL100"));
        }

        [Fact]
        public void DoesNotReport_When_Captured_Field_Is_Static()
        {
            // Static fields hold values that do not change between renders, so they are not reactive and must
            // never be flagged — flagging them would be a false positive.
            const string source = @"
namespace MyApp.Pages
{
    public sealed class HomePage
    {
        private static int _shared = 0;
        public void Render()
        {
            global::Velvet.Hooks.UseEffect(() => () => System.Console.WriteLine(_shared), new object[] { });
        }
    }
}";
            var diagnostics = GeneratorTestHelper.RunAnalyzer(source, new UseEffectExhaustiveDepsAnalyzer());
            Assert.Empty(diagnostics.Where(d => d.Id == "VEL100"));
        }

        [Fact]
        public void DoesNotReport_When_Captured_Field_Is_Const()
        {
            // Const fields are compile-time constants and never reactive; capturing one must not produce VEL100.
            const string source = @"
namespace MyApp.Pages
{
    public sealed class HomePage
    {
        private const int Limit = 10;
        public void Render()
        {
            global::Velvet.Hooks.UseEffect(() => () => System.Console.WriteLine(Limit), new object[] { });
        }
    }
}";
            var diagnostics = GeneratorTestHelper.RunAnalyzer(source, new UseEffectExhaustiveDepsAnalyzer());
            Assert.Empty(diagnostics.Where(d => d.Id == "VEL100"));
        }

        [Fact]
        public void DoesNotReport_When_Captured_Field_Is_Readonly_Instance()
        {
            // A readonly instance field is assigned once at construction and never changes between renders
            // (e.g. an injected dependency), so it is not reactive and must not be flagged.
            const string source = @"
namespace MyApp.Pages
{
    public sealed class HomePage
    {
        private readonly int _timeout = 5000;
        public void Render()
        {
            global::Velvet.Hooks.UseEffect(() => () => System.Console.WriteLine(_timeout), new object[] { });
        }
    }
}";
            var diagnostics = GeneratorTestHelper.RunAnalyzer(source, new UseEffectExhaustiveDepsAnalyzer());
            Assert.Empty(diagnostics.Where(d => d.Id == "VEL100"));
        }

        [Fact]
        public void DoesNotReport_When_Captured_Property_Is_Static()
        {
            // Static properties are excluded for the same stability reason as static fields.
            const string source = @"
namespace MyApp.Pages
{
    public sealed class HomePage
    {
        private static int Shared { get; set; }
        public void Render()
        {
            global::Velvet.Hooks.UseEffect(() => () => System.Console.WriteLine(Shared), new object[] { });
        }
    }
}";
            var diagnostics = GeneratorTestHelper.RunAnalyzer(source, new UseEffectExhaustiveDepsAnalyzer());
            Assert.Empty(diagnostics.Where(d => d.Id == "VEL100"));
        }

        [Fact]
        public void Reports_When_Lambda_Captures_ThisQualified_Instance_Field()
        {
            // A `this.`-qualified mutable instance field is a reactive value and must be flagged when absent
            // from deps, exactly like its unqualified form.
            const string source = @"
namespace MyApp.Pages
{
    public sealed class HomePage
    {
        private int _count = 0;
        public void Render()
        {
            global::Velvet.Hooks.UseEffect(() => () => System.Console.WriteLine(this._count), new object[] { });
        }
    }
}";
            var diagnostics = GeneratorTestHelper.RunAnalyzer(source, new UseEffectExhaustiveDepsAnalyzer());
            var vel100 = diagnostics.Where(d => d.Id == "VEL100").ToList();
            Assert.Single(vel100);
            Assert.Contains("_count", vel100[0].GetMessage());
        }

        [Fact]
        public void DoesNotReport_When_Captured_Property_Is_InitOnly()
        {
            // An init-only property is assigned once at construction and never changes between renders, so it
            // is stable and must not be flagged.
            const string source = @"
namespace MyApp.Pages
{
    public sealed class HomePage
    {
        public int Limit { get; init; }
        public void Render()
        {
            global::Velvet.Hooks.UseEffect(() => () => System.Console.WriteLine(Limit), new object[] { });
        }
    }
}";
            var diagnostics = GeneratorTestHelper.RunAnalyzer(source, new UseEffectExhaustiveDepsAnalyzer());
            Assert.Empty(diagnostics.Where(d => d.Id == "VEL100"));
        }
    }
}
