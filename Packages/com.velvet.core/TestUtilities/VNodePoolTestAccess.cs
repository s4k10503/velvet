using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine.UIElements;

namespace Velvet.TestUtilities
{
    /// <summary>
    /// Drains, fills and measures <c>VNodePool</c>'s process-wide recyclable-element pools, and reads its rented-out
    /// sets and its rental journal. Every drain and every reading goes
    /// through reflection because production types carry no test-only members, and because the pool type is
    /// a private nested one that no signature here could name even if they did; a fill goes in through the
    /// pool's own return method.
    /// <para>
    /// Every member throws — <see cref="MissingFieldException"/>, <see cref="MissingMethodException"/> or
    /// <see cref="MissingMemberException"/> — when what it reflects for is gone. Throwing is the point: a
    /// caller clears to make a pool-size assertion independent of whatever ran before it, so a clear that
    /// quietly reached nothing would leave that assertion reading another fixture's leftovers.
    /// </para>
    /// </summary>
    public static class VNodePoolTestAccess
    {
        private const string LabelPoolFieldName = "s_labelPool";
        private const string ButtonPoolFieldName = "s_buttonPool";
        private const string TogglePoolFieldName = "s_togglePool";
        private const string SliderPoolFieldName = "s_sliderPool";
        private const string SliderIntPoolFieldName = "s_sliderIntPool";
        private const string TextFieldPoolFieldName = "s_textFieldPool";
        private const string OwnedPropsFieldName = "s_ownedProps";
        private const string OwnedNodeArraysFieldName = "s_ownedNodeArrays";
        private const string RentalJournalFieldName = "s_rentalJournal";
        private const string ClearMethodName = "Clear";
        private const string CountPropertyName = "Count";
        private const int SaturationFillLimit = 1024;

        // Bypasses: nothing — it resets a static pool, which no production path does.
        public static void ClearLabelPoolForTest() => Clear(LabelPoolFieldName);

        // Bypasses: nothing — it resets a static pool, which no production path does.
        public static void ClearButtonPoolForTest() => Clear(ButtonPoolFieldName);

        // Bypasses: nothing — it resets a static pool, which no production path does.
        public static void ClearTogglePoolForTest() => Clear(TogglePoolFieldName);

        // Bypasses: nothing — it resets a static pool, which no production path does.
        public static void ClearSliderPoolForTest() => Clear(SliderPoolFieldName);

        // Bypasses: nothing — it resets a static pool, which no production path does.
        public static void ClearSliderIntPoolForTest() => Clear(SliderIntPoolFieldName);

        // Bypasses: nothing — it resets a static pool, which no production path does.
        public static void ClearTextFieldPoolForTest() => Clear(TextFieldPoolFieldName);

        // Bypasses: nothing — it reads a static pool's depth.
        public static int LabelPoolCountForTest => Count(LabelPoolFieldName);

        // Bypasses: nothing — it reads a static pool's depth.
        public static int ButtonPoolCountForTest => Count(ButtonPoolFieldName);

        // Bypasses: nothing — it reads a static pool's depth.
        public static int TextFieldPoolCountForTest => Count(TextFieldPoolFieldName);

        // Bypasses: nothing — it reads a static pool's depth.
        public static int SliderIntPoolCountForTest => Count(SliderIntPoolFieldName);

        // For a case whose identity term has to tell a patch from a discard-and-recreate at a pooled slot.
        // ChildReconciler.PatchOrReplaceAtSlot removes the occupant before it creates the replacement and
        // the pools pop the last element pushed, so with room left the discard is rented straight back as
        // the replacement. A full pool drops the return instead. Each answers whether the fill reached the
        // cap, which the caller folds into its assertion so a fill that stopped working fails the case.
        // Bypasses: FiberElementCleaner — it returns Labels no tree mounted, through ReturnLabel.
        public static bool SaturateLabelPoolForTest()
            => Saturate(LabelPoolFieldName, () => VNodePool.ReturnLabel(new Label()));

