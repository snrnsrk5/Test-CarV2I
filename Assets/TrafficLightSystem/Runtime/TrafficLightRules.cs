using System;

namespace ModularTrafficLights
{
    public enum SignalLamp { Red, Yellow, Left, Green }
    public enum VehicleAspect { AllOff, Red, Yellow, Green, Left, RedAndLeft, GreenAndLeft }
    public enum PedestrianVariant { None, Number, Bar }

    /// <summary>Pure rules, independent of the Unity scene and coordinate axes.</summary>
    public static class TrafficLightRules
    {
        // A,B,C,D,E,F,G = top, upper-right, lower-right, bottom,
        // lower-left, upper-left, middle.
        private static readonly int[] DigitMasks =
            { 0x3f, 0x06, 0x5b, 0x4f, 0x66, 0x6d, 0x7d, 0x07, 0x7f, 0x6f };

        public static int ClampSignalCount(int count) => Math.Max(1, Math.Min(3, count));
        public static int ClampSignCount(int count, int signals) => Math.Max(0, Math.Min(ClampSignalCount(signals), count));
        public static bool IsHorizontalProp(string name) => name != null && name.StartsWith("WIDTH_", StringComparison.Ordinal);
        public static bool IsVerticalProp(string name) => name != null && name.StartsWith("LENGTH_", StringComparison.Ordinal);
        public static int DigitMask(int digit) => digit >= 0 && digit <= 9 ? DigitMasks[digit] : 0;

        public static int SegmentBit(string sourceCode)
        {
            switch (sourceCode)
            {
                case "t": return 0;
                case "rt": return 1;
                case "ru": return 2;
                case "u": return 3;
                case "lu": return 4;
                case "lt": return 5;
                case "c": return 6;
                default: return -1;
            }
        }

        // Actual inspected source geometry: digit 1 = units, digit 2 = tens.
        public static bool NumberSegmentOn(int seconds, int sourceDigit, string code, bool blankLeadingZero)
        {
            seconds = Math.Max(0, Math.Min(99, seconds));
            if (sourceDigit != 1 && sourceDigit != 2) return false;
            if (sourceDigit == 2 && blankLeadingZero && seconds < 10) return false;
            int bit = SegmentBit(code);
            if (bit < 0) return false;
            int digit = sourceDigit == 1 ? seconds % 10 : seconds / 10;
            return (DigitMask(digit) & (1 << bit)) != 0;
        }

        public static int BarSegments(int remaining, int total, int segmentCount = 9)
        {
            if (total <= 0 || remaining <= 0 || segmentCount <= 0) return 0;
            remaining = Math.Min(total, remaining);
            return (int)Math.Ceiling((double)remaining * segmentCount / total);
        }

        public static bool LampOn(VehicleAspect aspect, SignalLamp lamp)
        {
            switch (aspect)
            {
                case VehicleAspect.Red: return lamp == SignalLamp.Red;
                case VehicleAspect.Yellow: return lamp == SignalLamp.Yellow;
                case VehicleAspect.Green: return lamp == SignalLamp.Green;
                case VehicleAspect.Left: return lamp == SignalLamp.Left;
                case VehicleAspect.RedAndLeft: return lamp == SignalLamp.Red || lamp == SignalLamp.Left;
                case VehicleAspect.GreenAndLeft: return lamp == SignalLamp.Green || lamp == SignalLamp.Left;
                default: return false;
            }
        }
    }
}
