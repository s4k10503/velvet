using System;
using System.Collections.Generic;

namespace Velvet.Samples.StarterApp
{
    /// <summary>
    /// The screen the sample scene mounts: a two-route app over a store-backed keyed list, with local
    /// state for the draft field, hover variants on the buttons and rows, and an enter/exit transition
    /// per row.
    /// </summary>
    public static class StarterApp
    {
        public const string TasksPath = "/tasks";

        public const string AboutPath = "/about";

        private const string NavClass =
            "px-3 py-1 rounded-md border-0 bg-slate-800 text-slate-300 hover:bg-slate-700 transition-colors";

        private const string NavActiveClass = "bg-sky-600 text-slate-50";

        private const string ActionClass =
            "px-3 py-1 rounded-md border-0 bg-sky-600 text-slate-50 hover:bg-sky-500 transition-colors";

        private const string RowClass =
            "flex flex-row items-center gap-3 px-3 py-2 rounded-md bg-slate-800 hover:bg-slate-700 transition-colors";

        private static readonly Dictionary<string, string> RowPoses = new()
        {
            ["entering"] = "opacity-0 scale-95",
            ["shown"] = "opacity-100 scale-100",
        };

        /// <summary>
        /// The route table. The layout route owns the chrome and an <c>Outlet</c>; its children fill it.
        /// </summary>
        public static RouteDefinition[] Routes() => V.Routes(
            V.Route(
                path: "/",
                element: V.Component(Chrome),
                children: new[]
                {
                    V.Route(path: "tasks", element: V.Component(TaskScreen)),
                    V.Route(path: "about", element: V.Component(AboutScreen)),
                }));

        /// <summary>
        /// The tree's root. The router publishes a location change as an event while
        /// <c>RouterContext.Location</c> wants a value, and this is the bridge between them: hold the
        /// location in state, republish it on every change. The host mounts this before starting the first
        /// navigation, so the subscription is what puts the opening route on screen as much as every later
        /// one — remove it and nothing under the <c>Outlet</c> ever renders.
        /// </summary>
        [Component]
        public static VNode Root(Router router)
        {
            var (location, setLocation) = Hooks.UseState(router.CurrentLocation);

            Hooks.UseEffect(() =>
            {
                void Handle(RouterLocation next) => setLocation.Invoke(next);

                router.OnLocationChanged += Handle;
                // The first navigation may land between construction and this subscription.
                setLocation.Invoke(router.CurrentLocation);
                return () => router.OnLocationChanged -= Handle;
            }, new object[] { router });

            return V.Provider(RouterContext.Location, location, children: new VNode[] { V.Outlet() });
        }

        [Component]
        public static VNode Chrome()
        {
            return V.Div(
                className: "flex flex-col w-full h-full bg-slate-950 p-6 gap-4",
                name: "starter-app",
                children: new VNode[]
                {
                    V.Div(
                        className: "flex flex-row items-center gap-3",
                        name: "starter-header",
                        children: new VNode[]
                        {
                            V.Label(className: "grow text-2xl font-bold text-slate-100", text: "Velvet Starter"),
                            V.NavLink(to: TasksPath, activeClass: NavActiveClass, text: "Tasks",
                                className: NavClass, name: "nav-tasks"),
                            V.NavLink(to: AboutPath, activeClass: NavActiveClass, text: "About",
                                className: NavClass, name: "nav-about"),
                        }),
                    V.Outlet(),
                });
        }

        [Component]
        public static VNode TaskScreen()
        {
            var items = Hooks.UseStore(TaskBoardStore.Instance, state => state.Items);
            var (draft, setDraft) = Hooks.UseState(string.Empty);

            var add = Hooks.UseCallback<Action>(
                () =>
                {
                    var title = draft.Trim();
                    if (title.Length == 0)
                    {
                        return;
                    }

                    TaskBoardStore.Instance.Add(title);
                    setDraft.Invoke(string.Empty);
                },
                draft);

            return V.Div("flex flex-col gap-3",
                V.Div("flex flex-row items-center gap-2",
                    V.TextField(className: "grow", value: draft, onValueChanged: setDraft, name: "draft-field"),
                    V.Button(className: ActionClass, text: "Add", onClick: add, name: "add-button")),
                V.AnimatePresence(children: V.List(items, item => item.Id, TaskRow)));
        }

        [Component]
        public static VNode AboutScreen()
        {
            var count = Hooks.UseStore(
                TaskBoardStore.Instance, state => state.Items.Count, EqualityComparer<int>.Default);

            return V.Div("flex flex-col gap-3 items-start",
                V.Label(className: "text-lg font-semibold text-slate-100", text: "What this scene wires up"),
                V.Label(className: "text-sm text-slate-300",
                    text: "A UIDocument hosts the panel, the bundled utility stylesheet is attached from code, "
                          + "and V.Mount renders the route table into it."),
                V.Label(className: "text-sm text-slate-400", text: $"The store currently holds {count} tasks."),
                V.Link(to: TasksPath, text: "Back to the task list", className: ActionClass, name: "back-link"));
        }

        // No [Component]: with no hooks to hold, a row stays a helper returning nodes rather than a
        // reconcile boundary of its own.
        private static VNode TaskRow(TaskItem item)
        {
            return V.Motion(
                className: RowClass,
                variants: RowPoses,
                initial: "entering",
                animate: "shown",
                exit: "entering",
                transition: new StyleTransitionConfig { DurationSec = 0.18f },
                children: new VNode[]
                {
                    V.Button(
                        className: item.Done
                            ? "px-2 py-1 rounded border-0 bg-emerald-600 text-slate-50 hover:bg-emerald-500 transition-colors"
                            : "px-2 py-1 rounded border-0 bg-slate-700 text-slate-200 hover:bg-slate-600 transition-colors",
                        text: item.Done ? "Done" : "Todo",
                        onClick: () => TaskBoardStore.Instance.Toggle(item.Id)),
                    V.Label(
                        className: item.Done ? "grow text-slate-500" : "grow text-slate-100",
                        text: item.Title),
                    V.Button(
                        className: "px-2 py-1 rounded border-0 bg-rose-600 text-slate-50 hover:bg-rose-500 transition-colors",
                        text: "Remove",
                        onClick: () => TaskBoardStore.Instance.Remove(item.Id)),
                });
        }
    }
}
