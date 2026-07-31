using System.Collections.Generic;
using System.Linq;

namespace Velvet.Samples.StarterApp
{
    // Plain classes and constructors rather than records throughout this sample: a sample has to compile
    // in whatever project imports it, and this assembly carries no shim of its own.

    /// <summary>One row of the task list. Immutable — an edit produces a new instance.</summary>
    public sealed class TaskItem
    {
        public TaskItem(string id, string title, bool done)
        {
            Id = id;
            Title = title;
            Done = done;
        }

        public string Id { get; }

        public string Title { get; }

        public bool Done { get; }

        public TaskItem WithDone(bool done) => new TaskItem(Id, Title, done);
    }

    /// <summary>
    /// The store's whole state. The id counter lives here rather than in a field on the store so
    /// <c>Reset</c> rewinds it with everything else.
    /// </summary>
    public sealed class TaskBoardState
    {
        public TaskBoardState(IReadOnlyList<TaskItem> items, int nextId)
        {
            Items = items;
            NextId = nextId;
        }

        public IReadOnlyList<TaskItem> Items { get; }

        public int NextId { get; }
    }

    /// <summary>
    /// Zustand-style store: the state object is replaced, never mutated, which is what lets a selector
    /// compare the slice it returned last render against the one it returns now.
    /// </summary>
    public sealed class TaskBoardStore : Store<TaskBoardState>
    {
        /// <summary>
        /// One instance for the whole app — the module-level store of the Zustand model. A screen that
        /// wants its own copy constructs one instead.
        /// </summary>
        public static readonly TaskBoardStore Instance = new TaskBoardStore();

        private TaskBoardStore() : base(Seed())
        {
        }

        public void Add(string title) => SetState(state => new TaskBoardState(
            state.Items.Append(new TaskItem("task-" + state.NextId, title, false)).ToArray(),
            state.NextId + 1));

        public void Toggle(string id) => SetState(state => new TaskBoardState(
            state.Items.Select(item => item.Id == id ? item.WithDone(!item.Done) : item).ToArray(),
            state.NextId));

        public void Remove(string id) => SetState(state => new TaskBoardState(
            state.Items.Where(item => item.Id != id).ToArray(),
            state.NextId));

        protected override void ResetCore() => SetState(_ => Seed());

        private static TaskBoardState Seed() => new TaskBoardState(
            new[]
            {
                new TaskItem("task-0", "Open StarterApp.unity and press Play", true),
                new TaskItem("task-1", "Add a task with the field above", false),
                new TaskItem("task-2", "Follow the About link", false),
            },
            nextId: 3);
    }
}
