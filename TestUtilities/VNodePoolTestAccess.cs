using System;
using System.Reflection;

namespace Velvet.TestUtilities
{
    /// <summary>
    /// Drains and measures <c>VNodePool</c>'s process-wide recyclable-element pools. Every member goes
    /// through reflection because production types carry no test-only members, and because the pool type is
    /// a private nested one that no signature here could name even if they did.
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
        private const string TextFieldPoolFieldName = "s_textFieldPool";
        private const string ClearMethodName = "Clear";
        private const string CountPropertyName = "Count";

        // Bypasses: nothing — it resets a static pool, which no production path does.
        public static void ClearLabelPoolForTest() => Clear(LabelPoolFieldName);

        // Bypasses: nothing — it resets a static pool, which no production path does.
        public static void ClearButtonPoolForTest() => Clear(ButtonPoolFieldName);

        // Bypasses: nothing — it resets a static pool, which no production path does.
        public static void ClearTogglePoolForTest() => Clear(TogglePoolFieldName);

        // Bypasses: nothing — it resets a static pool, which no production path does.
        public static void ClearSliderPoolForTest() => Clear(SliderPoolFieldName);

        // Bypasses: nothing — it resets a static pool, which no production path does.
        public static void ClearTextFieldPoolForTest() => Clear(TextFieldPoolFieldName);

        // Bypasses: nothing — it reads a static pool's depth.
        public static int LabelPoolCountForTest => Count(LabelPoolFieldName);

        // Bypasses: nothing — it reads a static pool's depth.
        public static int ButtonPoolCountForTest => Count(ButtonPoolFieldName);

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

        private static int Count(string fieldName)
        {
            var pool = Pool(fieldName);
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
