using System.Linq;
using Velvet.SourceGenerators.RulesOfHooks;
using Xunit;

namespace Velvet.SourceGenerators.Tests
{
    /// <summary>Tests for <see cref="RulesOfHooksAnalyzer"/> (VEL101).</summary>
    public class RulesOfHooksAnalyzerTests
    {
        [Fact]
        public void Reports_When_Hook_Called_Inside_If()
        {
            const string source = @"
namespace MyApp.Pages
{
    public static class HomePage
    {
        public static void Render(bool cond)
        {
            if (cond)
            {
                global::Velvet.Hooks.UseEffect(() => () => { }, new object[] { });
            }
        }
    }
}";
            var diagnostics = GeneratorTestHelper.RunAnalyzer(source, new RulesOfHooksAnalyzer());
            var vel101 = diagnostics.Where(d => d.Id == "VEL101").ToList();
            Assert.Single(vel101);
            Assert.Contains("UseEffect", vel101[0].GetMessage());
            Assert.Contains("if/else", vel101[0].GetMessage());
        }

        [Fact]
        public void Reports_When_Hook_Called_After_Conditional_Early_Return()
        {
            const string source = @"
namespace MyApp.Pages
{
    public static class HomePage
    {
        public static void Render(bool skip)
        {
            if (skip) return;
            global::Velvet.Hooks.UseEffect(() => () => { }, new object[] { });
        }
    }
}";
            var diagnostics = GeneratorTestHelper.RunAnalyzer(source, new RulesOfHooksAnalyzer());
            var vel101 = diagnostics.Where(d => d.Id == "VEL101").ToList();
            Assert.Single(vel101);
            Assert.Contains("UseEffect", vel101[0].GetMessage());
        }

        [Fact]
        public void DoesNotReport_When_Hook_Follows_Unconditional_Statements()
        {
            const string source = @"
namespace MyApp.Pages
{
    public static class HomePage
    {
        public static void Render()
        {
            var x = 5;
            var y = x + 1;
            global::Velvet.Hooks.UseEffect(() => () => { }, new object[] { y });
        }
    }
}";
            var diagnostics = GeneratorTestHelper.RunAnalyzer(source, new RulesOfHooksAnalyzer());
            Assert.Empty(diagnostics.Where(d => d.Id == "VEL101"));
        }

        [Fact]
        public void DoesNotReport_When_Hook_Follows_Conditional_Throw()
        {
            // A conditional throw aborts the render entirely; across SUCCESSFUL renders the hook always runs, so
            // it is not a rules-of-hooks violation (only conditional return/break/continue skip the hook).
            const string source = @"
namespace MyApp.Pages
{
    public static class HomePage
    {
        public static void Render(bool bad)
        {
            if (bad) throw new System.Exception();
            global::Velvet.Hooks.UseEffect(() => () => { }, new object[] { });
        }
    }
}";
            var diagnostics = GeneratorTestHelper.RunAnalyzer(source, new RulesOfHooksAnalyzer());
            Assert.Empty(diagnostics.Where(d => d.Id == "VEL101"));
        }

        [Fact]
        public void Reports_When_Hook_Called_Inside_For_Loop()
        {
            const string source = @"
namespace MyApp.Pages
{
    public static class HomePage
    {
        public static void Render()
        {
            for (int i = 0; i < 3; i++)
            {
                global::Velvet.Hooks.UseState(0);
            }
        }
    }
}";
            var diagnostics = GeneratorTestHelper.RunAnalyzer(source, new RulesOfHooksAnalyzer());
            Assert.Single(diagnostics.Where(d => d.Id == "VEL101"));
        }

        [Fact]
        public void Reports_When_Hook_Called_Inside_Nested_Lambda()
        {
            const string source = @"
namespace MyApp.Pages
{
    public static class HomePage
    {
        public static void Render()
        {
            global::System.Action a = () =>
            {
                global::Velvet.Hooks.UseState(0);
            };
        }
    }
}";
            var diagnostics = GeneratorTestHelper.RunAnalyzer(source, new RulesOfHooksAnalyzer());
            Assert.Single(diagnostics.Where(d => d.Id == "VEL101"));
        }

        [Fact]
        public void Reports_When_Hook_Called_Inside_Conditional_Expression()
        {
            const string source = @"
namespace MyApp.Pages
{
    public static class HomePage
    {
        public static int Render(bool cond)
        {
            return cond ? global::Velvet.Hooks.UseState(0).value : 0;
        }
    }
}";
            var diagnostics = GeneratorTestHelper.RunAnalyzer(source, new RulesOfHooksAnalyzer());
            Assert.Single(diagnostics.Where(d => d.Id == "VEL101"));
        }

        [Fact]
        public void Reports_When_Hook_Called_After_Short_Circuit_AND()
        {
            const string source = @"
namespace MyApp.Pages
{
    public static class HomePage
    {
        public static bool Render(bool cond)
        {
            return cond && global::Velvet.Hooks.UseState(false).value;
        }
    }
}";
            var diagnostics = GeneratorTestHelper.RunAnalyzer(source, new RulesOfHooksAnalyzer());
            Assert.Single(diagnostics.Where(d => d.Id == "VEL101"));
        }

