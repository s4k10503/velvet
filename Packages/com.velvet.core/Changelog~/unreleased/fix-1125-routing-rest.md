### Added

- Route actions, React Router's route `action`, `useSubmit` and `useActionData`: `RouteDefinition.Action`
  (and `V.Route(action:)`), `Hooks.UseSubmit`, `Router.SubmitAsync`, `SubmitOptions` and
  `Hooks.UseActionData`. A submission other than `get` calls the action of the route it targets and,
  once the action returns, runs every matched loader and commits the action's result for `UseActionData`;
  an action that throws renders through the nearest `errorElement`. A `get` submission navigates with its `ISearchParams` as the
  query string. `Hooks.UseNavigation` reports `NavigationLifecycle.Submitting` while an action runs, and
  `NavigationState.FormMethod`, `FormAction` and `FormData` describe the submission in flight.
