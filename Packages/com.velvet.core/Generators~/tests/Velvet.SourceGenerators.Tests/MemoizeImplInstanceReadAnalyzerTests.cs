using System.Linq;
using Velvet.SourceGenerators.AutoDeps;
using Xunit;

namespace Velvet.SourceGenerators.Tests
{
    /// <summary>
    /// Tests for <see cref="MemoizeImplInstanceReadAnalyzer"/>. A sample declaring a <c>[MemoizeMethod]</c> partial
    /// method writes its implementing half by hand, in the shape the generator emits, so the source compiles
    /// without the generator.
    /// </summary>
    public sealed class MemoizeImplInstanceReadAnalyzerTests
    {
        private static string Vel012Messages(string members, string typeDeclaration = "public partial class Page")
        {
            var source = @"
namespace MyApp
{
    public sealed class Props { public int Count { get; set; } }

    " + typeDeclaration + @"
    {
" + members + @"
    }
}";
            var diagnostics = GeneratorTestHelper.RunAnalyzerOnCompilingSource(source, new MemoizeImplInstanceReadAnalyzer());
            return string.Join("|", diagnostics.Where(d => d.Id == "VEL012").Select(d => d.GetMessage()));
        }

        private const string Wrapper = @"
        [global::Velvet.MemoizeMethod]
        public partial global::Velvet.VNode Build(int x);
        public partial global::Velvet.VNode Build(int x) => global::Velvet.V.Memoized(() => Build_Impl(x), new object[] { x, this });";

        [Fact]
        public void Given_AnImplReadingAMutableField_When_Analyzed_Then_Vel012NamesTheField()
        {
            // Arrange
            const string members = Wrapper + @"
        private int _count;
        private global::Velvet.VNode Build_Impl(int x) => x + _count > 0 ? null : null;";

            // Act
            var messages = Vel012Messages(members);

            // Assert
            Assert.Equal("'Build_Impl' reads '_count', which the memo generated for 'Build' does not key on; pass it to 'Build' as a parameter", messages);
        }

        [Fact]
        public void Given_AnImplReadingASettablePropertyTwice_When_Analyzed_Then_Vel012ReportsItOnce()
        {
            // Arrange
            const string members = Wrapper + @"
        public int Count { get; set; }
        private global::Velvet.VNode Build_Impl(int x) => x + Count + this.Count > 0 ? null : null;";

            // Act
            var messages = Vel012Messages(members);

            // Assert
            Assert.Equal("'Build_Impl' reads 'Count', which the memo generated for 'Build' does not key on; pass it to 'Build' as a parameter", messages);
        }

        [Fact]
        public void Given_AnImplReadingAReadonlyField_When_Analyzed_Then_NothingIsReported()
        {
            // Arrange
            const string members = Wrapper + @"
        private readonly int _fixed = 1;
        private global::Velvet.VNode Build_Impl(int x) => x + _fixed > 0 ? null : null;";

            // Act
            var messages = Vel012Messages(members);

            // Assert
            Assert.Equal(string.Empty, messages);
        }

        [Fact]
        public void Given_AnImplReadingAnInitOnlyProperty_When_Analyzed_Then_NothingIsReported()
        {
            // Arrange
            const string members = Wrapper + @"
        public int Seed { get; init; }
        private global::Velvet.VNode Build_Impl(int x) => x + Seed > 0 ? null : null;";

            // Act
            var messages = Vel012Messages(members);

            // Assert
            Assert.Equal(string.Empty, messages);
        }

        [Fact]
        public void Given_AnImplSettingAMemberOfAnObjectItBuilds_When_Analyzed_Then_NothingIsReported()
        {
            // Arrange
            const string members = Wrapper + @"
        private global::Velvet.VNode Build_Impl(int x) => new Props { Count = x }.Count > 0 ? null : null;";

            // Act
            var messages = Vel012Messages(members);

            // Assert
            Assert.Equal(string.Empty, messages);
        }

        [Fact]
        public void Given_AReadonlyStructMemberReadingAField_When_Analyzed_Then_NothingIsReported()
        {
            // Arrange
            const string members = @"
        private int _count;
        [global::Velvet.MemoizeMethod]
        public readonly partial global::Velvet.VNode Build(int x);
        public readonly partial global::Velvet.VNode Build(int x) { var self = this; return global::Velvet.V.Memoized(() => self.Build_Impl(x), new object[] { x, this }); }
        private readonly global::Velvet.VNode Build_Impl(int x) => x + _count > 0 ? null : null;";

            // Act
            var messages = Vel012Messages(members, typeDeclaration: "public partial struct Page");

            // Assert
            Assert.Equal(string.Empty, messages);
        }

        [Fact]
        public void Given_AnImplWhoseMethodIsNotMemoized_When_Analyzed_Then_NothingIsReported()
        {
            // Arrange
            const string members = @"
        private int _count;
        public global::Velvet.VNode Build(int x) => Build_Impl(x);
        private global::Velvet.VNode Build_Impl(int x) => x + _count > 0 ? null : null;";

            // Act
            var messages = Vel012Messages(members);

            // Assert
            Assert.Equal(string.Empty, messages);
        }

        [Fact]
        public void Given_AMethodWithoutTheImplSuffixWhosePrefixIsMemoized_When_Analyzed_Then_NothingIsReported()
        {
            // Arrange
            const string members = Wrapper + @"
        private int _count;
        private global::Velvet.VNode Build_Impl(int x) => null;
        private global::Velvet.VNode BuildOther(int x) => x + _count > 0 ? null : null;";

            // Act
            var messages = Vel012Messages(members);

            // Assert
            Assert.Equal(string.Empty, messages);
        }
    }
}