        [Fact]
        public void DoesNotReport_When_Hook_Called_At_Method_Top_Level()
        {
            const string source = @"
namespace MyApp.Pages
{
    public static class HomePage
    {
        public static void Render()
        {
            global::Velvet.Hooks.UseEffect(() => () => { }, new object[] { });
            global::Velvet.Hooks.UseState(0);
        }
    }
}";
            var diagnostics = GeneratorTestHelper.RunAnalyzer(source, new RulesOfHooksAnalyzer());
            Assert.Empty(diagnostics.Where(d => d.Id == "VEL101"));
        }

        [Fact]
        public void DoesNotReport_When_Hook_Called_Inside_Block_Without_Control_Flow()
        {
            const string source = @"
namespace MyApp.Pages
{
    public static class HomePage
    {
        public static void Render()
        {
            // A simple nested block (no if/loop/lambda) is structurally sequential — Hooks call ordering
            // is unaffected, so no warning should fire.
            {
                global::Velvet.Hooks.UseState(0);
            }
        }
    }
}";
            var diagnostics = GeneratorTestHelper.RunAnalyzer(source, new RulesOfHooksAnalyzer());
            Assert.Empty(diagnostics.Where(d => d.Id == "VEL101"));
        }

        [Fact]
        public void Reports_When_Hook_Called_Inside_Try_Block()
        {
            const string source = @"
namespace MyApp.Pages
{
    public static class HomePage
    {
        public static void Render()
        {
            try
            {
                global::Velvet.Hooks.UseState(0);
            }
            catch { }
        }
    }
}";
            var diagnostics = GeneratorTestHelper.RunAnalyzer(source, new RulesOfHooksAnalyzer());
            Assert.Single(diagnostics.Where(d => d.Id == "VEL101"));
        }

        [Fact]
        public void Reports_When_Hook_Called_Inside_Catch_Block()
        {
            const string source = @"
namespace MyApp.Pages
{
    public static class HomePage
    {
        public static void Render()
        {
            try { }
            catch
            {
                global::Velvet.Hooks.UseState(0);
            }
        }
    }
}";
            var diagnostics = GeneratorTestHelper.RunAnalyzer(source, new RulesOfHooksAnalyzer());
            Assert.Single(diagnostics.Where(d => d.Id == "VEL101"));
        }

        [Fact]
        public void DoesNotReport_When_Hook_Called_Inside_Finally_Block()
        {
            // finally runs unconditionally on every exit path so hook ordering is invariant;
            // the analyzer intentionally excludes FinallyClause from the control-flow list.
            const string source = @"
namespace MyApp.Pages
{
    public static class HomePage
    {
        public static void Render()
        {
            try { }
            finally
            {
                global::Velvet.Hooks.UseState(0);
            }
        }
    }
}";
            var diagnostics = GeneratorTestHelper.RunAnalyzer(source, new RulesOfHooksAnalyzer());
            Assert.Empty(diagnostics.Where(d => d.Id == "VEL101"));
        }

        [Fact]
        public void Reports_When_Hook_Called_Inside_Do_While_Loop()
        {
            const string source = @"
namespace MyApp.Pages
{
    public static class HomePage
    {
        public static void Render()
        {
            int i = 0;
            do
            {
                global::Velvet.Hooks.UseState(0);
                i++;
            } while (i < 3);
        }
    }
}";
            var diagnostics = GeneratorTestHelper.RunAnalyzer(source, new RulesOfHooksAnalyzer());
            Assert.Single(diagnostics.Where(d => d.Id == "VEL101"));
        }

        [Fact]
        public void Reports_When_Custom_Hook_Called_Inside_If()
        {
            // Any `Use` + uppercase method is treated as a hook.
            // A custom hook like Auth.UseCurrentUser (wraps Hooks.UseContext internally) must obey
            // the hook-ordering rule the same way the built-in Velvet.Hooks.UseXxx does.
            const string source = @"
namespace MyApp.Pages
{
    public static class Auth { public static int UseCurrentUser() => 0; }
    public static class HomePage
    {
        public static void Render(bool cond)
        {
            if (cond) { var _ = global::MyApp.Pages.Auth.UseCurrentUser(); }
        }
    }
}";
            var diagnostics = GeneratorTestHelper.RunAnalyzer(source, new RulesOfHooksAnalyzer());
            var vel101 = diagnostics.Where(d => d.Id == "VEL101").ToList();
            Assert.Single(vel101);
            Assert.Contains("UseCurrentUser", vel101[0].GetMessage());
        }

