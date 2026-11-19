using Velvet.SourceGenerators.Shared;
using Xunit;

namespace Velvet.SourceGenerators.Tests
{
    public sealed class SourceBuilderTests
    {
        [Fact]
        public void AppendLine_AppendsTextWithNewline()
        {
            var sb = new SourceBuilder();
            sb.AppendLine("hello");
            Assert.Equal("hello\n", sb.ToString());
        }

        [Fact]
        public void AppendLine_EmptyString_AppendsBlankLineWithoutIndent()
        {
            var sb = new SourceBuilder();
            sb.AppendLine("a");
            sb.AppendLine();
            sb.AppendLine("b");
            Assert.Equal("a\n\nb\n", sb.ToString());
        }

        [Fact]
        public void Block_IndentsContentByFourSpaces()
        {
            var sb = new SourceBuilder();
            sb.AppendLine("class C");
            using (sb.Block())
            {
                sb.AppendLine("int X;");
            }
            Assert.Equal("class C\n{\n    int X;\n}\n", sb.ToString());
        }

        [Fact]
        public void Block_NestedBlocks_AccumulateIndent()
        {
            var sb = new SourceBuilder();
            sb.AppendLine("namespace N");
            using (sb.Block())
            {
                sb.AppendLine("class C");
                using (sb.Block())
                {
                    sb.AppendLine("int X;");
                }
            }
            Assert.Equal(
                "namespace N\n{\n    class C\n    {\n        int X;\n    }\n}\n",
                sb.ToString());
        }

        [Fact]
        public void Block_MultipleSiblingBlocks_ReturnToOuterIndent()
        {
            var sb = new SourceBuilder();
            sb.AppendLine("class C");
            using (sb.Block())
            {
                sb.AppendLine("void M1()");
                using (sb.Block())
                {
                }
                sb.AppendLine("void M2()");
                using (sb.Block())
                {
                }
            }
            Assert.Equal(
                "class C\n{\n    void M1()\n    {\n    }\n    void M2()\n    {\n    }\n}\n",
                sb.ToString());
        }
    }
}
