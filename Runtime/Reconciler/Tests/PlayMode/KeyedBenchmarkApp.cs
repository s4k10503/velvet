using System;
using System.Collections.Generic;

namespace Velvet.Tests.Performance
{
    internal enum KeyedActionKind
    {
        Run,
        Add,
        Update,
        Select,
        Swap,
        Remove,
        Clear,
    }

    internal sealed record KeyedAction(KeyedActionKind Kind, int Count = 0, int Id = 0);

    internal sealed class KeyedRow
    {
        public KeyedRow(int id, string label)
        {
            Id = id;
            IdText = id.ToString();
            Label = label;
        }

        public int Id { get; }

        public string IdText { get; }

        public string Label { get; }
    }

    // Selected is 0 for no selection: ids start at 1.
    internal sealed record KeyedState(KeyedRow[] Data, int Selected);

    internal sealed record KeyedRowProps(KeyedRow Item, bool Selected, Action<int> OnSelect, Action<int> OnRemove);

    // The keyed application of the js-framework-benchmark suite: one reducer holding the rows and the
    // selected id, a memoized row per item, and callbacks that dispatch to the reducer. The word lists and
    // the label shape are the suite's; the pseudo-random choice is seeded so that two runs build the same
    // labels.
    internal static class KeyedBenchmarkApp
    {
        private static readonly string[] s_adjectives =
        {
            "pretty", "large", "big", "small", "tall", "short", "long", "handsome", "plain", "quaint",
            "clean", "elegant", "easy", "angry", "crazy", "helpful", "mushy", "odd", "unsightly", "adorable",
            "important", "inexpensive", "cheap", "expensive", "fancy",
        };

        private static readonly string[] s_colours =
        {
            "red", "yellow", "blue", "green", "pink", "brown", "purple", "brown", "white", "black", "orange",
        };

        private static readonly string[] s_nouns =
        {
            "table", "chair", "house", "bbq", "desk", "car", "pony", "cookie", "sandwich", "burger", "pizza",
            "mouse", "keyboard",
        };

        private static readonly KeyedState s_empty = new(Array.Empty<KeyedRow>(), 0);

        private const int k_Seed = 20240601;

        private static Random s_random = new(k_Seed);
        private static int s_nextId = 1;

        // Written by the render, read by the benchmark between operations.
        internal static Action<KeyedAction> Dispatch = null!;
        internal static KeyedState State = s_empty;
        internal static int RowRenders;

        internal static void Reset()
        {
            s_random = new Random(k_Seed);
            s_nextId = 1;
            State = s_empty;
            RowRenders = 0;
        }

        [Component]
        internal static VNode RenderApp()
        {
            var (state, dispatch) = Hooks.UseReducer<KeyedState, KeyedAction>(Reduce, s_empty);
            var select = Hooks.UseCallback<Action<int>>(
                id => dispatch(new KeyedAction(KeyedActionKind.Select, Id: id)), dispatch);
            var remove = Hooks.UseCallback<Action<int>>(
                id => dispatch(new KeyedAction(KeyedActionKind.Remove, Id: id)), dispatch);

            Dispatch = dispatch;
            State = state;

            var rows = V.List(
                state.Data,
                row => row.IdText,
                row => V.Component(RenderRow, new KeyedRowProps(row, row.Id == state.Selected, select, remove)));
            return V.Div("flex flex-col", rows);
        }

        [Component(Memoize = true)]
        private static VNode RenderRow(KeyedRowProps props)
        {
            RowRenders++;
            var item = props.Item;
            return V.Div(
                props.Selected ? "flex flex-row bg-red-500" : "flex flex-row",
                V.Label(text: item.IdText),
                V.Button(text: item.Label, onClick: () => props.OnSelect(item.Id)),
                V.Button(text: "x", onClick: () => props.OnRemove(item.Id)));
        }

        private static KeyedState Reduce(KeyedState state, KeyedAction action) => action.Kind switch
        {
            KeyedActionKind.Run => new KeyedState(BuildData(action.Count), 0),
            KeyedActionKind.Add => state with { Data = Append(state.Data, BuildData(action.Count)) },
            KeyedActionKind.Update => state with { Data = UpdateEveryTenth(state.Data) },
            KeyedActionKind.Select => state with { Selected = action.Id },
            KeyedActionKind.Swap => state with { Data = Swap(state.Data, 1, 998) },
            KeyedActionKind.Remove => state with { Data = Without(state.Data, action.Id) },
            _ => new KeyedState(Array.Empty<KeyedRow>(), 0),
        };

        private static KeyedRow[] BuildData(int count)
        {
            var data = new KeyedRow[count];
            for (var i = 0; i < count; i++)
            {
                var label = $"{Pick(s_adjectives)} {Pick(s_colours)} {Pick(s_nouns)}";
                data[i] = new KeyedRow(s_nextId++, label);
            }
            return data;
        }

        private static string Pick(string[] words) => words[s_random.Next(words.Length)];

        private static KeyedRow[] Append(KeyedRow[] data, KeyedRow[] added)
        {
            var result = new KeyedRow[data.Length + added.Length];
            Array.Copy(data, result, data.Length);
            Array.Copy(added, 0, result, data.Length, added.Length);
            return result;
        }

        private static KeyedRow[] UpdateEveryTenth(KeyedRow[] data)
        {
            var result = (KeyedRow[])data.Clone();
            for (var i = 0; i < result.Length; i += 10)
            {
                result[i] = new KeyedRow(result[i].Id, result[i].Label + " !!!");
            }
            return result;
        }

        private static KeyedRow[] Swap(KeyedRow[] data, int first, int second)
        {
            if (data.Length <= second)
            {
                return data;
            }
            var result = (KeyedRow[])data.Clone();
            (result[first], result[second]) = (result[second], result[first]);
            return result;
        }

        private static KeyedRow[] Without(KeyedRow[] data, int id)
        {
            var kept = new List<KeyedRow>(data.Length);
            foreach (var row in data)
            {
                if (row.Id != id)
                {
                    kept.Add(row);
                }
            }
            return kept.ToArray();
        }
    }
}
