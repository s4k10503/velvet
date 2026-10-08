### Added

- `Hooks.UseMutation` takes `MutationOptions<TVariables, TData, TContext>`, TanStack Query's `onMutate` /
  `onSuccess` / `onError` / `onSettled` quartet: `OnMutate` runs before `MutationFn`, once the handle shows
  the call pending, and what it returns is the context that call's `OnSuccess`, `OnError` and `OnSettled`
  receive, so an optimistic write made there can be rolled back from `OnError`. A throwing `OnMutate` fails
  the call with its exception before `MutationFn` runs, and `OnError` / `OnSettled` receive a default context.
  Overlapping calls each carry their own context.

- `MutationOptions`, `MutationOptions<TVariables>` and `MutationOptions<TVariables, TData>` take an
  `OnSettled`, run after `OnSuccess` or `OnError` on either path and before the outcome is committed. It is
  appended as the last positional parameter, so each record's constructor and `Deconstruct` take one more
  argument: a call by position or by name compiles as before, and a deconstruction of one of these records
  into its three members does not. A throwing `OnSettled` on the success path fails the call, which then runs
  `OnError` and `OnSettled` with that exception, as v5 does; on the failure path it is logged and the outcome
  stays the mutation's own, as it does after a throwing `OnError`, which does not cost the call its
  `OnSettled`.
