; Unshipped analyzer release
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md
;
; ID range convention (see Issue 887):
;   VEL001-VEL099  Velvet.Memoize       [Memoize] codegen / [Component(Memoize=true)] validation
;   VEL100-VEL199  Velvet.Hooks         Rules-of-Hooks / exhaustive-deps / hook-only constraints
;   VEL200-VEL299  Velvet.Routing       (reserved)
;   VEL300-VEL399  Velvet.Reactive      (reserved)
;   VEL400-VEL499  Velvet.Style         (reserved)
; New IDs follow the convention so IDE category filtering (e.g.
; `dotnet_diagnostic.category-Velvet.Memoize.severity = none`) doesn't accidentally
; silence diagnostics from unrelated subsystems.

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
VEL001 | Velvet.Memoize | Warning | [Memoize] requires at least one parameter
VEL002 | Velvet.Memoize | Warning | [Memoize] supports only 1-8 parameters
VEL003 | Velvet.Memoize | Warning | [Memoize] does not support generic methods
VEL004 | Velvet.Memoize | Warning | [Memoize] does not support async methods
VEL005 | Velvet.Memoize | Warning | [Memoize] does not support ref/out/in parameters
VEL006 | Velvet.Memoize | Warning | [Memoize] partial method declaration requires an accessibility modifier
VEL007 | Velvet.Memoize | Warning | [Memoize] containing type must be declared partial
VEL008 | Velvet.Memoize | Warning | [Memoize] method must return Velvet.VNode or a derived type
VEL009 | Velvet.Memoize | Warning | [Memoize] partial method declaration must not have a body
VEL100 | Velvet.Hooks | Warning | Hook lambda captures a local that is not in the deps array (exhaustive-deps)
VEL101 | Velvet.Hooks | Warning | Hook call inside conditional control flow (Rules of Hooks)
