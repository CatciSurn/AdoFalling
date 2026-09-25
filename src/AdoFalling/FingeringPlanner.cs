using System;
using System.Collections.Generic;

namespace AdoFalling
{
    /// <summary>
    /// Plans a human fingering for the chart and writes it into the note lanes.
    ///
    /// The rules model what two hands on a 4-key row actually do (the same technique
    /// categories ReADOFAIMacro works with, adapted so that a single lane is never
    /// hammered):
    ///
    ///   * notes closer together than the roll window ("FingeringWindowMs", 180 ms by
    ///     default) cannot be repeated by one finger, so they are played as a roll (轮指):
    ///     the lane walks one step in the current direction every note, wrapping around the
    ///     four lanes. Each new fast run flips the direction, which reads as the hands
    ///     taking turns;
    ///   * slower notes are played with the two index fingers, so the lane alternates
    ///     between the middle lanes 1 and 2 (两指交替) instead of staying on one key;
    ///   * the result never places two consecutive notes on the same lane and never leaves
    ///     a lane idle for long.
    ///
    /// Lower the window to roll more, raise it to tap more; it is live-tunable from the
    /// F5 panel's 排键 section.
    /// </summary>
    internal static class FingeringPlanner
    {
        public const int LaneCount = FallingView.LaneCount;
        public const double DefaultWindowMs = 180.0;

        /// <summary>The 2-lane pair the slow notes alternate between (the index fingers).</summary>
        private const int TapLaneA = 1;
        private const int TapLaneB = 2;

        /// <summary>
        /// Re-lane every note in <paramref name="notes"/> (in HitTime order) and return a
        /// one-line summary for the log. <paramref name="windowMs"/> is the largest gap
        /// (milliseconds) that still counts as a roll; 0 or invalid values fall back to the
        /// default window.
        /// </summary>
        public static string Assign(List<NoteInstance> notes, double windowMs)
        {
            int count = notes.Count;
            if (count == 0)
                return "fingering: no notes";

            double window = windowMs / 1000.0;
            if (double.IsNaN(window) || double.IsInfinity(window) || window <= 0.0001)
                window = DefaultWindowMs / 1000.0;

            int activeWindow = (int)Math.Round(window * 1000.0);
            int[] laneUse = new int[LaneCount];
            int lane = TapLaneA;
            int direction = 1;
            bool inRoll = false;
            int rolls = 0;
            int taps = 0;

            for (int i = 0; i < count; i++)
            {
                if (i > 0)
                {
                    double gap = notes[i].HitTime - notes[i - 1].HitTime;
                    if (gap < window - 1e-9)
                    {
                        // Roll: step one lane; a fresh run reverses the direction so the
                        // hands alternate instead of always sweeping the same way.
                        if (!inRoll)
                            direction = -direction;
                        lane = (lane + direction + LaneCount) % LaneCount;
                        inRoll = true;
                        rolls++;
                    }
                    else
                    {
                        // Tap: alternate the two index lanes.
                        lane = (lane == TapLaneB) ? TapLaneA : TapLaneB;
                        inRoll = false;
                        taps++;
                    }
                }

                NoteInstance n = notes[i];
                n.Lane = lane;
                notes[i] = n;
                laneUse[lane]++;
            }

            return string.Format(
                "fingering: window {0}ms, {1} notes -> roll {2}, tap {3}, lanes {4}/{5}/{6}/{7}",
                activeWindow, count, rolls, taps, laneUse[0], laneUse[1], laneUse[2], laneUse[3]);
        }
    }
}
