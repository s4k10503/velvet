using System.Diagnostics.CodeAnalysis;

// Each member below builds rows without keys, as its fixture was written. Keying them would change what the
// fixture arranges, so VEL600 is suppressed by member here instead.

[assembly: SuppressMessage("Velvet.Reconciler", "VEL600", Scope = "member", Target = "~M:Velvet.Tests.UseDeferredValueTests.SlicedParentRender")]
[assembly: SuppressMessage("Velvet.Reconciler", "VEL600", Scope = "member", Target = "~M:Velvet.Tests.UseOptimisticRevertCommitTests.SlicedRowsRender")]
