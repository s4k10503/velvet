using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins, at the public hook surface, the equality branches that <see cref="StateUpdater{T}"/>'s remarks spell
    /// out to callers and that the <c>comparer</c> parameter of <see cref="Hooks.UseStore{TStore,TSel}"/> points
    /// at. A rule spelled out in prose drifts unless something fails when it changes. <c>ObjectIsTests</c>
    /// and <c>ContextProviderObjectIsTests</c> call that comparer directly, so what these cases add is the
    /// routing: they also fail when a hook stops reaching it, which a direct call cannot see.
    /// <list type="bullet">
    /// <item>A <c>UseState</c> setter handed a freshly concatenated string of equal content bails: strings compare
    /// by ordinal content, not by instance.</item>
    /// <item>A <c>UseState</c> setter handed a distinct struct instance with equal fields bails: a value type that
    /// is not float or double compares through <c>EqualityComparer&lt;T&gt;.Default</c>, which is the branch a
    /// <c>record struct</c> takes too.</item>
    /// <item>A <c>UseState</c> setter handed a fresh <c>record class</c> instance of equal content re-renders,
    /// even though the record's own <c>Equals</c> calls the two instances equal: a reference type other than
    /// string compares by instance.</item>
    /// <item><c>UseStore</c>'s default comparer follows the same rule rather than
    /// <c>EqualityComparer&lt;TSel&gt;.Default</c>, which is what separates the two on a <c>record class</c>
    /// selector.</item>
    /// </list>
    /// </summary>
    /// <remarks>
    /// Uses the <c>[Component] static VNode</c> + <c>V.Mount</c> + static-field exposure pattern of
    /// <c>UseStateTests</c> and <c>UseStoreTests</c>, with per-region <c>Reset{Region}()</c> helpers.
    /// The two bail cases drive a real change through the setter before the equal one, so the committed value
    /// folded into the assertion separates a bail from a setter that was never reaching the state slot.
    /// </remarks>
    [TestFixture]
    internal sealed class HookBailoutEqualityTests
    {
        private VisualElement _root;

        [SetUp]
        public void SetUp()
        {
            _root = new VisualElement();
            ResetText();
            ResetPoint();
            ResetRecordState();
            ResetPayloadSelector();
        }

        // GREEN_ON_BASE(characterization): this branch changes no production code — it corrects prose
        // that restated the comparer — so every case here is green on both sides. What shows this one
        // can fail is the comparer's string branch perturbed to compare by reference, measured: the
        // rebuilt-but-equal string then re-renders.
        [Test]
        public void Given_StringState_When_SetterInvokedWithRebuiltEqualString_Then_NoRerender()
        {
            // Arrange
            s_textInitial = "alpha";
            using var mounted = V.Mount(_root, V.Component(TextRender, key: "text"));
            s_textSetter.Invoke(BuildBeta());
            mounted.FlushStateForTest();
            var renderCountAfterChange = s_textRenderCount;
            var rebuilt = BuildBeta();

            // Act
            s_textSetter.Invoke(rebuilt);
            mounted.FlushStateForTest();

            // Assert — a distinct instance rules out the reference branch as the reason it bailed, and the
            // committed value rules out a setter that never reached the slot
            Assert.That(
                (ReferenceEquals(s_textLastValue, rebuilt), s_textLastValue, s_textRenderCount),
                Is.EqualTo((false, "beta-1", renderCountAfterChange)));
        }

        // GREEN_ON_BASE(characterization): this branch changes no production code — it corrects prose
        // that restated the comparer — so every case here is green on both sides. What shows this one
        // can fail is the comparer's value-type fall-through perturbed to compare by reference, measured:
        // the field-equal struct then re-renders.
        [Test]
        public void Given_StructState_When_SetterInvokedWithFieldEqualInstance_Then_NoRerender()
        {
            // Arrange
            s_pointInitial = new Point(0, 0);
            using var mounted = V.Mount(_root, V.Component(PointRender, key: "point"));
            s_pointSetter.Invoke(new Point(3, 4));
            mounted.FlushStateForTest();
            var renderCountAfterChange = s_pointRenderCount;

            // Act
            s_pointSetter.Invoke(new Point(3, 4));
            mounted.FlushStateForTest();

            // Assert — the committed value rules out a setter that never reached the slot
            Assert.That(
                (s_pointLastValue, s_pointRenderCount),
                Is.EqualTo((new Point(3, 4), renderCountAfterChange)));
        }

        // GREEN_ON_BASE(characterization): this branch changes no production code — it corrects prose
        // that restated the comparer — so every case here is green on both sides. What shows this one
        // can fail is the comparer's reference branch perturbed to EqualityComparer<T>.Default, measured:
        // the fresh record then bails instead.
        [Test]
        public void Given_RecordState_When_SetterInvokedWithFreshContentEqualInstance_Then_ComponentReRenders()
        {
            // Arrange
            s_recordInitial = new Payload(1);
            using var mounted = V.Mount(_root, V.Component(RecordStateRender, key: "record"));
            var renderCountBefore = s_recordRenderCount;
            var committed = s_recordLastValue;
            var fresh = new Payload(1);

            // Act
            s_recordSetter.Invoke(fresh);
            mounted.FlushStateForTest();

            // Assert — the record calls the two instances equal, and the setter re-rendered anyway
            Assert.That(
                (committed.Equals(fresh), s_recordRenderCount),
                Is.EqualTo((true, renderCountBefore + 1)));
        }

        // GREEN_ON_BASE(characterization): this branch changes no production code — it corrects prose
        // that restated the comparer — so every case here is green on both sides. What shows this one
        // can fail is UseStore's default comparer swapped for EqualityComparer<TSel>.Default, measured: it
        // is the only case of the four that reddens there, which is why the surface carries its own case.
        [Test]
        public void Given_DefaultComparer_When_SelectorReturnsFreshContentEqualRecord_Then_ComponentReRenders()
        {
            // Arrange
            using var store = new TestPayloadStore(new PayloadState(new Payload(1)));
            s_payloadStore = store;
            using var mounted = V.Mount(_root, V.Component(PayloadSelectorRender, key: "payload"));
            var renderCountBefore = s_payloadRenderCount;
            var committed = s_payloadLastValue;
            var fresh = new Payload(1);

            // Act
            store.SetPayload(fresh);
            mounted.FlushStateForTest();

            // Assert — the record calls the two selected values equal, so EqualityComparer<TSel>.Default as the
            // default would have skipped this re-render
            Assert.That(
                (committed.Equals(fresh), s_payloadRenderCount),
                Is.EqualTo((true, renderCountBefore + 1)));
        }

        // Concatenated at run time so the result is a fresh instance rather than an interned literal, which is
        // what lets the string case tell the ordinal branch from the reference branch.
        private static string BuildBeta() => string.Concat("beta-", 1.ToString());

        private readonly struct Point
        {
            public Point(int x, int y)
            {
                X = x;
                Y = y;
            }

            public int X { get; }
            public int Y { get; }

            public override string ToString() => $"({X}, {Y})";
        }

        private sealed record Payload(int Number);

        private sealed record PayloadState(Payload Payload);

        private sealed class TestPayloadStore : Store<PayloadState>
        {
            public TestPayloadStore(PayloadState initial) : base(initial) { }
            public void SetPayload(Payload next) => SetState(_ => new PayloadState(next));
            protected override void ResetCore() => SetState(_ => new PayloadState(new Payload(0)));
        }

        #region Text component (UseState over a string)

        private static string s_textInitial;
        private static string s_textLastValue;
        private static int s_textRenderCount;
        private static StateUpdater<string> s_textSetter;

        private static void ResetText()
        {
            s_textInitial = null;
            s_textLastValue = null;
            s_textRenderCount = 0;
            s_textSetter = default;
        }

        [Component]
        private static VNode TextRender()
        {
            s_textRenderCount++;
            var (value, setValue) = Hooks.UseState(s_textInitial);
            s_textLastValue = value;
            s_textSetter = setValue;
            return V.Label(text: value);
        }

        #endregion

        #region Point component (UseState over a plain struct)

        private static Point s_pointInitial;
        private static Point s_pointLastValue;
        private static int s_pointRenderCount;
        private static StateUpdater<Point> s_pointSetter;

        private static void ResetPoint()
        {
            s_pointInitial = default;
            s_pointLastValue = default;
            s_pointRenderCount = 0;
            s_pointSetter = default;
        }

        [Component]
        private static VNode PointRender()
        {
            s_pointRenderCount++;
            var (value, setValue) = Hooks.UseState(s_pointInitial);
            s_pointLastValue = value;
            s_pointSetter = setValue;
            return V.Label(text: value.X.ToString());
        }

        #endregion

        #region RecordState component (UseState over a record)

        private static Payload s_recordInitial;
        private static Payload s_recordLastValue;
        private static int s_recordRenderCount;
        private static StateUpdater<Payload> s_recordSetter;

        private static void ResetRecordState()
        {
            s_recordInitial = null;
            s_recordLastValue = null;
            s_recordRenderCount = 0;
            s_recordSetter = default;
        }

        [Component]
        private static VNode RecordStateRender()
        {
            s_recordRenderCount++;
            var (value, setValue) = Hooks.UseState(s_recordInitial);
            s_recordLastValue = value;
            s_recordSetter = setValue;
            return V.Label(text: value.Number.ToString());
        }

        #endregion

        #region PayloadSelector component (UseStore with a record selector, default comparer)

        private static Store<PayloadState> s_payloadStore;
        private static Payload s_payloadLastValue;
        private static int s_payloadRenderCount;

        private static void ResetPayloadSelector()
        {
            s_payloadStore = null;
            s_payloadLastValue = null;
            s_payloadRenderCount = 0;
        }

        [Component]
        private static VNode PayloadSelectorRender()
        {
            s_payloadRenderCount++;
            s_payloadLastValue = Hooks.UseStore(s_payloadStore, s => s.Payload);
            return V.Label(text: s_payloadLastValue.Number.ToString());
        }

        #endregion
    }
}
