# Navigation blocking

`Hooks.UseBlocker` is React Router's `useBlocker`: a predicate the router consults before it leaves the
current location, and a `RouteBlockerState` a component renders a confirm dialog from. The canonical use
is an unsaved-changes prompt — the predicate returns true while the form is dirty, the dialog appears on
the blocked attempt, and the buttons on it call `Proceed` or `Reset`.

`UseBlocker` takes a `bool`, as `useBlocker(boolean)` does, or a predicate over a `BlockerFunctionArgs`:
`CurrentLocation`, `NextLocation` and `HistoryAction`, the three fields React Router's blocker function
receives. The component re-renders whenever its Blocker's state changes. Registering is the hook's job;
`Router.RouteBlockerManager` is the same registry underneath, and a `UseBlocker` registration takes the
new predicate when its dependency list changes — the list is read the way every other one is, which
[react-migration.md](react-migration.md) owns — keeping its place in the registration order.

## The three states

| `RouteBlockerStatus` | React Router | What it means |
|---|---|---|
| `Idle` | `unblocked` | Holding nothing. `Location`, `Proceed` and `Reset` are null. |
| `Blocked` | `blocked` | Holding a navigation the predicate vetoed. `Location` is where it was heading. |
| `Proceeding` | `proceeding` | `Proceed` released that navigation and it is on its way. `Location` is still its destination; `Proceed` and `Reset` are null. |

A vetoed navigation returns `NavigationResult.Blocked` from `Router.NavigateAsync` / `GoBack` /
`GoForward` and leaves the router where it was, history index included, and the navigation already in
flight, if any, goes on. `Location` is what a dialog names the destination from.

## Resolving a block

`Proceed` sends the blocked navigation through again as the caller made it — its path, its mode, so a
Back or Forward goes again as the same history step, and its submission, if it was one. That navigation does not consult the Blocker that
released it — that is what `Proceeding` is for — so a predicate that still answers "block" does not have
to disarm itself. It hands nothing back: `Router.Navigation` reports the navigation while it is in
flight, and `Router.OnLocationChanged` announces it if it commits.

`Reset` abandons the navigation instead. The router is already where it was by the time any UI can call
it, so this returns the Blocker to `Idle` and nothing else.

Both are handed out with the block, as React Router's `proceed` and `reset` are. A kept `Proceed` throws
`InvalidOperationException` naming the state transition while the Blocker is not `Blocked`, and one kept
while the Blocker holds a newer navigation releases the navigation it was handed out for. A kept `Reset`
returns the Blocker to `Idle` whatever its state.

## When a Blocker starts over

Every Blocker returns to `Idle` when a navigation commits. A navigation that ends without committing
while another is under way leaves a `Proceeding` Blocker as it is, as React Router's stays proceeding
until a navigation completes: one that takes over from the navigation it released is not put to it
either. One that ends without committing — a Guard redirect that goes nowhere, a failure, a
cancellation — with none left under way returns it to `Idle`. One a Blocker registered after it blocks
leaves it `Proceeding`, as a blocked navigation completes nothing in React Router either.

A navigation a `Blocked` Blocker lets through does not release it: the block stands while that
navigation loads, and ends when a navigation commits. A navigation it vetoes replaces what it holds.

Disposing a Blocker's registration removes it, and returns its state to `Idle` whatever it was holding,
as React Router deletes a blocker's state with its key: a `Proceed` kept from it throws.

## More than one Blocker

A router consults only the Blocker registered last, as React Router's does, and logs a warning each
time it does so with more than one registered. A `UseBlocker` whose dependencies
change keeps its place in the order rather than moving to the end.

## Where the router puts the Blocker in the sequence

The Blocker is consulted before the path is matched, as React Router consults its blocker before it
starts the navigation: a path no route matches is put to it, and nothing of the attempt is published
in `Router.Navigation` while it decides.

Route Guards run after it, so a Guard is not asked about an attempt the Blocker stopped. A Guard's
redirect is not put to the Blocker, as React Router does not put a redirect a loader returns to its
blocker: an auth redirect off a dirty form is vetoed, if at all, as the departure the user asked for.
