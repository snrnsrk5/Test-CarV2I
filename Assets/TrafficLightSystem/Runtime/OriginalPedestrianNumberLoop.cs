using System;
namespace ModularTrafficLights
{
    // Sampled from TrafficLightAnim.blend at 1 FPS; source segment codes retained.
    public static class OriginalPedestrianNumberLoop
    {
        public readonly struct Frame
        {
            public readonly float time;
            public readonly bool red, green;
            public readonly int unitsMask, tensMask;
            public Frame(float time, bool red, bool green, int units, int tens)
            { this.time=time; this.red=red; this.green=green; unitsMask=units; tensMask=tens; }
        }
        public const float AnimationSeconds = 30f;
        internal static readonly Frame[] Frames = new Frame[]
        {
            new Frame(0.0f, false, true, 63, 79),
            new Frame(1.0f, false, true, 111, 91),
            new Frame(2.0f, false, true, 127, 91),
            new Frame(3.0f, false, true, 7, 91),
            new Frame(4.0f, false, true, 125, 91),
            new Frame(5.0f, false, true, 109, 91),
            new Frame(6.0f, false, true, 102, 91),
            new Frame(7.0f, false, true, 79, 91),
            new Frame(8.0f, false, true, 91, 91),
            new Frame(9.0f, false, true, 6, 91),
            new Frame(10.0f, false, true, 63, 91),
            new Frame(11.0f, false, true, 111, 6),
            new Frame(12.0f, false, true, 127, 6),
            new Frame(13.0f, false, true, 7, 6),
            new Frame(14.0f, false, true, 125, 6),
            new Frame(15.0f, false, true, 109, 6),
            new Frame(16.0f, false, true, 102, 6),
            new Frame(17.0f, false, true, 79, 6),
            new Frame(18.0f, false, true, 91, 6),
            new Frame(19.0f, false, true, 6, 6),
            new Frame(20.0f, false, true, 63, 6),
            new Frame(21.0f, false, true, 111, 0),
            new Frame(22.0f, false, true, 127, 0),
            new Frame(23.0f, false, false, 7, 0),
            new Frame(23.5f, false, true, 7, 0),
            new Frame(24.0f, false, false, 125, 0),
            new Frame(24.5f, false, true, 125, 0),
            new Frame(25.0f, false, false, 109, 0),
            new Frame(25.5f, false, true, 109, 0),
            new Frame(26.0f, false, false, 102, 0),
            new Frame(26.5f, false, true, 102, 0),
            new Frame(27.0f, false, false, 79, 0),
            new Frame(27.5f, false, true, 79, 0),
            new Frame(28.0f, false, false, 91, 0),
            new Frame(28.5f, false, true, 91, 0),
            new Frame(29.0f, false, false, 6, 0),
            new Frame(29.5f, false, true, 6, 0),
            new Frame(30.0f, true, false, 0, 0),
        };
        public static Frame Evaluate(float seconds)
        {
            int low=0, high=Frames.Length-1;
            while(low<high)
            { int mid=(low+high+1)/2; if(Frames[mid].time<=seconds) low=mid; else high=mid-1; }
            return Frames[low];
        }
    }
}
