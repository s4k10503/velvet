; Unshipped analyzer release
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md
;
; ID range convention:
;   VEL001-VEL099  Velvet.Memoize       [MemoizeMethod] codegen / [Component(Memoize=true)] validation
;   VEL100-VEL199  Velvet.Hooks         Rules-of-Hooks / exhaustive-deps / hook-only constraints
;   VEL200-VEL299  Velvet.Routing       (reserved)
;   VEL300-VEL399  Velvet.Reactive      (reserved)
;   VEL400-VEL499  Velvet.Style         (reserved)
;   VEL500-VEL599  Velvet.Shape         Mechanical code-shape limits (nesting depth, branch count)
; New IDs follow the convention so IDE category filtering (e.g.
; `dotnet_analyzer_diagnostic.category-Velvet.Memoize.severity = none`) doesn't accidentally
; silence diagnostics from unrelated subsystems. The bulk key is `dotnet_analyzer_diagnostic`;
; `dotnet_diagnostic` takes only single IDs and silently ignores a `category-` suffix. Either key
; needs `is_global = true` on the first line of a `.globalconfig` or the whole file is ignored.

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
VEL001 | Velvet.Memoize | Warning | [MemoizeMethod] arity 0 cannot prove the _Impl method is pure
VEL002 | Velvet.Memoize | Warning | [MemoizeMethod] supports only 1-8 parameters
VEL003 | Velvet.Memoize | Warning | [MemoizeMethod] does not support generic methods
VEL004 | Velvet.Memoize | Warning | [MemoizeMethod] does not support async methods
VEL005 | Velvet.Memoize | Warning | [MemoizeMethod] does not support ref/out/in parameters
VEL006 | Velvet.Memoize | Warning | [MemoizeMethod] partial method declaration requires an accessibility modifier
VEL007 | Velvet.Memoize | Warning | [MemoizeMethod] containing type must be declared partial
VEL008 | Velvet.Memoize | Warning | [MemoizeMethod] method must return Velvet.VNode or a derived type
VEL009 | Velvet.Memoize | Warning | [MemoizeMethod] partial method declaration must not have a body
VEL100 | Velvet.Hooks | Warning | Hook lambda captures a local that is not in the deps array (exhaustive-deps)
VEL101 | Velvet.Hooks | Warning | Hook call inside conditional control flow (Rules of Hooks)
VEL500 | Velvet.Shape | Error | Member body nests control flow more than 4 levels deep
VEL501 | Velvet.Shape | Warning | Member body makes more than 20 branching decisions
