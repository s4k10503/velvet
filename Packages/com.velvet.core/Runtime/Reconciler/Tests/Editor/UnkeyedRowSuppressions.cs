using System.Diagnostics.CodeAnalysis;

// Each member below builds rows without keys, and its fixture renders them more than once, so they are matched
// by index across those renders; a key would change that matching.

[assembly: SuppressMessage("Velvet.Reconciler", "VEL600", Scope = "member", Target = "~M:Velvet.Tests.PositionalStateBleedParityTests.HeadInsertingParent")]
[assembly: SuppressMessage("Velvet.Reconciler", "VEL600", Scope = "member", Target = "~M:Velvet.Tests.MountHostStructuralVariantTests.Rows(System.Int32,System.String)")]
[assembly: SuppressMessage("Velvet.Reconciler", "VEL600", Scope = "member", Target = "~M:Velvet.Tests.PortalContainerOwnRowsTests.Rows(System.String[])")]
[assembly: SuppressMessage("Velvet.Reconciler", "VEL600", Scope = "member", Target = "~M:Velvet.Tests.PortalNeighbourGrowthTests.SharedTargetHostRender")]
[assembly: SuppressMessage("Velvet.Reconciler", "VEL600", Scope = "member", Target = "~M:Velvet.Tests.PortalRegistryRetargetTests.Tree(System.String,System.String[])")]
[assembly: SuppressMessage("Velvet.Reconciler", "VEL600", Scope = "member", Target = "~M:Velvet.Tests.PortalSlotShiftScopeTests.Growing")]
[assembly: SuppressMessage("Velvet.Reconciler", "VEL600", Scope = "member", Target = "~M:Velvet.Tests.UnkeyedElementSlotTests.Grows(System.Int32)")]
[assembly: SuppressMessage("Velvet.Reconciler", "VEL600", Scope = "member", Target = "~M:Velvet.Tests.ZIndexStackingTests.StructuralSweepHost")]
