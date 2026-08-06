using System.Runtime.CompilerServices;

// A helper here that wraps one of Velvet's internal types — ReconcilerScope over the Reconciler, the
// extensions over FiberBatchScheduler — is itself internal, so it reaches only the assemblies listed below.
// That list has to stay the runtime AssemblyInfo's list of test assemblies: an assembly that can already
// name the internal type is exactly the one that would otherwise hand-roll the helper, and a grant missing
// here surfaces as CS1061 on the helper with nothing pointing at the real cause. TestUtilities is a dev-only
// assembly (stripped from the published UPM package), so this widening never reaches consumers.
[assembly: InternalsVisibleTo("Velvet.Tests.Async.Editor")]
[assembly: InternalsVisibleTo("Velvet.Tests.Async.PlayMode")]
[assembly: InternalsVisibleTo("Velvet.Tests.Component.Editor")]
[assembly: InternalsVisibleTo("Velvet.Tests.Component.PlayMode")]
[assembly: InternalsVisibleTo("Velvet.Tests.Hooks.Editor")]
[assembly: InternalsVisibleTo("Velvet.Tests.Hooks.PlayMode")]
[assembly: InternalsVisibleTo("Velvet.Tests.Reconciler.Editor")]
[assembly: InternalsVisibleTo("Velvet.Tests.Reconciler.PlayMode")]
[assembly: InternalsVisibleTo("Velvet.Tests.Routing.Editor")]
[assembly: InternalsVisibleTo("Velvet.Tests.Routing.PlayMode")]
[assembly: InternalsVisibleTo("Velvet.Tests.Store.Editor")]
[assembly: InternalsVisibleTo("Velvet.Tests.Styling.Editor")]
[assembly: InternalsVisibleTo("Velvet.Tests.Styling.PlayMode")]

// See Generators~/README.md for the code-shape rules and why they are opt-in.
[assembly: System.Reflection.AssemblyMetadata("Velvet.CodeShape", "enforce")]
