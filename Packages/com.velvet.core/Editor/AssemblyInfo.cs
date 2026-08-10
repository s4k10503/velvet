using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("Velvet.Tests.BuildInclusion.Editor")]
[assembly: InternalsVisibleTo("Velvet.Tests.DevTools.Editor")]

// See Generators~/README.md for the code-shape rules and why they are opt-in.
[assembly: System.Reflection.AssemblyMetadata("Velvet.CodeShape", "enforce")]
