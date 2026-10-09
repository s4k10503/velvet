### Added

- VEL102 warns where a hook is called in a method that is neither a component nor a custom hook named `Use`
  followed by an uppercase letter, and where a hook sits in a field or property initializer, as React's
  `rules-of-hooks` lint reports a hook in a function that is neither. A helper calling `Hooks.UseState` that a
  component called on some renders only previously drew no diagnostic. A component is a `[Component]` method, or
  a method or lambda handed to `V.Component` or `V.Memo` as its render body. Mark the method `[Component]`, or
  rename it to the custom-hook shape so VEL101 checks each call to it. A `#pragma warning disable` naming VEL102
  opts a test harness out.
- VEL103 warns where a component whose declaration calls a hook is called directly as a plain method, whose hooks
  then run as part of the caller. Mount it with `V.Component` instead. A call that is the whole render body of a
  lambda handed to `V.Component`, as in V.Component(() => Sheet()), is not reported.

### Changed

- VEL101 is no longer reported for a hook VEL102 reports, matching React's lint, which asks the conditional
  checks only of a component or a custom hook.
- VEL101 no longer reports a hook in a lambda handed to `V.Component` or `V.Memo` as its render body, and still
  reports one inside a condition there.
- VEL101 reports a hook in any other lambda only where the lambda sits inside a component or a custom hook, as
  React's lint reports a hook in a callback. A hook in a lambda inside a plain helper or a field initializer is no
  longer reported.
- A call on a member counts as a hook call only where the receiver's name starts with an uppercase letter, as in
  React's lint: Hooks.UseState(...) is one, and a method named like a hook called on a local, a parameter, a field
  or `this`, such as svc.UseDefaults(), is an ordinary call. VEL101 previously reported such a call inside a
  condition.

### Fixed

- VEL101 reports a hook in a `finally` block that sits inside a lambda or a condition. It previously stopped
  looking at the `finally` and reported nothing.
- A hook called by a bare name with type arguments, such as UseTab<int>(), is checked as one. VEL101 previously
  skipped it.
- `Hooks.UseLoaderData` reads the loader-data context before it returns early for a component outside a matched
  route, so it calls the same hooks on every render.
