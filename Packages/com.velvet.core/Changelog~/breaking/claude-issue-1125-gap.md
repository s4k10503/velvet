### Changed

- `Router.SubmitAsync` and the function `Hooks.UseSubmit` returns no longer throw `ArgumentException` for a
  `get` submission whose form data is not an `ISearchParams`: the navigation commits "Unable to encode
  submission body" as the leaf route's error and runs no action. A caller that caught the exception reads
  `Router.CurrentLoaderErrors` instead.
