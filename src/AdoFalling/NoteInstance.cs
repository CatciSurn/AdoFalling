using UnityEngine;

namespace AdoFalling
{
    /// <summary>One note in the falling timeline.</summary>
    internal struct NoteInstance
    {
        /// <summary>Song time (seconds) at which the note must reach the judgement line.</summary>
        public double HitTime;

        /// <summary>0 .. FallingView.LaneCount-1.</summary>
        public int Lane;

        /// <summary>Signed distance in pixels from the judgement line (positive = above).</summary>
        public float DistancePx;

        /// <summary>Chart tile this note came from (scrFloor.seqID), or -1 for demo notes.</summary>
        public int SeqId;

        /// <summary>Seconds the tile must be held (from the game's own timeline), or 0.</summary>
        public double HoldSeconds;

        /// <summary>
        /// The live scrFloor this note came from, so the renderer can read the game's own hold
        /// progress (scrFloor.holdCompletion) instead of guessing it from the clock.
        /// </summary>
        public object Floor;

        public Color Color;
        public bool Active;

        /// <summary>Set once the game has registered a successful hit on this note, so the
        /// overlay can hide it instead of letting it keep falling past the line.</summary>
        public bool Consumed;
    }
}
