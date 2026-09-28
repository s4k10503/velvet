using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using Velvet.Editor.Preview;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    internal sealed class PreviewControlsPanelTests
    {
        internal enum FiveOptions { A, B, C, D, E }

        internal enum SixOptions { A, B, C, D, E, F }

        [Flags]
        internal enum Mask { None = 0, X = 1, Y = 2 }

        internal sealed class Inner { public int Value; }

        internal sealed class Loop
        {
            public Loop Next;

            public static Loop Closed()
            {
                var loop = new Loop();
                loop.Next = loop;
                return loop;
            }
        }

        // The public constructor leaves abstractness as the only thing that stops Set object creating one.
        internal abstract class Shape
        {
            public int Sides;

            public Shape() { }
        }

        internal struct Point { public int X; }

        internal sealed class Frozen
        {
            public const int Max = 1;
            public readonly int Value;
        }

        internal sealed class ThrowingArgs
        {
            public ThrowingArgs() => throw new InvalidOperationException("ctor");
        }

        internal sealed class NoDefault
        {
            public int Value;

            public NoDefault(int value) => Value = value;
        }

        internal sealed class ControlArgs
        {
            public bool Flag;
            public int Count;
            public float Ratio;
            public string Title = "t";
            public Color Tint;
            public long Big = 42;
            public double Precise;
            [UnityEngine.Range(0, 1)] public double Wide;
            [UnityEngine.Range(2, 10)] public int Stepped = 2;
            [UnityEngine.Range(0.25f, 0.75f)] public float Fraction = 0.25f;
            public FiveOptions Five = FiveOptions.C;
            public SixOptions Six;
            public Mask Bits;
            public DateTime When = new(2020, 1, 2, 3, 4, 5);
            public Texture2D Image;
            public Inner Nested = new();
            public Point Position;
            public int[] Numbers = { 1, 2 };
            public List<string> Names = new() { "a" };
            public Inner Missing;
            public int[] Unset;
            public NoDefault Unbuildable;
            public int[,] Grid;
            public Vector2Int Cell;
            public Frozen Locked = new();
            public Dictionary<string, int> Lookup = new();
            public Loop Ring = Loop.Closed();
            public Shape Outline;
            public Action Callback;
        }

        private static VNode ControlsStory(ControlArgs args) => V.Div();

        private static VNode ThrowingArgsStory(ThrowingArgs args) => V.Div();

        private VisualElement _panel;

        [SetUp]
        public void SetUp()
        {
            _panel = PanelFor(StoryHandle(nameof(ControlsStory)));
        }

        private static VisualElement PanelFor(VelvetPreviewStory story)
        {
            var type = typeof(VelvetPreviewWindow).Assembly.GetType("Velvet.Editor.Preview.PreviewControlsPanel");
            Assume.That(type, Is.Not.Null, "PreviewControlsPanel must exist");
            var panel = (VisualElement)Activator.CreateInstance(type);
            type.GetMethod("SetStory").Invoke(panel, new object[] { story });
            return panel;
        }

        private static VelvetPreviewStory StoryHandle(string methodName)
        {
            var method = typeof(PreviewControlsPanelTests).GetMethod(
                methodName, BindingFlags.Static | BindingFlags.NonPublic);
            var attribute = new VelvetPreviewAttribute { Name = "Controls", Group = "ControlsFixture" };
            var ctor = typeof(VelvetPreviewStory).GetConstructor(
                BindingFlags.Instance | BindingFlags.NonPublic, null,
                new[] { typeof(MethodInfo), typeof(VelvetPreviewAttribute) }, null);
            Assume.That(ctor, Is.Not.Null, "VelvetPreviewStory's internal constructor must exist");
            return (VelvetPreviewStory)ctor.Invoke(new object[] { method, attribute });
        }

        private ControlArgs Args => (ControlArgs)_panel.GetType().GetProperty("Args").GetValue(_panel);

        private static TField Field<TField, TValue>(VisualElement scope, string label)
            where TField : BaseField<TValue> =>
            scope?.Query<TField>().Where(f => f.label == label).First();

        private Foldout CollapsedFoldoutFor(string label) =>
            _panel.Query<Foldout>().Where(f => f.text == label).First();

        private static Foldout Expanded(Foldout foldout)
        {
            foldout?.SimulateChange(true);
            return foldout;
        }

        private Foldout FoldoutFor(string label) => Expanded(CollapsedFoldoutFor(label));

        private static Label NoteStartingWith(VisualElement scope, string text) =>
            scope?.Query<Label>().Where(l => l.text.StartsWith(text, StringComparison.Ordinal)).First();

        // GREEN_ON_BASE(characterization): the base notes a story with no args the same way.
        [Test]
        public void Given_AStoryWithNoArgs_When_ControlsBuilt_Then_ItIsNotedThatThereAreNone()
        {
            // Act
            var panel = PanelFor(null);

            // Assert
            Assert.That(NoteStartingWith(panel, "No controls for this story."), Is.Not.Null);
        }

        // GREEN_ON_BASE(characterization): the base notes an args constructor that throws the same way.
        [Test]
        public void Given_AnArgsTypeWhoseConstructorThrows_When_ControlsBuilt_Then_ItIsNoted()
        {
            // Act
            var panel = PanelFor(StoryHandle(nameof(ThrowingArgsStory)));

            // Assert
            Assert.That(NoteStartingWith(panel, "Could not create args"), Is.Not.Null);
        }

        // GREEN_ON_BASE(characterization): the base already builds these five controls for these types.
        [TestCase("Flag", typeof(Toggle))]
        [TestCase("Count", typeof(IntegerField))]
        [TestCase("Ratio", typeof(FloatField))]
        [TestCase("Title", typeof(TextField))]
        [TestCase("Tint", typeof(ColorField))]
        public void Given_AScalarMember_When_ControlsBuilt_Then_ItGetsItsControl(string member, Type control)
        {
            // Act
            var labelled = _panel.Query<Label>()
                .Where(l => l.text == member && l.ClassListContains(BaseField<int>.labelUssClassName)).First();

            // Assert
            Assert.That(labelled?.parent?.GetType(), Is.EqualTo(control));
        }

        [Test]
        public void Given_ALongMember_When_ControlsBuilt_Then_ItShowsTheArgsValue()
        {
            // Act
            var field = Field<LongField, long>(_panel, nameof(ControlArgs.Big));

            // Assert
            Assert.That(field?.value, Is.EqualTo(42L));
        }

        [Test]
        public void Given_ALongMember_When_Edited_Then_TheArgsHoldTheValue()
        {
            // Act
            Field<LongField, long>(_panel, nameof(ControlArgs.Big))?.SimulateChange(7L);

            // Assert
            Assert.That(Args.Big, Is.EqualTo(7L));
        }

        [Test]
        public void Given_ADoubleMember_When_Edited_Then_TheArgsHoldTheValue()
        {
            // Act
            Field<DoubleField, double>(_panel, nameof(ControlArgs.Precise))?.SimulateChange(0.5);

            // Assert
            Assert.That(Args.Precise, Is.EqualTo(0.5));
        }

        [Test]
        public void Given_ADoubleMemberWithARange_When_ControlsBuilt_Then_ItIsANumberField()
        {
            // Act
            var field = Field<DoubleField, double>(_panel, nameof(ControlArgs.Wide));

            // Assert
            Assert.That(field, Is.Not.Null);
        }

        [Test]
        public void Given_AnIntMemberWithARange_When_ControlsBuilt_Then_ItIsASliderOverThatRange()
        {
            // Act
            var slider = Field<SliderInt, int>(_panel, nameof(ControlArgs.Stepped));

            // Assert
            Assert.That(slider == null ? null : $"{slider.lowValue}..{slider.highValue}", Is.EqualTo("2..10"));
        }

        [Test]
        public void Given_AFloatMemberWithARange_When_ControlsBuilt_Then_ItIsASliderOverThatRange()
        {
            // Act
            var slider = Field<Slider, float>(_panel, nameof(ControlArgs.Fraction));

            // Assert
            Assert.That(slider == null ? null : $"{slider.lowValue}..{slider.highValue}", Is.EqualTo("0.25..0.75"));
        }

        [Test]
        public void Given_AnIntMemberWithARange_When_ControlsBuilt_Then_ItsSliderShowsItsValue()
        {
            // Act
            var slider = Field<SliderInt, int>(_panel, nameof(ControlArgs.Stepped));

            // Assert
            Assert.That(slider?.showInputField, Is.True);
        }

        [Test]
        public void Given_AFloatMemberWithARange_When_ControlsBuilt_Then_ItsSliderShowsItsValue()
        {
            // Act
            var slider = Field<Slider, float>(_panel, nameof(ControlArgs.Fraction));

            // Assert
            Assert.That(slider?.showInputField, Is.True);
        }

        [Test]
        public void Given_AnIntMemberWithARange_When_TheSliderMoves_Then_TheArgsHoldTheValue()
        {
            // Act
            Field<SliderInt, int>(_panel, nameof(ControlArgs.Stepped))?.SimulateChange(4);

            // Assert
            Assert.That(Args.Stepped, Is.EqualTo(4));
        }

        [Test]
        public void Given_AFloatMemberWithARange_When_TheSliderMoves_Then_TheArgsHoldTheValue()
        {
            // Act
            Field<Slider, float>(_panel, nameof(ControlArgs.Fraction))?.SimulateChange(0.5f);

            // Assert
            Assert.That(Args.Fraction, Is.EqualTo(0.5f));
        }

        [Test]
        public void Given_AnEnumOfFiveOptions_When_ControlsBuilt_Then_ItsRadioSelectsTheArgsValue()
        {
            // Act
            var radio = Field<RadioButtonGroup, int>(_panel, nameof(ControlArgs.Five));

            // Assert
            Assert.That(radio?.value, Is.EqualTo((int)FiveOptions.C));
        }

        [Test]
        public void Given_AnEnumOfFiveOptions_When_ARadioOptionIsPicked_Then_TheArgsHoldThatValue()
        {
            // Act
            Field<RadioButtonGroup, int>(_panel, nameof(ControlArgs.Five))?.SimulateChange(3);

            // Assert
            Assert.That(Args.Five, Is.EqualTo(FiveOptions.D));
        }

        // GREEN_ON_BASE(characterization): the base shows every enum as a select.
        // Above five options the branch still does.
        [Test]
        public void Given_AnEnumOfSixOptions_When_ControlsBuilt_Then_ItIsASelect()
        {
            // Act
            var select = Field<EnumField, Enum>(_panel, nameof(ControlArgs.Six));

            // Assert
            Assert.That(select, Is.Not.Null);
        }

        [Test]
        public void Given_AFlagsEnum_When_TwoFlagsAreChecked_Then_TheArgsHoldBoth()
        {
            // Act
            Field<EnumFlagsField, Enum>(_panel, nameof(ControlArgs.Bits))?.SimulateChange(Mask.X | Mask.Y);

            // Assert
            Assert.That(Args.Bits, Is.EqualTo(Mask.X | Mask.Y));
        }

        [Test]
        public void Given_ADateMember_When_ControlsBuilt_Then_ItShowsTheArgsDate()
        {
            // Act
            var date = Field<TextField, string>(_panel, nameof(ControlArgs.When));

            // Assert
            Assert.That(date?.value, Is.EqualTo("2020-01-02 03:04:05"));
        }

        [Test]
        public void Given_ADateMember_When_ADateIsTyped_Then_TheArgsHoldIt()
        {
            // Act
            Field<TextField, string>(_panel, nameof(ControlArgs.When))?.SimulateChange("2021-06-07 08:09:10");

            // Assert
            Assert.That(Args.When, Is.EqualTo(new DateTime(2021, 6, 7, 8, 9, 10)));
        }

        // GREEN_ON_BASE(characterization): the base has no date control, so its date never moves.
        // The branch's control has to leave it where it was for text that is not a date.
        [Test]
        public void Given_ADateMember_When_TextThatIsNotADateIsTyped_Then_TheArgsKeepTheirDate()
        {
            // Act
            Field<TextField, string>(_panel, nameof(ControlArgs.When))?.SimulateChange("not a date");

            // Assert
            Assert.That(Args.When, Is.EqualTo(new DateTime(2020, 1, 2, 3, 4, 5)));
        }

        [Test]
        public void Given_AnAssetMember_When_ControlsBuilt_Then_ItsPickerIsForThatAssetType()
        {
            // Act
            var picker = Field<ObjectField, UnityEngine.Object>(_panel, nameof(ControlArgs.Image));

            // Assert
            Assert.That(picker?.objectType, Is.EqualTo(typeof(Texture2D)));
        }

        [Test]
        public void Given_AnAssetMember_When_ControlsBuilt_Then_ItsPickerOffersNoSceneObjects()
        {
            // Act
            var picker = Field<ObjectField, UnityEngine.Object>(_panel, nameof(ControlArgs.Image));

            // Assert
            Assert.That(picker?.allowSceneObjects, Is.False);
        }

        [Test]
        public void Given_AnAssetMember_When_AnAssetIsPicked_Then_TheArgsHoldIt()
        {
            // Arrange
            var texture = new Texture2D(1, 1);
            try
            {
                // Act
                Field<ObjectField, UnityEngine.Object>(_panel, nameof(ControlArgs.Image))?.SimulateChange(texture);

                // Assert
                Assert.That(Args.Image, Is.SameAs(texture));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(texture);
            }
        }

        [Test]
        public void Given_ANestedObjectMember_When_ItsMemberIsEdited_Then_TheArgsHoldTheValue()
        {
            // Act
            Field<IntegerField, int>(FoldoutFor(nameof(ControlArgs.Nested)), nameof(Inner.Value))?.SimulateChange(5);

            // Assert
            Assert.That(Args.Nested.Value, Is.EqualTo(5));
        }

        [Test]
        public void Given_ANestedStructMember_When_ItsMemberIsEdited_Then_TheArgsHoldTheValue()
        {
            // Act
            Field<IntegerField, int>(FoldoutFor(nameof(ControlArgs.Position)), nameof(Point.X))?.SimulateChange(3);

            // Assert
            Assert.That(Args.Position.X, Is.EqualTo(3));
        }

        [Test]
        public void Given_AnArrayMember_When_ControlsBuilt_Then_ItsLengthFieldShowsItsLength()
        {
            // Act
            var length = Field<IntegerField, int>(FoldoutFor(nameof(ControlArgs.Numbers)), "Length");

            // Assert
            Assert.That(length?.value, Is.EqualTo(2));
        }

        [Test]
        public void Given_AnArrayMember_When_AnElementIsEdited_Then_ArgsChangedIsRaised()
        {
            // Arrange
            var raised = 0;
            _panel.GetType().GetEvent("ArgsChanged").AddEventHandler(_panel, (Action<object>)(_ => raised++));

            // Act
            Field<IntegerField, int>(FoldoutFor(nameof(ControlArgs.Numbers)), "[0]")?.SimulateChange(5);

            // Assert
            Assert.That(raised, Is.EqualTo(1));
        }

        [Test]
        public void Given_AnArrayMember_When_AnElementIsEdited_Then_TheArgsHoldTheValue()
        {
            // Act
            Field<IntegerField, int>(FoldoutFor(nameof(ControlArgs.Numbers)), "[1]")?.SimulateChange(9);

            // Assert
            Assert.That(string.Join(",", Args.Numbers), Is.EqualTo("1,9"));
        }

        [Test]
        public void Given_AnArrayMember_When_ItsLengthGrows_Then_TheArgsHoldTheLongerArray()
        {
            // Act
            Field<IntegerField, int>(FoldoutFor(nameof(ControlArgs.Numbers)), "Length")?.SimulateChange(3);

            // Assert
            Assert.That(string.Join(",", Args.Numbers), Is.EqualTo("1,2,0"));
        }

        [Test]
        public void Given_AnArrayMember_When_ItsLengthGrows_Then_TheNewElementGetsAControl()
        {
            // Arrange
            var numbers = FoldoutFor(nameof(ControlArgs.Numbers));

            // Act
            Field<IntegerField, int>(numbers, "Length")?.SimulateChange(3);

            // Assert
            Assert.That(Field<IntegerField, int>(numbers, "[2]"), Is.Not.Null);
        }

        [Test]
        public void Given_AnArrayMember_When_ANegativeLengthIsTyped_Then_TheArgsHoldAnEmptyArray()
        {
            // Act
            Field<IntegerField, int>(FoldoutFor(nameof(ControlArgs.Numbers)), "Length")?.SimulateChange(-1);

            // Assert
            Assert.That(Args.Numbers, Is.Empty);
        }

        [Test]
        public void Given_AListMember_When_ItsLengthGrows_Then_TheArgsHoldADefaultElement()
        {
            // Act
            Field<IntegerField, int>(FoldoutFor(nameof(ControlArgs.Names)), "Length")?.SimulateChange(2);

            // Assert
            Assert.That(string.Join(",", Args.Names.Select(n => n ?? "null")), Is.EqualTo("a,null"));
        }

        [Test]
        public void Given_AListMember_When_ItsLengthShrinks_Then_TheArgsHoldTheShorterList()
        {
            // Act
            Field<IntegerField, int>(FoldoutFor(nameof(ControlArgs.Names)), "Length")?.SimulateChange(0);

            // Assert
            Assert.That(Args.Names, Is.Empty);
        }

        [Test]
        public void Given_AListMember_When_AnElementIsEdited_Then_TheArgsHoldTheValue()
        {
            // Act
            Field<TextField, string>(FoldoutFor(nameof(ControlArgs.Names)), "[0]")?.SimulateChange("z");

            // Assert
            Assert.That(string.Join(",", Args.Names), Is.EqualTo("z"));
        }

        [Test]
        public void Given_ANullObjectMember_When_SetObjectIsClicked_Then_TheArgsHoldANewInstance()
        {
            // Act
            FoldoutFor(nameof(ControlArgs.Missing))?.Q<Button>()?.SimulateClick();

            // Assert
            Assert.That(Args.Missing, Is.Not.Null);
        }

        [Test]
        public void Given_ANullObjectMember_When_SetObjectIsClicked_Then_TheNewInstanceGetsControls()
        {
            // Arrange
            var missing = FoldoutFor(nameof(ControlArgs.Missing));

            // Act
            missing?.Q<Button>()?.SimulateClick();

            // Assert
            Assert.That(Field<IntegerField, int>(missing, nameof(Inner.Value)), Is.Not.Null);
        }

        [Test]
        public void Given_ANullObjectMember_When_SetObjectIsClicked_Then_TheButtonIsGone()
        {
            // Arrange
            var missing = FoldoutFor(nameof(ControlArgs.Missing));

            // Act
            missing?.Q<Button>()?.SimulateClick();

            // Assert
            Assert.That((missing != null, missing?.Q<Button>() == null), Is.EqualTo((true, true)));
        }

        [Test]
        public void Given_ANullArrayMember_When_SetObjectIsClicked_Then_TheArgsHoldAnEmptyArray()
        {
            // Act
            FoldoutFor(nameof(ControlArgs.Unset))?.Q<Button>()?.SimulateClick();

            // Assert
            Assert.That(Args.Unset, Is.Not.Null.And.Empty);
        }

        [Test]
        public void Given_ANullMemberWithNoParameterlessConstructor_When_ControlsBuilt_Then_ItOffersNoSetButton()
        {
            // Act
            var unbuildable = FoldoutFor(nameof(ControlArgs.Unbuildable));

            // Assert
            Assert.That(unbuildable?.Query<Label>().Where(l => l.text == "null").First(), Is.Not.Null);
        }

        [Test]
        public void Given_APropertyOnlyStructMember_When_ItsPropertyIsEdited_Then_TheArgsHoldTheValue()
        {
            // Act
            Field<IntegerField, int>(FoldoutFor(nameof(ControlArgs.Cell)), "x")?.SimulateChange(6);

            // Assert
            Assert.That(Args.Cell.x, Is.EqualTo(6));
        }

        // GREEN_ON_BASE(characterization): the base shows no struct foldout at all.
        // The branch's has to leave out a property it cannot write.
        [Test]
        public void Given_AStructWithAReadOnlyProperty_When_ControlsBuilt_Then_ThatPropertyGetsNoControl()
        {
            // Act
            var magnitude = Field<FloatField, float>(FoldoutFor(nameof(ControlArgs.Cell)), "magnitude");

            // Assert
            Assert.That(magnitude, Is.Null);
        }

        // GREEN_ON_BASE(characterization): the base notes an object with only fixed fields as unsupported.
        // The branch's object control finds nothing on it to write and has to note it the same way.
        [Test]
        public void Given_AnObjectWithOnlyReadOnlyAndConstFields_When_ControlsBuilt_Then_ItIsNotedAsUnsupported()
        {
            // Act
            var note = NoteStartingWith(_panel, nameof(ControlArgs.Locked));

            // Assert
            Assert.That(note?.text, Does.Contain("unsupported"));
        }

        // GREEN_ON_BASE(characterization): the base notes a dictionary as unsupported.
        // A dictionary is neither an array nor a list and has no writable member, so the branch has to note it
        // the same way.
        [Test]
        public void Given_ADictionaryMember_When_ControlsBuilt_Then_ItIsNotedAsUnsupported()
        {
            // Act
            var note = NoteStartingWith(_panel, nameof(ControlArgs.Lookup));

            // Assert
            Assert.That(note?.text, Does.Contain("unsupported"));
        }

        // GREEN_ON_BASE(characterization): the base notes a two-dimensional array as unsupported.
        // The branch's object control takes an array of one dimension and has to note this one the same way.
        [Test]
        public void Given_ATwoDimensionalArrayMember_When_ControlsBuilt_Then_ItIsNotedAsUnsupported()
        {
            // Act
            var note = NoteStartingWith(_panel, nameof(ControlArgs.Grid));

            // Assert
            Assert.That(note?.text, Does.Contain("unsupported"));
        }

        [Test]
        public void Given_AnObjectMember_When_ControlsBuilt_Then_ItsFoldoutStartsCollapsed()
        {
            // Act
            var nested = CollapsedFoldoutFor(nameof(ControlArgs.Nested));

            // Assert
            Assert.That(nested?.value, Is.False);
        }

        [Test]
        public void Given_AMemberEdit_When_Applied_Then_TheArgsAreANewInstanceAndTheOldOneIsUnchanged()
        {
            // Arrange
            var before = Args;

            // Act
            Field<TextField, string>(_panel, nameof(ControlArgs.Title))?.SimulateChange("z");

            // Assert
            Assert.That((ReferenceEquals(before, Args), before.Title), Is.EqualTo((false, "t")));
        }

        [Test]
        public void Given_ANestedObjectEdit_When_Applied_Then_TheObjectTheArgsHeldBeforeIsUnchanged()
        {
            // Arrange
            var before = Args.Nested;

            // Act
            Field<IntegerField, int>(FoldoutFor(nameof(ControlArgs.Nested)), nameof(Inner.Value))?.SimulateChange(5);

            // Assert
            Assert.That((Args.Nested.Value, before.Value), Is.EqualTo((5, 0)));
        }

        [Test]
        public void Given_AListElementEdit_When_Applied_Then_TheListTheArgsHeldBeforeIsUnchanged()
        {
            // Arrange
            var before = Args.Names;

            // Act
            Field<TextField, string>(FoldoutFor(nameof(ControlArgs.Names)), "[0]")?.SimulateChange("z");

            // Assert
            Assert.That((string.Join(",", Args.Names), before[0]), Is.EqualTo(("z", "a")));
        }

        [Test]
        public void Given_AnArrayElementEdit_When_Applied_Then_TheArrayTheArgsHeldBeforeIsUnchanged()
        {
            // Arrange
            var before = Args.Numbers;

            // Act
            Field<IntegerField, int>(FoldoutFor(nameof(ControlArgs.Numbers)), "[0]")?.SimulateChange(7);

            // Assert
            Assert.That((string.Join(",", Args.Numbers), before[0]), Is.EqualTo(("7,2", 1)));
        }

        [Test]
        public void Given_AnObjectThatRefersBackToItself_When_ItsReferenceIsExpanded_Then_ACycleIsNoted()
        {
            // Act
            var next = Expanded(FoldoutFor(nameof(ControlArgs.Ring))?.Query<Foldout>()
                .Where(f => f.text == nameof(Loop.Next)).First());

            // Assert
            Assert.That(next?.Query<Label>().Where(l => l.text == "cycle").First(), Is.Not.Null);
        }

        [Test]
        public void Given_ANullAbstractMember_When_Expanded_Then_ItOffersNoSetButton()
        {
            // Act
            var outline = FoldoutFor(nameof(ControlArgs.Outline));

            // Assert
            Assert.That(outline?.Query<Label>().Where(l => l.text == "null").First(), Is.Not.Null);
        }

        // GREEN_ON_BASE(characterization): the base notes a delegate member as unsupported.
        // The branch's object control finds nothing on a delegate to edit and has to note it the same way.
        [Test]
        public void Given_ADelegateMember_When_ControlsBuilt_Then_ItIsNotedAsUnsupported()
        {
            // Act
            var note = NoteStartingWith(_panel, nameof(ControlArgs.Callback));

            // Assert
            Assert.That(note?.text, Does.Contain("unsupported"));
        }
    }
}
