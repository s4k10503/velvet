# Styling notes: Variants & container queries

Velvet's utility classes are Tailwind-inspired, and so is its **variant** syntax — the
`hover:`, `dark:`, `sm:`, `group-hover:`, … prefixes that apply a utility only in a given
state, theme, breakpoint, or relation. This page is the reference for the full variant set
and for **container queries** (`@container`), the CSS `container-type: inline-size`
equivalent that re-points responsive breakpoints at a specific element's width.

A variant token has the shape `<variant>:<payload>`, where the payload is an ordinary
utility — a USS class (`hover:bg-blue-500`) or an arbitrary value (`active:w-[200px]`). USS
class selectors cannot contain `:`, so these tokens are never written to the element's class
list; the reconciler routes each one to a **manipulator** that toggles the payload on and off
as the matching signal changes.

## The variant set

| Family | Prefixes | Driven by |
|---|---|---|
| **State** | `hover:` · `focus:` · `focus-visible:` · `active:` · `checked:` | The element's own pointer / focus state (and `ChangeEvent<bool>` for `checked:`) |
| **Theme** | `dark:` | `VelvetTheme.IsDark` |
| **Responsive** | `sm:` · `md:` · `lg:` · `xl:` · `2xl:` | The resolved responsive-scope width (the panel root by default — see below) |
| **Relational (group)** | `group-hover:` · `group-focus:` · `group-focus-within:` · `group-active:` | A marked ancestor's (`group`) state |
| **Relational (peer)** | `peer-hover:` · `peer-focus:` · `peer-focus-within:` · `peer-active:` · `peer-checked:` | A marked previous-sibling's (`peer`) state |

```csharp
// State: a hover background and an active scale, layered over the base utilities.
V.Button(className: "bg-primary hover:bg-primary-600 active:scale-95", text: "Save");

// Theme: a dark-mode surface color.
V.Div(className: "bg-white dark:bg-neutral-900", ...);

// Responsive: full-width below md, a fixed column from md up.
V.Div(className: "w-full md:w-[320px]", ...);
```

### Responsive breakpoints

The responsive prefixes activate at Tailwind's default **min-widths** — `sm` 640, `md` 768,
`lg` 1024, `xl` 1280, `2xl` 1536 (reference px). They are evaluated against a single resolved
**width source**: by default the panel root, so an unscoped tree behaves exactly like a
panel-width media query. The `@container` marker below changes which element supplies that
width.

### Relational variants (`group-` / `peer-`)

`group-*` reacts to a **marked ancestor**: add the `group` class to a container, and a
descendant's `group-hover:` payload toggles when that container is hovered. `peer-*` reacts to
a **marked previous sibling**: add `peer` to one element, and a later sibling's `peer-checked:`
payload toggles with that peer's checked state. Tailwind's **named** forms are supported, so
multiple groups / peers can coexist without cross-talk:

```csharp
V.Div(className: "group ...",
    children: new[]
    {
        // Tints only when THIS card (the group) is hovered.
        V.Label(className: "text-muted group-hover:text-foreground", text: "Title"),
    });

// Named group: scope the relation to "sidebar" so a nested group does not trigger it.
V.Div(className: "group/sidebar ...",
    children: new[] { V.Label(className: "group-hover/sidebar:text-on", text: "Item") });
```

> Note — there is no `disabled:` variant. UI Toolkit has no reliable "enabled changed" event
> to drive a manipulator, so disabled-state styling stays on the USS `:disabled` pseudo-class
> (the curated `disabled-*` utilities).

### Stacked variants

Variants **stack** like Tailwind's, and the order does not matter — `dark:hover:bg-red`
applies `bg-red` only when the theme is dark **and** the element is hovered, identical to
`hover:dark:bg-red`. A stacked leaf may itself still be a variant (`dark:hover:focus:…`),
nesting another gate. Stacking composes any of the families above (state / theme / responsive
/ relational), so `md:hover:`, `group-hover:dark:`, and similar combinations are all valid.

```csharp
// Underline on hover, but only in dark mode and only from md up.
V.Label(className: "md:dark:hover:underline", text: "Docs");
```

## Container queries — `@container`

By default every responsive breakpoint (`sm:`/`md:`/…) is measured against the **panel root**
width. A container query re-points that measurement at a specific element, so the same
breakpoints respond to **that element's** width instead — the CSS `container-type:
inline-size` equivalent. This lets a component be responsive to the space it is *given* rather
than to the whole window, so the same component can sit in a narrow sidebar and a wide main
column and lay out correctly in each.

Mark an element as a responsive scope with the `@container` class. Its descendants' responsive
breakpoints then resolve against its width. Resolution walks up from each descendant to the
nearest `@container` ancestor; with none marked it falls back to the panel root, so adding
`@container` is purely additive — unscoped subtrees keep the original panel-width behavior
exactly.

```csharp
// This card is a responsive container. Inside it, md: means "the CARD is >= 768px wide",
// not "the window is >= 768px wide".
V.Div(className: "@container w-full",
    children: new[]
    {
        V.Div(className: "flex flex-col md:flex-row gap-4", children: ...),
    });
```

Reference the marker from code via `VelvetResponsive.ContainerClass` (its value is the literal
`"@container"`) rather than hardcoding the string — tooling such as the preview viewport
switcher applies it this way.

### When breakpoints resolve — attach-time binding

A container query is **structural**, like a real CSS container, so the binding has one caveat
worth knowing:

- A descendant binds its responsive **width source once, at the moment it attaches to the
  panel** — it resolves the nearest `@container` ancestor (or the panel root) then, and watches
  that element's width from then on.
- Adding or removing `@container` on an **already-attached** ancestor at runtime does **not**
  re-point descendants that are already attached. They keep the source they bound at attach
  until they re-attach.

The supported usage follows from this: put `@container` on the scope element **before its
subtree mounts**, or **re-mount the subtree** after toggling the marker. (The preview window's
viewport switcher does exactly the latter — it re-mounts the story after changing the canvas's
scope so the simulated width drives the breakpoints; see
[preview-tooling.md](preview-tooling.md).)

### `@container` vs. the panel-width default

| | Default (no marker) | `@container` scope |
|---|---|---|
| What `sm:`/`md:`/… measure | The panel root's width | The nearest `@container` ancestor's width |
| Analogy | A CSS media query | A CSS container query (`container-type: inline-size`) |
| When it binds | At descendant attach | At descendant attach |
| Effect of toggling at runtime | n/a | Needs a re-mount to re-point already-attached descendants |

See also [styling-flexbox-and-gap.md](styling-flexbox-and-gap.md) for the layout utilities the
examples above compose with.