        // The Sliders are built by the pool's own factory, so a later rent gets the keyboard callbacks a pooled
        // Slider carries.
        // Bypasses: FiberElementCleaner — it returns Sliders no tree mounted, through ReturnSlider.
        public static bool SaturateSliderPoolForTest()
            => Saturate(SliderPoolFieldName, () => VNodePool.ReturnSlider(FiberSliderKeyboard.Create()));

        // Bypasses: FiberElementCleaner — it returns TextFields no tree mounted, through ReturnTextField.
        public static bool SaturateTextFieldPoolForTest()
            => Saturate(TextFieldPoolFieldName, () => VNodePool.ReturnTextField(new TextField()));

        // Bypasses: nothing — it reads how many props bags, event arrays and node arrays are rented out.
        public static (int Props, int EventArrays, int NodeArrays) RentedOutCountsForTest()
            => (Count(OwnedPropsFieldName), CountOf(OwnedEventArrays()), Count(OwnedNodeArraysFieldName));

        // The rented-out event-array set is the pool's one static HashSet<FiberEventBinding[]>, and is found
        // by that type, so a fixture reading it does not name the field.
        private static object OwnedEventArrays()
        {
            var fields = typeof(VNodePool).GetFields(BindingFlags.Static | BindingFlags.NonPublic);
            FieldInfo? match = null;
            foreach (var field in fields)
            {
                if (field.FieldType != typeof(HashSet<FiberEventBinding[]>)) continue;
                if (match != null)
                {
                    throw new AmbiguousMatchException(
                        typeof(VNodePool).FullName + " holds more than one HashSet<FiberEventBinding[]>.");
                }
                match = field;
            }
            if (match == null)
            {
                throw new MissingFieldException(typeof(VNodePool).FullName, "HashSet<FiberEventBinding[]>");
            }
            return match.GetValue(null)!;
        }

        // Bypasses: nothing — it reads whether the pool counts a props bag as rented out.
        public static bool IsRentedOutForTest(FiberElementProps props)
            => ((HashSet<FiberElementProps>)Pool(OwnedPropsFieldName)).Contains(props);

        // Bypasses: nothing — it reads how many rentals the journal holds.
        public static int RentalJournalCountForTest => Count(RentalJournalFieldName);

        // Bypasses: nothing — it reads the capacity the journal's backing array keeps.
        public static int RentalJournalCapacityForTest => ((List<object>)Pool(RentalJournalFieldName)).Capacity;

        // Fills until a return stops deepening the pool rather than counting up to the cap: the cap is a
        // private constant, and a mirror of it here would go quietly wrong the day it moves.
        private static bool Saturate(string fieldName, Action returnOne)
        {
            for (var i = 0; i < SaturationFillLimit; i++)
            {
                var before = Count(fieldName);
                returnOne();
                if (Count(fieldName) == before) return true;
            }
            return false;
        }

        private static void Clear(string fieldName)
        {
            var pool = Pool(fieldName);
            var clear = pool.GetType().GetMethod(ClearMethodName, BindingFlags.Instance | BindingFlags.Public);
            if (clear == null)
            {
                throw new MissingMethodException(pool.GetType().FullName, ClearMethodName);
            }
            clear.Invoke(pool, null);
        }

        private static int Count(string fieldName) => CountOf(Pool(fieldName));

        private static int CountOf(object pool)
        {
            var count = pool.GetType().GetProperty(CountPropertyName, BindingFlags.Instance | BindingFlags.Public);
            if (count == null)
            {
                throw new MissingMemberException(pool.GetType().FullName, CountPropertyName);
            }
            return (int)count.GetValue(pool)!;
        }

        private static object Pool(string fieldName)
        {
            var field = typeof(VNodePool).GetField(fieldName, BindingFlags.Static | BindingFlags.NonPublic);
            if (field == null)
            {
                throw new MissingFieldException(typeof(VNodePool).FullName, fieldName);
            }
            return field.GetValue(null)!;
        }
    }
}
