using System.Diagnostics.CodeAnalysis;

// Each member below builds rows without keys, as its fixture was written. Keying them would change what the
// fixture arranges, so VEL600 is suppressed by member here instead.

[assembly: SuppressMessage("Velvet.Reconciler", "VEL600", Scope = "member", Target = "~M:Velvet.Tests.DndPlayModeTests.ParkedOverlayList")]
[assembly: SuppressMessage("Velvet.Reconciler", "VEL600", Scope = "member", Target = "~M:Velvet.Tests.Performance.BenchmarkHelpers.BuildExpansionNodes(System.Int32,System.String)")]
[assembly: SuppressMessage("Velvet.Reconciler", "VEL600", Scope = "member", Target = "~M:Velvet.Tests.Performance.BenchmarkHelpers.BuildLabelAndButtonNodes(System.Int32)")]
[assembly: SuppressMessage("Velvet.Reconciler", "VEL600", Scope = "member", Target = "~M:Velvet.Tests.Performance.BenchmarkHelpers.BuildLabelNodes(System.Int32,System.String)")]
[assembly: SuppressMessage("Velvet.Reconciler", "VEL600", Scope = "member", Target = "~M:Velvet.Tests.Performance.InitialMountAllocBreakdown.BuildLabelVNodes")]
[assembly: SuppressMessage("Velvet.Reconciler", "VEL600", Scope = "member", Target = "~M:Velvet.Tests.Performance.PaintPassReconcileBenchmarks.BuildRows(System.String)")]
[assembly: SuppressMessage("Velvet.Reconciler", "VEL600", Scope = "member", Target = "~M:Velvet.Tests.Performance.StyleManipulatorReconcileBenchmarks.BuildRows(System.String)")]
[assembly: SuppressMessage("Velvet.Reconciler", "VEL600", Scope = "member", Target = "~M:Velvet.Tests.Performance.StyleManipulatorReconcileBenchmarks.BuildStrippedRows")]
[assembly: SuppressMessage("Velvet.Reconciler", "VEL600", Scope = "member", Target = "~M:Velvet.Tests.Performance.TextSizeEffectReconcileBenchmarks.BuildLabels(System.String,System.String)")]
[assembly: SuppressMessage("Velvet.Reconciler", "VEL600", Scope = "member", Target = "~M:Velvet.Tests.Performance.VisibleToggleProjectionBenchmarks.BuildRows(System.String,System.Boolean)")]