        [Fact]
        public void DoesNotReport_When_Method_Name_Does_Not_Match_Hook_Convention()
        {
            // `Useless` / `Used` and similar prefix-only collisions must NOT be flagged — only
            // camelCase `Use` + uppercase letter matches the hook convention.
            const string source = @"
namespace MyApp.Pages
{
    public static class Util
    {
        public static int Useless() => 0;
        public static int Used() => 0;
    }
    public static class HomePage
    {
        public static void Render(bool cond)
        {
            if (cond)
            {
                var _ = global::MyApp.Pages.Util.Useless();
                var __ = global::MyApp.Pages.Util.Used();
            }
        }
    }
}";
            var diagnostics = GeneratorTestHelper.RunAnalyzer(source, new RulesOfHooksAnalyzer());
            Assert.Empty(diagnostics.Where(d => d.Id == "VEL101"));
        }

        [Fact]
        public void Reports_When_Local_Function_Custom_Hook_Called_Inside_If()
        {
            // Local-function custom hooks (declared inside Render) are the canonical pattern for
            // in-method abstractions. Bare-identifier invocations must be inspected the same way as
            // member-access invocations — otherwise this common shape silently bypasses the rule.
            const string source = @"
namespace MyApp.Pages
{
    public static class HomePage
    {
        public static void Render(bool cond)
        {
            int UseLocal() => global::Velvet.Hooks.UseState(0).value;
            if (cond) { var _ = UseLocal(); }
        }
    }
}";
            var diagnostics = GeneratorTestHelper.RunAnalyzer(source, new RulesOfHooksAnalyzer());
            Assert.Single(diagnostics.Where(d => d.Id == "VEL101"));
        }

        [Fact]
        public void Reports_When_Instance_Custom_Hook_Called_Inside_If()
        {
            // Instance method (not static) must also be flagged — the analyzer's gate is the name
            // convention, not the receiver shape.
            const string source = @"
namespace MyApp.Pages
{
    public sealed class Auth { public int UseCurrentUser() => 0; }
    public static class HomePage
    {
        public static void Render(bool cond)
        {
            var a = new Auth();
            if (cond) { var _ = a.UseCurrentUser(); }
        }
    }
}";
            var diagnostics = GeneratorTestHelper.RunAnalyzer(source, new RulesOfHooksAnalyzer());
            Assert.Single(diagnostics.Where(d => d.Id == "VEL101"));
        }

        [Fact]
        public void DoesNotReport_When_Method_Name_Is_Exactly_Use()
        {
            // Length-boundary: `Use()` (length 3) has no 4th char and must NOT be flagged. Velvet
            // ships a real `Hooks.Use<T>(factory)` (Suspense API) — the convention exempts it from
            // Rules-of-Hooks because it isn't a stateful slot hook.
            const string source = @"
namespace MyApp.Pages
{
    public static class Util { public static int Use() => 0; }
    public static class HomePage
    {
        public static void Render(bool cond)
        {
            if (cond) { var _ = global::MyApp.Pages.Util.Use(); }
        }
    }
}";
            var diagnostics = GeneratorTestHelper.RunAnalyzer(source, new RulesOfHooksAnalyzer());
            Assert.Empty(diagnostics.Where(d => d.Id == "VEL101"));
        }

        [Fact]
        public void DoesNotReport_When_4th_Char_Is_Non_Ascii_Uppercase()
        {
            // ASCII-only uppercase check (the 4th char must match [A-Z]). A method whose 4th char is a
            // Unicode uppercase letter outside A-Z (e.g., Cyrillic) is NOT a hook by this
            // convention.
            const string source = @"
namespace MyApp.Pages
{
    public static class Util { public static int UseЛог() => 0; }
    public static class HomePage
    {
        public static void Render(bool cond)
        {
            if (cond) { var _ = global::MyApp.Pages.Util.UseЛог(); }
        }
    }
}";
            var diagnostics = GeneratorTestHelper.RunAnalyzer(source, new RulesOfHooksAnalyzer());
            Assert.Empty(diagnostics.Where(d => d.Id == "VEL101"));
        }

        [Fact]
        public void DoesNotReport_When_Custom_Hook_Called_At_Method_Top_Level()
        {
            // Custom hook called unconditionally (no enclosing control-flow): OK.
            const string source = @"
namespace MyApp.Pages
{
    public static class Auth { public static int UseCurrentUser() => 0; }
    public static class HomePage
    {
        public static void Render()
        {
            var _ = global::MyApp.Pages.Auth.UseCurrentUser();
        }
    }
}";
            var diagnostics = GeneratorTestHelper.RunAnalyzer(source, new RulesOfHooksAnalyzer());
            Assert.Empty(diagnostics.Where(d => d.Id == "VEL101"));
        }

        [Fact]
        public void Reports_When_Hook_Called_Inside_Switch_Case()
        {
            const string source = @"
namespace MyApp.Pages
{
    public static class HomePage
    {
        public static void Render(int n)
        {
            switch (n)
            {
                case 1:
                    global::Velvet.Hooks.UseState(0);
                    break;
                default:
                    break;
            }
        }
    }
}";
            var diagnostics = GeneratorTestHelper.RunAnalyzer(source, new RulesOfHooksAnalyzer());
            Assert.Single(diagnostics.Where(d => d.Id == "VEL101"));
        }
    }
}
