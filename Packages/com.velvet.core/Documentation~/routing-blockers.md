# Navigation blocking

`Hooks.UseBlocker` is React Router's `useBlocker`: a predicate the router consults before it leaves the
current location, and a `RouteBlockerState` a component renders a confirm dialog from. The canonical use
is an unsaved-changes prompt — the predicate returns true while the form is dirty, the dialog appears on
the blocked attempt, and the buttons on it call `Proceed()` or `Reset()`.

The predicate receives a `NavigationAttempt` (`CurrentPath`, `NextPath`, `NavigationMode`). Registering
is the hook's job; `Router.RouteBlockerManager` is the same registry underneath, and a `UseBlocker`
registration is re-made when its dependency list changes — the list is read the way every other one is,
which [react-migration.md](react-migration.md) owns.

## The three states

| `RouteBlockerStatus` | React Router | What it means |
|---|---|---|
| `Idle` | `unblocked` | Holding nothing. `Attempt` is null. |
| `Blocked` | `blocked` | Holding a navigation the predicate vetoed. `Attempt` describes it. |
| `Proceeding` | `proceeding` | `Proceed()` released that navigation and it is on its way. `Attempt` still describes it. |

A vetoed navigation returns `NavigationResult.Blocked` from `Router.NavigateAsync` / `GoBack` /
`GoForward` and leaves the router where it was, history index included. `Attempt` is what a dialog
names the destination from.

## Resolving a block

`Proceed()` re-issues the navigation the Blocker is holding — the one the caller asked for, so a Back or
Forward goes again as the same history step. The re-issued navigation does not consult the Blocker that
released it — that is what `Proceeding` is for — so a predicate that still answers "block" does not have
to disarm itself.

Where a Guard redirected the attempt, the two halves come apart: `Attempt` describes where the
navigation was actually heading when the Blocker saw it, and `Proceed()` re-issues the step the user
asked for, so the Guard is consulted again and its redirect lands where it would have without the block.

`Proceed()` hands nothing back — like `useBlocker`'s `proceed`, it is `void`. The resumed navigation's
outcome arrives through `Router.OnLocationChanged` and `Router.Status`, not from the call.

`Reset()` abandons the navigation instead. The router is already back where it was by the time any UI
can call it, so this clears the state the dialogs are bound to and nothing else.

Both do nothing unless the Blocker is `Blocked`, so a stale button handler is a no-op rather than an
error.

## When a Blocker is armed again

A `Proceeding` Blocker returns to `Idle`, and so becomes able to veto the next navigation, when the
navigation it released:

- commits;
- ends without committing — a Guard redirect that goes nowhere, a newer navigation taking over, a
  failure;
- is abandoned — a Blocker that blocked it in turn calls `Reset()`, and no other is still holding it.

Separately, and regardless of any of this, starting a new navigation clears every `Blocked` Blocker
before the predicates run: a second navigation lifts a standing block even when nothing answered the
first one's dialog. `RouteBlockerManager.ResetAllBlocked` is that step, and it leaves a `Proceeding`
Blocker alone.

## More than one Blocker

React Router supports a single blocker: it warns when a second is registered and then silently consults
only the last one. Velvet consults them all — blocking does not end the pass — so two dirty forms both
veto one navigation, and both dialogs have to be answered.

The order they are answered in does not matter. Each `Proceed()` re-issues the attempt; the Blockers
that have already consented stay out of the way, and the ones that have not veto it again — until they
all have, at which point it lands. A `Reset()` from any of them abandons the attempt for all of them:
the others are released with it, and the ones that had consented come back into the way. React Router
does not settle this, since it consults one blocker and never has a second to release.

## Where the router puts Blockers in the sequence

Route Guards run before Blockers, so a route a Guard redirects away from is never put to a Blocker.
That is deliberate: an auth redirect should not raise an unsaved-changes prompt at a user who is not
signed in.
