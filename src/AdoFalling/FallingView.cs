using UnityEngine;

namespace AdoFalling
{
    /// <summary>
    /// Screen geometry for the falling overlay: lane positions, judgement line height and
    /// all the tunables resolved into pixels. The actual drawing is done by
    /// <see cref="OverlayRenderer"/>; this type holds no graphics objects.
    /// </summary>
    internal sealed class FallingView
    {
        public const int LaneCount = 4;

        /// <summary>Judgement line height from the bottom of the screen, in pixels.</summary>
        public float JudgeY { get; private set; }

        /// <summary>Distance from the judgement line to the top of the screen.</summary>
        public float FallDistance { get { return Mathf.Max(1f, Screen.height - JudgeY); } }

        public float LaneWidthPx { get { return _laneW; } }

        public bool Visible { get; private set; }

        private readonly float[] _laneX = new float[LaneCount];
        private float _laneW;

        public FallingView()
        {
            Visible = true;
            Layout(true);
        }

        public void Build()
        {
            Layout(true);
            FallingPlugin.Log.LogInfo("overlay geometry ready (" + LaneCount + " lanes)");
        }

        public void SetVisible(bool on)
        {
            Visible = on;
        }

        /// <summary>
        /// Recompute pixel geometry from the current config and screen size. Cheap enough
        /// to call every frame, and it must be, because every value here is user-tunable.
        /// </summary>
        public void Layout(bool force)
        {
            float w = Screen.width;
            float h = Screen.height;
            FallingConfig c = FallingPlugin.Cfg;
            float refScale = h / 1080f;

            _laneW = c.LaneWidth.Value * refScale;
            float gap = c.LaneGap.Value * refScale;
            float total = LaneCount * _laneW + (LaneCount - 1) * gap;
            float x0 = (w - total) * 0.5f;
            for (int i = 0; i < LaneCount; i++)
            {
                int idx = c.MirrorDirection.Value ? (LaneCount - 1 - i) : i;
                // Lane order 0/2 and 1/3 swapped, as requested: lane 0 is drawn where lane 2
                // used to be, lane 1 where lane 3 was, and vice versa.
                idx ^= 2;
                _laneX[i] = x0 + idx * (_laneW + gap);
            }
            JudgeY = c.JudgeLineY.Value * h;
        }

        /// <summary>Left edge of a lane in screen pixels (UGUI x axis).</summary>
        public float LaneLeft(int lane)
        {
            if (lane < 0) lane = 0;
            if (lane >= LaneCount) lane = LaneCount - 1;
            return _laneX[lane];
        }
    }
}
