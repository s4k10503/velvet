using System.Runtime.CompilerServices;

// ReconcilerScope / ReconcilerTestFixture expose the internal runtime Reconciler to the test
// assemblies that drive reconciler-level unit tests. Those assemblies already see Velvet's own
// internals (granted from the runtime AssemblyInfo); these grants let them reach the Reconciler
// surfaced through TestUtilities. TestUtilities is a dev-only assembly (stripped from the published
// UPM package), so this widening never reaches consumers.
[assembly: InternalsVisibleTo("Velvet.Tests.Component.Editor")]
[assembly: InternalsVisibleTo("Velvet.Tests.Reconciler.Editor")]
[assembly: InternalsVisibleTo("Velvet.Tests.Styling.Editor")]
[assembly: InternalsVisibleTo("Velvet.Tests.Hooks.Editor")]
