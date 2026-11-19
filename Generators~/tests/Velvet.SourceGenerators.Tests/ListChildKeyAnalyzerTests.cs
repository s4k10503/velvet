using System.Linq;
using Velvet.SourceGenerators.Keys;
using Xunit;

namespace Velvet.SourceGenerators.Tests
{
    /// <summary>
    /// Tests for <see cref="ListChildKeyAnalyzer"/>. Each sample builds a list in the body of <c>Rows</c>, beside the
    /// helpers the case declares on <c>Page</c>, and the case reads back the VEL600 messages it draws.
    /// </summary>
    public sealed class ListChildKeyAnalyzerTests
    {
        private const string UnkeyedComponentMessage =
            "'V.Component' builds a child of a list without a key; each child in a list should have a unique key";

        private static string Vel600Messages(string rows, string helpers = "")
        {
            var source = @"
using System.Collections.Generic;
using System.Linq;
using Velvet;

namespace MyApp
{
    public static class Ui
    {
        public static VNode[] Select(int[] items, System.Func<int, VNode> render) => null;

        public static ComponentNode MakeRow() => V.Component(Page.Row);

        public static ComponentNode MakeRowFor(int item) => V.Component(Page.Row);

        public static VNode Pad(VNode node) => node;

        public static VNode Nest(int depth) => depth == 0 ? V.Component(Page.Row) : Nest(depth - 1);
    }

    public static class Page
    {
        internal static VNode Row() => null;

" + helpers + @"

        public static object Rows(int[] items)
        {
            " + rows + @"
        }
    }
}";
            var diagnostics = GeneratorTestHelper.RunAnalyzerOnCompilingSource(source, new ListChildKeyAnalyzer());
            return string.Join("|", diagnostics.Where(d => d.Id == "VEL600").Select(d => d.GetMessage()));
        }

        [Fact]
        public void Given_ASelectorReturningAComponentWithoutAKey_When_Analyzed_Then_Vel600NamesTheFactory()
        {
            // Arrange
            const string rows = "return items.Select(item => V.Component(Row)).ToArray();";

            // Act
            var messages = Vel600Messages(rows);

            // Assert
            Assert.Equal(UnkeyedComponentMessage, messages);
        }

        [Fact]
        public void Given_ASelectorReturningAKeyedComponent_When_Analyzed_Then_NothingIsReported()
        {
            // Arrange
            const string rows = "return items.Select(item => V.Component(Row, key: item.ToString())).ToArray();";

            // Act
            var messages = Vel600Messages(rows);

            // Assert
            Assert.Equal(string.Empty, messages);
        }

        [Fact]
        public void Given_ASelectorPassingANullKey_When_Analyzed_Then_Vel600NamesTheFactory()
        {
            // Arrange
            const string rows = "return items.Select(item => V.Component(Row, key: null)).ToArray();";

            // Act
            var messages = Vel600Messages(rows);

            // Assert
            Assert.Equal(UnkeyedComponentMessage, messages);
        }

        [Fact]
        public void Given_ASelectorCastingAnUnkeyedComponent_When_Analyzed_Then_Vel600NamesTheFactory()
        {
            // Arrange
            const string rows = "return items.Select(item => (VNode)V.Component(Row)).ToArray();";

            // Act
            var messages = Vel600Messages(rows);

            // Assert
            Assert.Equal(UnkeyedComponentMessage, messages);
        }

        [Fact]
        public void Given_AConditionalBranchBuildingAnUnkeyedComponent_When_Analyzed_Then_Vel600NamesTheFactory()
        {
            // Arrange
            const string rows = "return items.Select(item => item > 0 ? V.Component(Row) : null).ToArray();";

            // Act
            var messages = Vel600Messages(rows);

            // Assert
            Assert.Equal(UnkeyedComponentMessage, messages);
        }

        [Fact]
        public void Given_ABlockSelectorReturningAnUnkeyedComponentFromAnIf_When_Analyzed_Then_Vel600NamesTheFactory()
        {
            // Arrange
            const string rows = "return items.Select(item => { if (item > 0) return V.Component(Row); return null; }).ToArray();";

            // Act
            var messages = Vel600Messages(rows);

            // Assert
            Assert.Equal(UnkeyedComponentMessage, messages);
        }

        [Fact]
        public void Given_ASelectorReturningText_When_Analyzed_Then_NothingIsReported()
        {
            // Arrange — a text node is a string child in React, which takes no key.
            const string rows = "return items.Select(item => (VNode)V.Text(item.ToString())).ToArray();";

            // Act
            var messages = Vel600Messages(rows);

            // Assert
            Assert.Equal(string.Empty, messages);
        }

