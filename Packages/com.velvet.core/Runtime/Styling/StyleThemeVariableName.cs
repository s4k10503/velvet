#nullable enable
using System.Collections.Generic;

namespace Velvet
{
    // The custom property a theme namespace gives a name (`--breakpoint-` and `sm` make `--breakpoint-sm`), built once
    // per pair: the class readers look one up for a class on each patch while a mount's scope is open, and a
    // concatenation there allocates every time (ClassReadAllocationTests).
    internal static class StyleThemeVariableName
    {
        // Past this many pairs a name is built on each call and not kept, so a stream of distinct classes cannot grow
        // the table without bound.
        private const int MaxCached = 4096;

        private static readonly Dictionary<(string Namespace, string Name), string> s_names = new();

        internal static string Of(string ns, string name)
        {
            if (!s_names.TryGetValue((ns, name), out var property))
            {
                property = ns + name;
                if (s_names.Count < MaxCached)
                {
                    s_names[(ns, name)] = property;
                }
            }
            return property;
        }
    }
}
