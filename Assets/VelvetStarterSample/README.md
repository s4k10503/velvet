# Velvet Starter App

A scene you can open and press Play on: a `UIDocument`, a `PanelSettings` and a mount call, assembled
and running.

## Running it

Open `StarterApp.unity` and press Play. Nothing else is needed: the scene carries its own
`PanelSettings` and theme, and `StarterAppHost` attaches the bundled utility stylesheet itself.

## What each file is for

| File | Role |
|------|------|
| `StarterAppHost.cs` | The `MonoBehaviour` on the `UIDocument`: attaches the stylesheet, builds the `Router`, mounts `V.RouterProvider` over it, and tears all three down again |
| `StarterApp.cs` | The screen — the route table, the layout route with its `Outlet`, and the two child routes |
| `TaskBoardStore.cs` | A `Store<T>` holding the task list, replaced rather than mutated on every edit |
| `StarterApp.unity` | Camera plus one GameObject carrying `UIDocument` and `StarterAppHost` |
| `StarterAppPanelSettings.asset` | The runtime panel: scale mode, reference resolution, theme |
| `StarterAppTheme.tss` | Unity's default runtime theme, shipped here so the panel above needs nothing from your project |

## What it exercises

`Hooks.UseState` for the draft field, `Hooks.UseStore` for the task list, `V.List` for the keyed rows,
`V.RouterProvider` / `V.Route` / `V.Outlet` / `V.NavLink` / `V.Link` for the two routes, `hover:`
variants on the buttons and the task rows, and `V.Motion` inside `V.AnimatePresence` so a row fades and
scales in and out. The package's `Documentation~/routing.md` owns what the provider publishes.

## The stylesheet call

`VelvetStyleUtilities.AttachTo(root)` in `StarterAppHost.OnEnable` is what makes the utility classes
resolve on a runtime panel. Removing it raises no error: the screen still mounts and the links still
navigate, it just renders wrong, which is why a missing call reads as a styling bug. The package's
`Documentation~/setup.md` owns which families stop working without the sheet and which are unaffected,
and the scene-reference alternative to the call.