        [Fact]
        public void Given_ASelectorReturningWhenOverAnUnkeyedComponent_When_Analyzed_Then_Vel600NamesTheComponent()
        {
            // Arrange — V.When hands back the node its factory builds.
            const string rows = "return items.Select(item => V.When(item > 0, () => V.Component(Row))).ToArray();";

            // Act
            var messages = Vel600Messages(rows);

            // Assert
            Assert.Equal(UnkeyedComponentMessage, messages);
        }

        [Fact]
        public void Given_ASelectorReturningAFactoryResultThatIsNoNode_When_Analyzed_Then_NothingIsReported()
        {
            // Arrange
            const string rows = "return items.Select(item => V.Routes()).ToArray();";

            // Act
            var messages = Vel600Messages(rows);

            // Assert
            Assert.Equal(string.Empty, messages);
        }

        [Fact]
        public void Given_AKeyedMemoWhoseFactoryBuildsAnUnkeyedComponent_When_Analyzed_Then_NothingIsReported()
        {
            // Arrange — the component is the memo's content, not a child of the list.
            const string rows = "return items.Select(item => (VNode)V.MemoizedWithKey(item.ToString(), () => V.Component(Row))).ToArray();";

            // Act
            var messages = Vel600Messages(rows);

            // Assert
            Assert.Equal(string.Empty, messages);
        }

        [Fact]
        public void Given_ASelectorReturningAHelpersUnkeyedComponent_When_Analyzed_Then_Vel600NamesTheComponent()
        {
            // Arrange
            const string rows = "return items.Select(item => Ui.MakeRow()).ToArray();";

            // Act
            var messages = Vel600Messages(rows);

            // Assert
            Assert.Equal(UnkeyedComponentMessage, messages);
        }

        [Fact]
        public void Given_AMethodGroupSelectorReturningAnUnkeyedComponent_When_Analyzed_Then_Vel600NamesTheComponent()
        {
            // Arrange
            const string rows = "return items.Select(Ui.MakeRowFor).ToArray();";

            // Act
            var messages = Vel600Messages(rows);

            // Assert
            Assert.Equal(UnkeyedComponentMessage, messages);
        }

        [Fact]
        public void Given_AnUnkeyedComponentPassedThroughAHelper_When_Analyzed_Then_Vel600NamesTheComponent()
        {
            // Arrange — the helper returns its parameter, so the argument is the list's child.
            const string rows = "return items.Select(item => Ui.Pad(V.Component(Row))).ToArray();";

            // Act
            var messages = Vel600Messages(rows);

            // Assert
            Assert.Equal(UnkeyedComponentMessage, messages);
        }

        [Fact]
        public void Given_AnUnkeyedComponentHeldInALocal_When_Analyzed_Then_Vel600NamesTheComponent()
        {
            // Arrange
            const string rows = "return items.Select(item => { var row = V.Component(Row); return row; }).ToArray();";

            // Act
            var messages = Vel600Messages(rows);

            // Assert
            Assert.Equal(UnkeyedComponentMessage, messages);
        }

        [Fact]
        public void Given_AnUnkeyedComponentReturnedByADelegateTheSelectorInvokes_When_Analyzed_Then_Vel600NamesTheComponent()
        {
            // Arrange
            const string rows = "return items.Select(item => { System.Func<VNode> later = () => V.Component(Row); return later(); }).ToArray();";

            // Act
            var messages = Vel600Messages(rows);

            // Assert
            Assert.Equal(UnkeyedComponentMessage, messages);
        }

        [Fact]
        public void Given_ARecursiveHelperBuildingAnUnkeyedComponent_When_Analyzed_Then_Vel600NamesTheComponent()
        {
            // Arrange — the trace ends where the helper calls itself.
            const string rows = "return items.Select(item => Ui.Nest(item)).ToArray();";

            // Act
            var messages = Vel600Messages(rows);

            // Assert
            Assert.Equal(UnkeyedComponentMessage, messages);
        }

        [Fact]
        public void Given_ALoopAddingAnUnkeyedComponentToAList_When_Analyzed_Then_Vel600NamesTheComponent()
        {
            // Arrange
            const string rows = "var rows = new List<VNode>(); foreach (var item in items) rows.Add(V.Component(Row)); return rows.ToArray();";

            // Act
            var messages = Vel600Messages(rows);

            // Assert
            Assert.Equal(UnkeyedComponentMessage, messages);
        }

        [Fact]
        public void Given_AListAddedToThatIsNeverMadeAnArray_When_Analyzed_Then_NothingIsReported()
        {
            // Arrange — the list holds nodes for some other use, never as children.
            const string rows = "var rows = new List<VNode>(); foreach (var item in items) rows.Add(V.Component(Row)); return rows.Count;";

            // Act
            var messages = Vel600Messages(rows);

            // Assert
            Assert.Equal(string.Empty, messages);
        }

