### Changed

- `Router.SubmitAsync` and the function `Hooks.UseSubmit` returns no longer throw `ArgumentException` for a
  `get` submission whose form data is not an `ISearchParams`; the navigation commits React Router's "Unable to
  encode submission body" as the leaf route's error and runs no action, as `routing.md` describes.
