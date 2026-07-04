using System;
using System.Text;

namespace Velvet.SourceGenerators.Shared
{
    internal sealed class SourceBuilder
    {
        private readonly StringBuilder _sb = new();
        private int _indent;

        public SourceBuilder AppendLine(string text = "")
        {
            if (string.IsNullOrEmpty(text))
            {
                _sb.Append('\n');
                return this;
            }

            for (var i = 0; i < _indent; i++)
            {
                _sb.Append("    ");
            }
            _sb.Append(text);
            _sb.Append('\n');
            return this;
        }

        public IDisposable Block()
        {
            AppendLine("{");
            _indent++;
            return new BlockScope(this);
        }

        public override string ToString() => _sb.ToString();

        private sealed class BlockScope : IDisposable
        {
            private readonly SourceBuilder _owner;
            private bool _disposed;

            public BlockScope(SourceBuilder owner)
            {
                _owner = owner;
            }

            public void Dispose()
            {
                if (_disposed)
                {
                    return;
                }
                _disposed = true;
                _owner._indent--;
                _owner.AppendLine("}");
            }
        }
    }
}
