using System.Linq;
using Velvet.SourceGenerators.RulesOfHooks;
using Xunit;

namespace Velvet.SourceGenerators.Tests
{
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
        [global::Velvet.Component]
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
        [global::Velvet.Component]
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
        [global::Velvet.Component]
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
        [global::Velvet.Component]
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
        [global::Velvet.Component]
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
        [global::Velvet.Component]
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
        [global::Velvet.Component]
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
        [global::Velvet.Component]
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
        [global::Velvet.Component]
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
        [global::Velvet.Component]
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
        [global::Velvet.Component]
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
        [global::Velvet.Component]
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
        [global::Velvet.Component]
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
        [global::Velvet.Component]
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
        [global::Velvet.Component]
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
        [global::Velvet.Component]
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
        [global::Velvet.Component]
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
        public void Given_HookNamedMethodCalledOnALowercaseLocal_When_CalledInsideIf_Then_ReportsNothing()
        {
            // Arrange — eslint-plugin-react-hooks counts a member call as a hook only on a receiver whose name
            // starts with an uppercase letter, so a method of an instance held in a local is an ordinary call.
            const string source = @"
namespace MyApp.Pages
{
    public sealed class Auth { public int UseCurrentUser() => 0; }
    public static class HomePage
    {
        [global::Velvet.Component]
        public static void Render(bool cond)
        {
            var a = new Auth();
            if (cond) { var _ = a.UseCurrentUser(); }
        }
    }
}";

            // Act
            var diagnostics = GeneratorTestHelper.RunAnalyzerOnCompilingSource(source, new RulesOfHooksAnalyzer());

            // Assert
            Assert.Empty(Describe(diagnostics));
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
        [global::Velvet.Component]
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
        [global::Velvet.Component]
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
            // Unconditional top-level calls execute in the same order on every render, so hook-state slots stay
            // aligned; nothing to flag.
            const string source = @"
namespace MyApp.Pages
{
    public static class Auth { public static int UseCurrentUser() => 0; }
    public static class HomePage
    {
        [global::Velvet.Component]
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
        [global::Velvet.Component]
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

        [Fact]
        public void Given_HookInPlainHelperCalledConditionally_When_Analyzed_Then_ReportsVel102OnTheHelpersHook()
        {
            // Arrange
            const string source = @"
using Velvet;

internal static class Panel
{
    [Component]
    internal static VNode Render()
    {
        var (open, setOpen) = Hooks.UseState(false);
        return open ? Sheet() : null;
    }

    private static VNode Sheet()
    {
        var (tab, setTab) = Hooks.UseState(0);
        return null;
    }
}";

            // Act
            var diagnostics = GeneratorTestHelper.RunAnalyzerOnCompilingSource(source, new RulesOfHooksAnalyzer());

            // Assert
            Assert.Equal(new[] { "VEL102 Hooks.UseState(0) 'UseState' 'Sheet'" }, Describe(diagnostics));
        }

        [Fact]
        public void Given_HookInUseNamedHelper_When_CalledUnconditionally_Then_ReportsNothing()
        {
            // Arrange
            const string source = @"
using Velvet;

internal static class Panel
{
    [Component]
    internal static VNode Render()
    {
        var (open, setOpen) = Hooks.UseState(false);
        return UseSheet();
    }

    private static VNode UseSheet()
    {
        var (tab, setTab) = Hooks.UseState(0);
        return null;
    }
}";

            // Act
            var diagnostics = GeneratorTestHelper.RunAnalyzerOnCompilingSource(source, new RulesOfHooksAnalyzer());

            // Assert
            Assert.Empty(Describe(diagnostics));
        }

        [Fact]
        public void Given_HookInUseNamedHelper_When_CalledConditionally_Then_ReportsOnlyVel101AtTheCall()
        {
            // Arrange
            const string source = @"
using Velvet;

internal static class Panel
{
    [Component]
    internal static VNode Render()
    {
        var (open, setOpen) = Hooks.UseState(false);
        return open ? UseSheet() : null;
    }

    private static VNode UseSheet()
    {
        var (tab, setTab) = Hooks.UseState(0);
        return null;
    }
}";

            // Act
            var diagnostics = GeneratorTestHelper.RunAnalyzerOnCompilingSource(source, new RulesOfHooksAnalyzer());

            // Assert
            Assert.Equal(new[] { "VEL101 UseSheet() 'UseSheet'" }, Describe(diagnostics));
        }

        [Fact]
        public void Given_HookInComponentHelper_When_MountedAsMethodGroup_Then_ReportsNothing()
        {
            // Arrange
            const string source = @"
using Velvet;

internal static class Panel
{
    [Component]
    internal static VNode Render()
    {
        var (open, setOpen) = Hooks.UseState(false);
        System.Func<VNode> sheet = Panel.Sheet;
        return open ? sheet() : null;
    }

    [Component]
    internal static VNode Sheet()
    {
        var (tab, setTab) = Hooks.UseState(0);
        return null;
    }
}";

            // Act
            var diagnostics = GeneratorTestHelper.RunAnalyzerOnCompilingSource(source, new RulesOfHooksAnalyzer());

            // Assert
            Assert.Empty(Describe(diagnostics));
        }

        [Fact]
        public void Given_HookInHelperMarkedWithAnotherNamespacesComponentAttribute_When_Analyzed_Then_ReportsVel102()
        {
            // Arrange
            const string source = @"
using Velvet;

namespace MyApp
{
    [System.AttributeUsage(System.AttributeTargets.Method)]
    internal sealed class ComponentAttribute : System.Attribute { }

    internal static class Panel
    {
        [Component]
        internal static VNode Sheet()
        {
            var (tab, setTab) = Hooks.UseState(0);
            return null;
        }
    }
}";

            // Act
            var diagnostics = GeneratorTestHelper.RunAnalyzerOnCompilingSource(source, new RulesOfHooksAnalyzer());

            // Assert
            Assert.Equal(new[] { "VEL102 Hooks.UseState(0) 'UseState' 'Sheet'" }, Describe(diagnostics));
        }

        [Fact]
        public void Given_HookInLocalFunctionNamedUse_When_CalledUnconditionally_Then_ReportsNothing()
        {
            // Arrange
            const string source = @"
using Velvet;

internal static class Panel
{
    [Component]
    internal static VNode Render()
    {
        int UseTab() => Hooks.UseState(0).value;
        var tab = UseTab();
        return null;
    }
}";

            // Act
            var diagnostics = GeneratorTestHelper.RunAnalyzerOnCompilingSource(source, new RulesOfHooksAnalyzer());

            // Assert
            Assert.Empty(Describe(diagnostics));
        }

        [Fact]
        public void Given_HookInLocalFunctionNotNamedUse_When_Analyzed_Then_ReportsVel102NamingTheLocalFunction()
        {
            // Arrange
            const string source = @"
using Velvet;

internal static class Panel
{
    [Component]
    internal static VNode Render()
    {
        int Tab() => Hooks.UseState(0).value;
        var tab = Tab();
        return null;
    }
}";

            // Act
            var diagnostics = GeneratorTestHelper.RunAnalyzerOnCompilingSource(source, new RulesOfHooksAnalyzer());

            // Assert
            Assert.Equal(new[] { "VEL102 Hooks.UseState(0) 'UseState' 'Tab'" }, Describe(diagnostics));
        }

        [Fact]
        public void Given_HookInHelperWhoseNameOnlyStartsWithUse_When_Analyzed_Then_ReportsVel102()
        {
            // Arrange
            const string source = @"
using Velvet;

internal static class Panel
{
    private static int Useful() => Hooks.UseState(0).value;
}";

            // Act
            var diagnostics = GeneratorTestHelper.RunAnalyzerOnCompilingSource(source, new RulesOfHooksAnalyzer());

            // Assert
            Assert.Equal(new[] { "VEL102 Hooks.UseState(0) 'UseState' 'Useful'" }, Describe(diagnostics));
        }

        [Fact]
        public void Given_HookInLambdaInsidePlainHelper_When_Analyzed_Then_ReportsNothing()
        {
            // Arrange
            const string source = @"
using Velvet;

internal static class Panel
{
    private static System.Func<int> Tab() => () => Hooks.UseState(0).value;
}";

            // Act
            var diagnostics = GeneratorTestHelper.RunAnalyzerOnCompilingSource(source, new RulesOfHooksAnalyzer());

            // Assert
            Assert.Empty(Describe(diagnostics));
        }

        [Fact]
        public void Given_HookInPropertyGetter_When_Analyzed_Then_ReportsVel102NamingTheProperty()
        {
            // Arrange
            const string source = @"
using Velvet;

internal static class Panel
{
    private static int Tab => Hooks.UseState(0).value;
}";

            // Act
            var diagnostics = GeneratorTestHelper.RunAnalyzerOnCompilingSource(source, new RulesOfHooksAnalyzer());

            // Assert
            Assert.Equal(new[] { "VEL102 Hooks.UseState(0) 'UseState' 'Tab'" }, Describe(diagnostics));
        }

        [Fact]
        public void Given_HookInConstructor_When_Analyzed_Then_ReportsVel102NamingTheType()
        {
            // Arrange
            const string source = @"
using Velvet;

internal sealed class Panel
{
    private readonly int _tab;

    internal Panel()
    {
        _tab = Hooks.UseState(0).value;
    }
}";

            // Act
            var diagnostics = GeneratorTestHelper.RunAnalyzerOnCompilingSource(source, new RulesOfHooksAnalyzer());

            // Assert
            Assert.Equal(new[] { "VEL102 Hooks.UseState(0) 'UseState' 'Panel'" }, Describe(diagnostics));
        }

        [Fact]
        public void Given_HookInPlainHelperInGeneratedCode_When_Analyzed_Then_ReportsNothing()
        {
            // Arrange
            const string source = @"// <auto-generated/>
using Velvet;

internal static class Panel
{
    private static int Tab() => Hooks.UseState(0).value;
}";

            // Act
            var diagnostics = GeneratorTestHelper.RunAnalyzerOnCompilingSource(source, new RulesOfHooksAnalyzer());

            // Assert
            Assert.Empty(Describe(diagnostics));
        }

        [Fact]
        public void Given_BareGenericHookCallInsideIf_When_Analyzed_Then_ReportsVel101()
        {
            // Arrange
            const string source = @"
using Velvet;

internal static class Panel
{
    [Component]
    internal static VNode Render(bool open)
    {
        if (open)
        {
            UseTab<int>();
        }
        return null;
    }

    private static T UseTab<T>() => Hooks.UseState(default(T)).value;
}";

            // Act
            var diagnostics = GeneratorTestHelper.RunAnalyzerOnCompilingSource(source, new RulesOfHooksAnalyzer());

            // Assert
            Assert.Equal(new[] { "VEL101 UseTab<int>() 'UseTab'" }, Describe(diagnostics));
        }

        [Fact]
        public void Given_BareGenericHookCallInPlainHelper_When_Analyzed_Then_ReportsVel102()
        {
            // Arrange
            const string source = @"
using Velvet;

internal static class Panel
{
    private static int Sheet() => UseTab<int>();

    private static T UseTab<T>() => Hooks.UseState(default(T)).value;
}";

            // Act
            var diagnostics = GeneratorTestHelper.RunAnalyzerOnCompilingSource(source, new RulesOfHooksAnalyzer());

            // Assert
            Assert.Equal(new[] { "VEL102 UseTab<int>() 'UseTab' 'Sheet'" }, Describe(diagnostics));
        }

        [Fact]
        public void Given_HookNamedMethodCalledOnALowercaseParameterInPlainHelper_When_Analyzed_Then_ReportsNothing()
        {
            // Arrange
            const string source = @"
using Velvet;

internal sealed class Service
{
    public int UseDefaults() => 0;
}

internal static class Panel
{
    private static int Sheet(Service svc) => svc.UseDefaults();
}";

            // Act
            var diagnostics = GeneratorTestHelper.RunAnalyzerOnCompilingSource(source, new RulesOfHooksAnalyzer());

            // Assert
            Assert.Empty(Describe(diagnostics));
        }

        [Fact]
        public void Given_HookInLambdaInsideCustomHook_When_Analyzed_Then_ReportsVel101ForTheLambda()
        {
            // Arrange
            const string source = @"
using Velvet;

internal static class Panel
{
    private static System.Func<int> UseTab() => () => Hooks.UseState(0).value;
}";

            // Act
            var diagnostics = GeneratorTestHelper.RunAnalyzerOnCompilingSource(source, new RulesOfHooksAnalyzer());

            // Assert
            Assert.Equal(new[] { "VEL101 a nested lambda or anonymous method" }, DescribeConstruct(diagnostics));
        }

        [Fact]
        public void Given_HookInMethodMountedByGenericVComponent_When_Analyzed_Then_ReportsNothing()
        {
            // Arrange
            const string source = @"
using Velvet;

internal static class Panel
{
    [Component]
    internal static VNode Render() => V.Component<int>(Row, 1);

    private static VNode Row(int index)
    {
        var (tab, setTab) = Hooks.UseState(index);
        return null;
    }
}";

            // Act
            var diagnostics = GeneratorTestHelper.RunAnalyzerOnCompilingSource(source, new RulesOfHooksAnalyzer());

            // Assert
            Assert.Empty(Describe(diagnostics));
        }

        [Fact]
        public void Given_ComponentCalledAsTheWholeRenderBodyOfALambda_When_Analyzed_Then_ReportsNothing()
        {
            // Arrange
            const string source = @"
using Velvet;

internal static class Panel
{
    [Component]
    internal static VNode Render() => V.Component(() => Sheet());

    [Component]
    internal static VNode Sheet()
    {
        var (tab, setTab) = Hooks.UseState(0);
        return null;
    }
}";

            // Act
            var diagnostics = GeneratorTestHelper.RunAnalyzerOnCompilingSource(source, new RulesOfHooksAnalyzer());

            // Assert
            Assert.Empty(Describe(diagnostics));
        }

        [Fact]
        public void Given_ComponentCalledConditionallyInsideARenderLambda_When_Analyzed_Then_ReportsVel103()
        {
            // Arrange
            const string source = @"
using Velvet;

internal static class Panel
{
    [Component]
    internal static VNode Render(bool open) => V.Component(() => open ? Sheet() : null);

    [Component]
    internal static VNode Sheet()
    {
        var (tab, setTab) = Hooks.UseState(0);
        return null;
    }
}";

            // Act
            var diagnostics = GeneratorTestHelper.RunAnalyzerOnCompilingSource(source, new RulesOfHooksAnalyzer());

            // Assert
            Assert.Equal(new[] { "VEL103 Sheet() 'Sheet'" }, Describe(diagnostics));
        }

        [Fact]
        public void Given_HookInMethodNamedUse_When_Analyzed_Then_ReportsNothing()
        {
            // Arrange — eslint counts a function named `use` as a hook, which Velvet spells Use.
            const string source = @"
using Velvet;

internal static class Panel
{
    private static int Use() => Hooks.UseState(0).value;
}";

            // Act
            var diagnostics = GeneratorTestHelper.RunAnalyzerOnCompilingSource(source, new RulesOfHooksAnalyzer());

            // Assert
            Assert.Empty(Describe(diagnostics));
        }

        [Fact]
        public void Given_HookInMethodMountedByQualifiedNameFromAnotherType_When_Analyzed_Then_ReportsNothing()
        {
            // Arrange
            const string source = @"
using Velvet;

internal static class Rows
{
    internal static VNode Row(int index)
    {
        var (tab, setTab) = Hooks.UseState(index);
        return null;
    }
}

internal static class Panel
{
    [Component]
    internal static VNode Render() => V.Component(Rows.Row, 1);
}";

            // Act
            var diagnostics = GeneratorTestHelper.RunAnalyzerOnCompilingSource(source, new RulesOfHooksAnalyzer());

            // Assert
            Assert.Empty(Describe(diagnostics));
        }

        [Fact]
        public void Given_HookInMethodHandedToAComponentMethodNotOnV_When_Analyzed_Then_ReportsVel102()
        {
            // Arrange
            const string source = @"
using Velvet;

internal static class Factory
{
    internal static VNode Component(System.Func<VNode> body) => body();
}

internal static class Panel
{
    [Component]
    internal static VNode Render() => Factory.Component(Sheet);

    private static VNode Sheet()
    {
        var (tab, setTab) = Hooks.UseState(0);
        return null;
    }
}";

            // Act
            var diagnostics = GeneratorTestHelper.RunAnalyzerOnCompilingSource(source, new RulesOfHooksAnalyzer());

            // Assert
            Assert.Equal(new[] { "VEL102 Hooks.UseState(0) 'UseState' 'Sheet'" }, Describe(diagnostics));
        }

        /// <summary>
        /// Each diagnostic as its ID, the source it is reported on and the quoted names its message carries, so a
        /// case pins which call is reported and which host is named without pinning the message's prose.
        /// </summary>
        private static string[] Describe(System.Collections.Immutable.ImmutableArray<Microsoft.CodeAnalysis.Diagnostic> diagnostics) =>
            diagnostics
                .Select(d => string.Join(
                    " ",
                    new[] { d.Id, d.Location.SourceTree!.GetText().ToString(d.Location.SourceSpan) }
                        .Concat(System.Text.RegularExpressions.Regex.Matches(d.GetMessage(), "'[^']*'")
                            .Select(match => match.Value))))
                .ToArray();

        [Fact]
        public void Given_ConditionalHookInPlainHelper_When_Analyzed_Then_ReportsOnlyVel102()
        {
            // Arrange
            const string source = @"
using Velvet;

internal static class Panel
{
    private static int Tab(bool open)
    {
        if (open)
        {
            return Hooks.UseState(0).value;
        }
        return 0;
    }
}";

            // Act
            var diagnostics = GeneratorTestHelper.RunAnalyzerOnCompilingSource(source, new RulesOfHooksAnalyzer());

            // Assert
            Assert.Equal(new[] { "VEL102 Hooks.UseState(0) 'UseState' 'Tab'" }, Describe(diagnostics));
        }

        [Fact]
        public void Given_HookInMethodMountedByNameWithVComponent_When_Analyzed_Then_ReportsNothing()
        {
            // Arrange
            const string source = @"
using Velvet;

internal static class Panel
{
    [Component]
    internal static VNode Render() => V.Component(Row, 1);

    private static VNode Row(int index)
    {
        var (tab, setTab) = Hooks.UseState(index);
        return null;
    }
}";

            // Act
            var diagnostics = GeneratorTestHelper.RunAnalyzerOnCompilingSource(source, new RulesOfHooksAnalyzer());

            // Assert
            Assert.Empty(Describe(diagnostics));
        }

        [Fact]
        public void Given_HookInMethodMountedByNameWithVMemo_When_Analyzed_Then_ReportsNothing()
        {
            // Arrange
            const string source = @"
using Velvet;

internal static class Panel
{
    [Component]
    internal static VNode Render() => V.Memo(Row, 1, (previous, next) => previous == next);

    private static VNode Row(int index)
    {
        var (tab, setTab) = Hooks.UseState(index);
        return null;
    }
}";

            // Act
            var diagnostics = GeneratorTestHelper.RunAnalyzerOnCompilingSource(source, new RulesOfHooksAnalyzer());

            // Assert
            Assert.Empty(Describe(diagnostics));
        }

        [Fact]
        public void Given_HookInMethodHandedToVMemoized_When_Analyzed_Then_ReportsVel102()
        {
            // Arrange
            const string source = @"
using Velvet;

internal static class Panel
{
    [Component]
    internal static VNode Render() => V.Memoized(Sheet);

    private static VNode Sheet()
    {
        var (tab, setTab) = Hooks.UseState(0);
        return null;
    }
}";

            // Act
            var diagnostics = GeneratorTestHelper.RunAnalyzerOnCompilingSource(source, new RulesOfHooksAnalyzer());

            // Assert
            Assert.Equal(new[] { "VEL102 Hooks.UseState(0) 'UseState' 'Sheet'" }, Describe(diagnostics));
        }

        [Fact]
        public void Given_HookInMethodHandedToVMemoAsItsComparer_When_Analyzed_Then_ReportsVel102()
        {
            // Arrange
            const string source = @"
using Velvet;

internal static class Panel
{
    [Component]
    internal static VNode Render() => V.Memo(Row, 1, Same);

    [Component]
    private static VNode Row(int index) => null;

    private static bool Same(int previous, int next) => Hooks.UseState(previous).value == next;
}";

            // Act
            var diagnostics = GeneratorTestHelper.RunAnalyzerOnCompilingSource(source, new RulesOfHooksAnalyzer());

            // Assert
            Assert.Equal(new[] { "VEL102 Hooks.UseState(previous) 'UseState' 'Same'" }, Describe(diagnostics));
        }

        [Fact]
        public void Given_HookInLambdaMountedWithVComponent_When_Analyzed_Then_ReportsNothing()
        {
            // Arrange
            const string source = @"
using Velvet;

internal static class Panel
{
    private static VNode Mount() => V.Component(() =>
    {
        var (tab, setTab) = Hooks.UseState(0);
        return null;
    });
}";

            // Act
            var diagnostics = GeneratorTestHelper.RunAnalyzerOnCompilingSource(source, new RulesOfHooksAnalyzer());

            // Assert
            Assert.Empty(Describe(diagnostics));
        }

        [Fact]
        public void Given_ConditionalHookInLambdaMountedWithVComponent_When_Analyzed_Then_ReportsVel101ForTheCondition()
        {
            // Arrange
            const string source = @"
using Velvet;

internal static class Panel
{
    private static VNode Mount(bool open) => V.Component(() =>
    {
        if (open)
        {
            Hooks.UseState(0);
        }
        return null;
    });
}";

            // Act
            var diagnostics = GeneratorTestHelper.RunAnalyzerOnCompilingSource(source, new RulesOfHooksAnalyzer());

            // Assert
            Assert.Equal(new[] { "VEL101 an if/else branch" }, DescribeConstruct(diagnostics));
        }

        [Fact]
        public void Given_ComponentCallingAHookCalledDirectly_When_Analyzed_Then_ReportsVel103AtTheCall()
        {
            // Arrange
            const string source = @"
using Velvet;

internal static class Panel
{
    [Component]
    internal static VNode Render()
    {
        var (open, setOpen) = Hooks.UseState(false);
        return open ? Sheet() : null;
    }

    [Component]
    internal static VNode Sheet()
    {
        var (tab, setTab) = Hooks.UseState(0);
        return null;
    }
}";

            // Act
            var diagnostics = GeneratorTestHelper.RunAnalyzerOnCompilingSource(source, new RulesOfHooksAnalyzer());

            // Assert
            Assert.Equal(new[] { "VEL103 Sheet() 'Sheet'" }, Describe(diagnostics));
        }

        [Fact]
        public void Given_MountedMethodCallingAHookCalledDirectly_When_Analyzed_Then_ReportsVel103AtTheCall()
        {
            // Arrange
            const string source = @"
using Velvet;

internal static class Panel
{
    [Component]
    internal static VNode Render() => V.Component(Row, 1);

    [Component]
    internal static VNode Other() => Row(2);

    private static VNode Row(int index)
    {
        var (tab, setTab) = Hooks.UseState(index);
        return null;
    }
}";

            // Act
            var diagnostics = GeneratorTestHelper.RunAnalyzerOnCompilingSource(source, new RulesOfHooksAnalyzer());

            // Assert
            Assert.Equal(new[] { "VEL103 Row(2) 'Row'" }, Describe(diagnostics));
        }

        [Fact]
        public void Given_ComponentCallingNoHookCalledDirectly_When_Analyzed_Then_ReportsNothing()
        {
            // Arrange
            const string source = @"
using Velvet;

internal static class Panel
{
    [Component]
    internal static VNode Render()
    {
        var (open, setOpen) = Hooks.UseState(false);
        return open ? Sheet() : null;
    }

    [Component]
    internal static VNode Sheet() => null;
}";

            // Act
            var diagnostics = GeneratorTestHelper.RunAnalyzerOnCompilingSource(source, new RulesOfHooksAnalyzer());

            // Assert
            Assert.Empty(Describe(diagnostics));
        }

        [Fact]
        public void Given_HookInFieldInitializer_When_Analyzed_Then_ReportsVel102NamingTheField()
        {
            // Arrange
            const string source = @"
using Velvet;

internal static class Panel
{
    private static readonly int s_tab = Hooks.UseState(0).value;
}";

            // Act
            var diagnostics = GeneratorTestHelper.RunAnalyzerOnCompilingSource(source, new RulesOfHooksAnalyzer());

            // Assert
            Assert.Equal(new[] { "VEL102 Hooks.UseState(0) 'UseState' 's_tab'" }, Describe(diagnostics));
        }

        [Fact]
        public void Given_HookInPropertyInitializer_When_Analyzed_Then_ReportsVel102NamingTheProperty()
        {
            // Arrange
            const string source = @"
using Velvet;

internal static class Panel
{
    private static int Tab { get; } = Hooks.UseState(0).value;
}";

            // Act
            var diagnostics = GeneratorTestHelper.RunAnalyzerOnCompilingSource(source, new RulesOfHooksAnalyzer());

            // Assert
            Assert.Equal(new[] { "VEL102 Hooks.UseState(0) 'UseState' 'Tab'" }, Describe(diagnostics));
        }

        [Fact]
        public void Given_HookInFinallyInsideLambda_When_Analyzed_Then_ReportsVel101ForTheLambda()
        {
            // Arrange
            const string source = @"
using Velvet;

internal static class Panel
{
    [Component]
    internal static VNode Render()
    {
        System.Action later = () =>
        {
            try { }
            finally
            {
                Hooks.UseState(0);
            }
        };
        return null;
    }
}";

            // Act
            var diagnostics = GeneratorTestHelper.RunAnalyzerOnCompilingSource(source, new RulesOfHooksAnalyzer());

            // Assert
            Assert.Equal(new[] { "VEL101 a nested lambda or anonymous method" }, DescribeConstruct(diagnostics));
        }

        [Fact]
        public void Given_HookInFinallyInsideIf_When_Analyzed_Then_ReportsVel101ForTheIf()
        {
            // Arrange
            const string source = @"
using Velvet;

internal static class Panel
{
    [Component]
    internal static VNode Render(bool open)
    {
        if (open)
        {
            try { }
            finally
            {
                Hooks.UseState(0);
            }
        }
        return null;
    }
}";

            // Act
            var diagnostics = GeneratorTestHelper.RunAnalyzerOnCompilingSource(source, new RulesOfHooksAnalyzer());

            // Assert
            Assert.Equal(new[] { "VEL101 an if/else branch" }, DescribeConstruct(diagnostics));
        }

        /// <summary>
        /// Each diagnostic as its ID and, for VEL101, the construct its message names — the part of the message
        /// <see cref="Describe"/> leaves out, which is what separates a hook judged against a lambda from one
        /// judged against a condition inside it.
        /// </summary>
        private static string[] DescribeConstruct(System.Collections.Immutable.ImmutableArray<Microsoft.CodeAnalysis.Diagnostic> diagnostics) =>
            diagnostics
                .Select(d => d.Id + " " + System.Text.RegularExpressions.Regex
                    .Match(d.GetMessage(), "must not be called inside (.*?); hooks must").Groups[1].Value)
                .ToArray();
    }
}
