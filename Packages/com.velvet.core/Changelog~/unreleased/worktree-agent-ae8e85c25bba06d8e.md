### Added

- VEL102 warns where a hook is called in a method that is neither a `[Component]` method nor a custom hook
  named `Use` followed by an uppercase letter, as React's `rules-of-hooks` lint reports a hook in a function
  that is neither a component nor a custom hook. A helper calling `Hooks.UseState` that a component called on
  some renders only previously drew no diagnostic. Mark the method `[Component]`, or rename it to that
  shape so VEL101 checks each call to it. A method mounted with `V.Component` without `[Component]` is reported too;
  a `#pragma warning disable` naming VEL102 opts a test harness out.