        [Fact]
        public void Given_ACollectionInitializerHoldingAnUnkeyedComponent_When_Analyzed_Then_NothingIsReported()
        {
            // Arrange — an initializer writes its children out, as an array initializer does.
            const string rows = "return new List<VNode> { V.Component(Row) }.ToArray();";

            // Act
            var messages = Vel600Messages(rows);

            // Assert
            Assert.Equal(string.Empty, messages);
        }

        [Fact]
        public void Given_AnArrayElementAssignedAnUnkeyedComponent_When_Analyzed_Then_Vel600NamesTheComponent()
        {
            // Arrange
            const string rows = "var rows = new VNode[items.Length]; for (var i = 0; i < rows.Length; i++) rows[i] = V.Component(Row); return rows;";
            // Act
            var messages = Vel600Messages(rows);

            // Assert
            Assert.Equal(UnkeyedComponentMessage, messages);
        }

        [Fact]
        public void Given_AnArrayFilledWithUnkeyedComponentsAndHandedToNoFactory_When_Analyzed_Then_NothingIsReported()
        {
            // Arrange — the array holds nodes for some other use, never as children.
            const string rows = "var rows = new VNode[items.Length]; for (var i = 0; i < rows.Length; i++) rows[i] = V.Component(Row); return rows.Length;";

            // Act
            var messages = Vel600Messages(rows);

            // Assert
            Assert.Equal(string.Empty, messages);
        }

        [Fact]
        public void Given_AnArrayFilledWithUnkeyedComponentsAndHandedToAFactory_When_Analyzed_Then_Vel600NamesTheComponent()
        {
            // Arrange
            const string rows = "var rows = new VNode[items.Length]; for (var i = 0; i < rows.Length; i++) rows[i] = V.Component(Row); return V.Fragment(rows);";

            // Act
            var messages = Vel600Messages(rows);

            // Assert
            Assert.Equal(UnkeyedComponentMessage, messages);
        }

        [Fact]
        public void Given_AnIteratorYieldingAnUnkeyedComponent_When_Analyzed_Then_Vel600NamesTheComponent()
        {
            // Arrange
            const string rows = "return Each(items).ToArray();";
            const string helpers = "static IEnumerable<VNode> Each(int[] items) { foreach (var item in items) yield return V.Component(Row); }";

            // Act
            var messages = Vel600Messages(rows, helpers);

            // Assert
            Assert.Equal(UnkeyedComponentMessage, messages);
        }

        [Fact]
        public void Given_AHelperAddingAPassedNodeToAListItsCallerMakesAnArray_When_Analyzed_Then_Vel600NamesTheComponent()
        {
            // Arrange — the node reaches the helper's parameter from the call that passes the list.
            const string rows = "var rows = new List<VNode>(); foreach (var item in items) Append(rows, V.Component(Row)); return rows.ToArray();";
            const string helpers = "static void Append(List<VNode> list, VNode node) => list.Add(node);";

            // Act
            var messages = Vel600Messages(rows, helpers);

            // Assert
            Assert.Equal(UnkeyedComponentMessage, messages);
        }

        [Fact]
        public void Given_AHelperAddingToAListItsCallerNeverMakesAnArray_When_Analyzed_Then_NothingIsReported()
        {
            // Arrange
            const string rows = "var rows = new List<VNode>(); foreach (var item in items) Append(rows, V.Component(Row)); return rows.Count;";
            const string helpers = "static void Append(List<VNode> list, VNode node) => list.Add(node);";

            // Act
            var messages = Vel600Messages(rows, helpers);

            // Assert
            Assert.Equal(string.Empty, messages);
        }

        [Fact]
        public void Given_AnUnkeyedComponentAtTheDeepestPathTheTraceFollows_When_Analyzed_Then_Vel600NamesTheComponent()
        {
            // Arrange — the selector and fifteen helpers hold the guide's sixteen.
            const string rows = "return items.Select(item => H1()).ToArray();";
            const string helpers = "static VNode H1() => H2(); static VNode H2() => H3(); static VNode H3() => H4(); static VNode H4() => H5(); static VNode H5() => H6(); static VNode H6() => H7(); static VNode H7() => H8(); static VNode H8() => H9(); static VNode H9() => H10(); static VNode H10() => H11(); static VNode H11() => H12(); static VNode H12() => H13(); static VNode H13() => H14(); static VNode H14() => H15(); static VNode H15() => V.Component(Row);";

            // Act
            var messages = Vel600Messages(rows, helpers);

            // Assert
            Assert.Equal(UnkeyedComponentMessage, messages);
        }

