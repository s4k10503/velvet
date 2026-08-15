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

`Proceed()` re-issues the request the caller made — its path and its mode, so a Back or Forward goes
again as the same history step — and the whole pipeline runs again from matching. The re-issued
navigation does not consult the Blocker that released it — that is what `Proceeding` is for — so a
predicate that still answers "block" does not have to disarm itself.

Where a Guard redirected the attempt, the two halves come apart: `Attempt` describes where the
navigation was actually heading when the Blocker saw it, and `Proceed()` re-issues the request the user
made, so the Guard is consulted again and its redirect lands where it would have without the block.

`Proceed()` hands nothing back — like `useBlocker`'s `proceed`, it is `void`. The re-issued navigation's
outcome arrives through `Router.OnLocationChanged` and `Router.Status`, not from the call.

`Reset()` abandons the navigation instead. The router is already back where it was by the time any UI
can call it, so this clears the state the dialogs are bound to and nothing else.

Both do nothing unless the Blocker is `Blocked`, so a stale button handler is a no-op rather than an
error. React Router matches that for `reset`, which writes the idle state whatever the current one is,
but not for `proceed()`: outside `blocked` it throws on the state transition. Its `proceed` is also
bound to the attempt it was made for, where `Proceed()` reads that attempt off the Blocker and so
re-issues whichever one it is holding when the button is pressed.

## When a Blocker is armed again

A `Proceeding` Blocker returns to `Idle`, and so becomes able to veto the next navigation, when the
navigation it released:

- commits;
- ends without committing — a Guard redirect that goes nowhere, a newer navigation taking over, a
  failure;
- is abandoned — a Blocker still blocking it calls `Reset()`, which releases the rest of them with it.

Disposing a Blocker's registration stops its predicate from being consulted immediately. If that
Blocker is already holding or releasing an attempt, its state still settles with that attempt, including
when a saved dialog handler calls `Proceed()` after the disposal.

The first two wait on one thing more: no Blocker left `Blocked`. A second Blocker vetoing the re-issue
is what that runs into, and the section below has the rest of it.

Separately, and regardless of any of this, a navigation that reaches the Blocker phase clears every
`Blocked` Blocker before the predicates run: a second navigation lifts a standing block even when
nothing answered the first one's dialog, and the Blocker then holds the second attempt rather than the
first. `RouteBlockerManager.ResetAllBlocked` is that step, and it leaves a `Proceeding` Blocker alone.
A navigation that matches no route returns before reaching it, so a standing block survives one.

Every navigation that commits passes through that step first, so the current path and the history index
cannot move while a Blocker is `Blocked`. A blocked Back or Forward therefore resumes as the same step
from the same place.

## More than one Blocker

React Router supports a single blocker: only the last one registered is consulted. In Velvet a block
does not end the pass — the Blockers after the one that blocked are consulted too, bar any already
`Proceeding` — so two dirty forms both veto one navigation, and both dialogs have to be answered.

The order they are answered in does not matter. Each `Proceed()` re-issues the attempt; the Blockers
that have already consented stay out of the way, and the ones that have not veto it again — until they
all have, at which point no Blocker is left in its way. A `Reset()` from any that is still blocking
abandons the attempt for all of them: the others are released with it, and the ones that had consented
come back into the way. React Router does not settle this: it consults one blocker per navigation.

## Where the router puts Blockers in the sequence

Route Guards run before Blockers, so a route a Guard redirects away from is never put to a Blocker.
That is deliberate: an auth redirect should not raise an unsaved-changes prompt at a user who is not
signed in.

Matching runs before both, where React Router consults its blocker before it matches: a path no route
matches is put to a Blocker there and not here.
