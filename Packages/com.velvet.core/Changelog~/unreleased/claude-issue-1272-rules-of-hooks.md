### Added

- VEL102 warns where a hook is called in a method that is neither a component nor a custom hook named `Use`
  followed by an uppercase letter or a digit, and where a hook sits in a field or property initializer, as React's
  `rules-of-hooks` lint reports a hook in a function that is neither. A helper calling `Hooks.UseState` that a
  component called on some renders only previously drew no diagnostic. A component is a `[Component]` method, or
  a method or lambda handed to `V.Component` or `V.Memo` as its render body. Mark the method `[Component]`, or
  rename it to the custom-hook shape so VEL101 checks each call to it. A `#pragma warning disable` naming VEL102
  opts a test harness out.
- VEL103 warns where a component whose declaration calls a hook is called directly as a plain method, whose hooks
  then run as part of the caller. Mount it with `V.Component` instead. It reads a call that names the component by
  a bare name from within its declaring type or qualified by that type's simple name; a call through
  `using static`, an alias, a base class or an instance is not reported, and nor is a call that is the whole
  render body of a lambda handed to `V.Component`, as in V.Component(() => Sheet()).

### Changed

- VEL101 reads a try and a catch as React's lint does. A hook in a try block is reported only where the try has a
  catch and something that may throw runs before the hook in it (a call, an object creation, a member or element
  access, an await or a throw); a hook in a catch is reported unless its try cannot complete normally. A hook in
  a foreach's collection or a for's initializer, which run once, is not reported, and a hook on the right of
  `??=` or after `?.` is.
- VEL101 is no longer reported for a hook VEL102 reports, matching React's lint, which asks the conditional
  checks only of a component or a custom hook.
- VEL101 no longer reports a hook in a lambda handed to `V.Component` or `V.Memo` as its render body, and still
  reports one inside a condition there.
- VEL101 reports a hook in any other lambda only where the lambda sits inside a component or a custom hook, as
  React's lint reports a hook in a callback. A hook in a lambda inside a plain helper or a field initializer is no
  longer reported.
- A call on a member counts as a hook call only where the receiver is a single name starting with an uppercase
  letter, as in React's lint, or a qualified name that binds to a namespace or a type, as in
  Velvet.Hooks.UseState(...). A method named like a hook called on a local, a parameter, a field, a property or
  `this`, such as svc.UseDefaults() or cfg.Logger.UseDefaults(), is an ordinary call. VEL101 previously reported
  such a call inside a condition.
- A hook in an if's condition, a conditional expression's condition, or the left operand of `&&`, `||` or `??`
  is no longer reported by VEL101: it runs before the branch is chosen, and React's lint reports none of them.
- A name of `Use` followed by a digit, such as Use2D, is a hook's, as React's lint reads `use` followed by a digit.
- A lambda held by a variable, or assigned to a name, of a hook's name is a custom hook: VEL101 no longer reports
  a hook in it as a nested lambda.

### Fixed

- VEL101 reports a hook in a `finally` block that sits inside a lambda or a condition. It previously stopped
  looking at the `finally` and reported nothing.
- VEL101 reports a hook after a conditional early return where a bare block, a lock, a using, a try or a finally
  stands between them. It previously read only the block holding the hook.
- A hook called by a bare name with type arguments, such as UseTab<int>(), is checked as one. VEL101 previously
  skipped it.
- `Hooks.UseLoaderData` reads the loader-data context before it returns early for a component outside a matched
  route, so it calls the same hooks on every render.