        [Fact]
        public void Given_AnUnkeyedComponentOnePastTheDeepestPathTheTraceFollows_When_Analyzed_Then_NothingIsReported()
        {
            // Arrange — the selector and sixteen helpers are one past the guide's sixteen.
            const string rows = "return items.Select(item => H1()).ToArray();";
            const string helpers = "static VNode H1() => H2(); static VNode H2() => H3(); static VNode H3() => H4(); static VNode H4() => H5(); static VNode H5() => H6(); static VNode H6() => H7(); static VNode H7() => H8(); static VNode H8() => H9(); static VNode H9() => H10(); static VNode H10() => H11(); static VNode H11() => H12(); static VNode H12() => H13(); static VNode H13() => H14(); static VNode H14() => H15(); static VNode H15() => H16(); static VNode H16() => V.Component(Row);";

            // Act
            var messages = Vel600Messages(rows, helpers);

            // Assert
            Assert.Equal(string.Empty, messages);
        }

        [Fact]
        public void Given_AListFieldAddedToAndMadeAnArrayElsewhere_When_Analyzed_Then_Vel600NamesTheComponent()
        {
            // Arrange
            const string rows = "s_rows.Add(V.Component(Row)); return null;";
            const string helpers = "static readonly List<VNode> s_rows = new List<VNode>(); static VNode[] Snapshot() => s_rows.ToArray();";

            // Act
            var messages = Vel600Messages(rows, helpers);

            // Assert
            Assert.Equal(UnkeyedComponentMessage, messages);
        }

        [Fact]
        public void Given_AListFieldAddedToAndNeverMadeAnArray_When_Analyzed_Then_NothingIsReported()
        {
            // Arrange
            const string rows = "s_rows.Add(V.Component(Row)); return s_rows.Count;";
            const string helpers = "static readonly List<VNode> s_rows = new List<VNode>();";

            // Act
            var messages = Vel600Messages(rows, helpers);

            // Assert
            Assert.Equal(string.Empty, messages);
        }

        [Fact]
        public void Given_AnArrayFieldFilledAndReturnedThroughAProperty_When_Analyzed_Then_Vel600NamesTheComponent()
        {
            // Arrange
            const string rows = "s_rows[0] = V.Component(Row); return null;";
            const string helpers = "static readonly VNode[] s_rows = new VNode[1]; static VNode[] Current => s_rows;";

            // Act
            var messages = Vel600Messages(rows, helpers);

            // Assert
            Assert.Equal(UnkeyedComponentMessage, messages);
        }

        [Fact]
        public void Given_AnArrayFieldFilledAndHandedToAFactory_When_Analyzed_Then_Vel600NamesTheComponent()
        {
            // Arrange
            const string rows = "s_rows[0] = V.Component(Row); return V.Fragment(s_rows);";
            const string helpers = "static readonly VNode[] s_rows = new VNode[1];";

            // Act
            var messages = Vel600Messages(rows, helpers);

            // Assert
            Assert.Equal(UnkeyedComponentMessage, messages);
        }

        [Fact]
        public void Given_AnArrayFieldFilledAndNeverHandedOn_When_Analyzed_Then_NothingIsReported()
        {
            // Arrange
            const string rows = "s_rows[0] = V.Component(Row); return s_rows.Length;";
            const string helpers = "static readonly VNode[] s_rows = new VNode[1];";

            // Act
            var messages = Vel600Messages(rows, helpers);

            // Assert
            Assert.Equal(string.Empty, messages);
        }

        [Fact]
        public void Given_AListGivenAnUnkeyedComponentByInsert_When_Analyzed_Then_Vel600NamesTheComponent()
        {
            // Arrange
            const string rows = "var rows = new List<VNode>(); rows.Insert(0, V.Component(Row)); return rows.ToArray();";
            // Act
            var messages = Vel600Messages(rows);

            // Assert
            Assert.Equal(UnkeyedComponentMessage, messages);
        }

        [Fact]
        public void Given_AListGivenAnUnkeyedComponentByAddRange_When_Analyzed_Then_Vel600NamesTheComponent()
        {
            // Arrange
            const string rows = "var rows = new List<VNode>(); rows.AddRange(new[] { V.Component(Row) }); return rows.ToArray();";
            // Act
            var messages = Vel600Messages(rows);

            // Assert
            Assert.Equal(UnkeyedComponentMessage, messages);
        }

        [Fact]
        public void Given_AListGivenAnUnkeyedComponentByInsertRange_When_Analyzed_Then_Vel600NamesTheComponent()
        {
            // Arrange
            const string rows = "var rows = new List<VNode>(); rows.InsertRange(0, new[] { V.Component(Row) }); return rows.ToArray();";
            // Act
            var messages = Vel600Messages(rows);

            // Assert
            Assert.Equal(UnkeyedComponentMessage, messages);
        }

        [Fact]
        public void Given_ASelectThatIsNotLinq_When_Analyzed_Then_NothingIsReported()
        {
            // Arrange
            const string rows = "return Ui.Select(items, item => V.Component(Row));";

            // Act
            var messages = Vel600Messages(rows);

            // Assert
            Assert.Equal(string.Empty, messages);
        }
    }
}
