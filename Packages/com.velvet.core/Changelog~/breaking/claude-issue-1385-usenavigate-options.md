### Changed

- `Hooks.UseNavigate` returns a `NavigateFunction` in place of `Func<string, VelvetTask<NavigationResult>>`, and takes
  no `replace` argument: React Router's `navigate(to, options)` has none. Replace a `Hooks.UseNavigate(true)` call with
  `Hooks.UseNavigate()` and pass `new NavigateOptions { Replace = true }` to each call that replaces. A delegate
  field or variable typed `Func<string, VelvetTask<NavigationResult>>` becomes `NavigateFunction`. A call such as
  `navigate("/path")` is unchanged.

- `RouterLocation.Path` no longer contains a `#fragment`. A target such as `/docs#intro` used to match no route; it
  now matches `/docs` and the fragment is `RouterLocation.Hash`.
