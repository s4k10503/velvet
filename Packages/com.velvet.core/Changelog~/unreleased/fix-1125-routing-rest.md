### Added

- Route actions, React Router's route `action`, `useSubmit` and `useActionData`: `RouteDefinition.Action`
  (and `V.Route(action:)`), `Hooks.UseSubmit`, `Router.SubmitAsync`, `SubmitOptions` and
  `Hooks.UseActionData`. A `post`, `put`, `patch` or `delete` submission calls the action of the route it
  targets; its result reaches `UseActionData` as soon as the action returns, the loaders run again, and
  the navigation commits with it. An action that throws renders through the nearest `errorElement`, as
  does React Router's 405 for a method no form takes. A `get` submission navigates with its
  `ISearchParams` as the query string. `Hooks.UseNavigation` reports `NavigationLifecycle.Submitting`
  while a submission's guards and action run, and `NavigationState.FormMethod`, `FormAction` and
  `FormData` describe the submission in flight.
