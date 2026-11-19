using System.Diagnostics.CodeAnalysis;

// Each member below builds rows without keys, as its fixture was written. Keying them would change what the
// fixture arranges, so VEL600 is suppressed by member here instead.

[assembly: SuppressMessage("Velvet.Reconciler", "VEL600", Scope = "member", Target = "~M:Velvet.Tests.ChildVariantClassParityTests.Row(System.String,System.Int32)")]
[assembly: SuppressMessage("Velvet.Reconciler", "VEL600", Scope = "member", Target = "~M:Velvet.Tests.DivideClassParityTests.Row(System.String,System.Int32)")]
[assembly: SuppressMessage("Velvet.Reconciler", "VEL600", Scope = "member", Target = "~M:Velvet.Tests.DividerEdgeDirectionTests.Row(System.String,System.Int32)")]
[assembly: SuppressMessage("Velvet.Reconciler", "VEL600", Scope = "member", Target = "~M:Velvet.Tests.DividerEdgePanelTests.MountColoredDashRow(System.String,System.String[])")]
[assembly: SuppressMessage("Velvet.Reconciler", "VEL600", Scope = "member", Target = "~M:Velvet.Tests.GapParityTests.Row(System.String,System.Int32)")]
[assembly: SuppressMessage("Velvet.Reconciler", "VEL600", Scope = "member", Target = "~M:Velvet.Tests.GridParityTests.Grid(System.String,System.Int32)")]
[assembly: SuppressMessage("Velvet.Reconciler", "VEL600", Scope = "member", Target = "~M:Velvet.Tests.GridSizingPanelTests.Given_GridCols3OnA300pxRow_When_LaidOut_Then_EachColumnFillsAThirdMinusTheGaps")]
[assembly: SuppressMessage("Velvet.Reconciler", "VEL600", Scope = "member", Target = "~M:Velvet.Tests.GridSizingPanelTests.Given_GridCols3WithASpaceXOnA300pxRow_When_LaidOut_Then_TheSpacedColumnGivesUpItsMargin")]
[assembly: SuppressMessage("Velvet.Reconciler", "VEL600", Scope = "member", Target = "~M:Velvet.Tests.VariantGateTokenPriorityTests.Mount(Velvet.TestUtilities.ReconcilerScope,System.String,System.Int32)")]
[assembly: SuppressMessage("Velvet.Reconciler", "VEL600", Scope = "member", Target = "~M:Velvet.Tests.VariantGatedLayoutManipulatorPanelTests.MountAndResolveAt(System.Single,System.String,System.Int32)")]
