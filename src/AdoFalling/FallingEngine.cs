using System;
using System.Collections.Generic;
using UnityEngine;

namespace AdoFalling
{
    /// <summary>A short-lived burst drawn at the judgement line when a tile is hit.</summary>
    internal struct HitEffect
    {
        public int Lane;
        public float Age;        // seconds since it started
        public float Lifetime;
        public string Text;      // judgement label shown above the line
        public Color Color;
    }

    /// <summary>
    /// Projects chart notes onto the screen from the smoothed song clock, and turns the
    /// game's hit events into judgement animations.
    ///
    /// The overlay is a pure visualisation: ADOFAI keeps handling input and judgement, we
    /// only make the chart readable as falling notes and echo the hits back visually.
    /// </summary>
    internal sealed class FallingEngine
    {
        private readonly FallingView _view;
        private readonly ChartReader _chart;
        private readonly FallingConfig _cfg;

        private readonly List<NoteInstance> _visible = new List<NoteInstance>(128);
        private readonly List<HitEffect> _effects = new List<HitEffect>(16);
        private float _demoHitTimer;
        private float _demoHitInterval = 0.4f;
        private int _demoSeq;
        private string _lastState = "";

        /// <summary>
        /// How far back the display clock may be held.
        ///
        /// Clamped so the floor never runs ahead of the game's own clock: that way the first
        /// visible note starts at the top of the screen instead of popping in part-way down,
        /// and normal playback is never disturbed.
        /// </summary>
        private static double NowRoom(GameClock clock, double fallTime)
        {
            double room = -clock.SongPosition;
            if (room < 0.0)
                room = 0.0;
            return Math.Min(fallTime, room);
        }

        private float _judgeFlash;
        private bool _laneFlashActive;
        private float _laneFlash;
        private int _laneFlashLane = -1;
        private bool _hitFlashActive;
        private float _hitFlash;
        private int _hitFlashLane = -1;
        private float _nextDiag;

        /// <summary>Notes on screen right now; read by the renderer during OnGUI.</summary>
        public List<NoteInstance> Visible { get { return _visible; } }
        public List<HitEffect> Effects { get { return _effects; } }

        public float JudgeFlash { get { return _judgeFlash; } }
        public bool LaneFlashActive { get { return _laneFlashActive; } }
        public float LaneFlash { get { return _laneFlash; } }
        public int LaneFlashLane { get { return _laneFlashLane; } }
        public bool HitFlashActive { get { return _hitFlashActive; } }
        public float HitFlash { get { return _hitFlash; } }
        public int HitFlashLane { get { return _hitFlashLane; } }

        private Color _hitColour = new Color(1f, 0.6f, 0.3f, 1f);

        /// <summary>Colour of the most recent judgement, used by the lane/judge flashes.</summary>
        public Color HitFlashColor { get { return _hitColour; } }

        private GameClock _clock;
        private bool _runUpSet;

        /// <summary>
        /// Applied lead in seconds. Negative means the display clock runs behind the song, so
        /// notes cross the judgement line later.
        /// </summary>
        public double LeadShift { get { return _clock != null ? _clock.LeadOffset : 0.0; } }
        public string StateName { get { return _clock != null ? _clock.StateName : ""; } }
        public double RawTime { get { return _clock != null ? _clock.SongPosition : 0.0; } }
        public int HoldCount { get { return _chart.HoldCount; } }
        public float JudgeLineY { get { return _view.JudgeY; } }
        public int NoteCount { get { return _chart.Count; } }
        public int VisibleCount { get { return _visible.Count; } }
        public double FirstHitTime { get { return _chart.FirstHitTime; } }
        public float MeasuredDt { get { return _clock != null ? _clock.MeasuredDt : 0f; } }
        public double MeasuredStep { get { return _clock != null ? _clock.MeasuredStep : 0.0; } }

        public FallingEngine(FallingView view, ChartReader chart, FallingConfig cfg)
        {
            _view = view;
            _chart = chart;
            _cfg = cfg;
        }

        public void Tick(GameClock clock, float dt, double rawBeforeFrame)
        {
            _view.Layout(false);
            _clock = clock;
            DecayEffects(dt);

            // A rewind means the level restarted or resumed from a checkpoint. Everything the
            // previous attempt consumed has to come back, otherwise the section after the
            // checkpoint would have no notes left to show.
            if (clock.Playing && rawBeforeFrame - clock.SongPosition > 0.5)
            {
                FallingPlugin.Log.LogInfo(string.Format(
                    "playback rewound {0:F2}s (checkpoint / restart) - rebuilding notes",
                    rawBeforeFrame - clock.SongPosition));
                _chart.Rebuild();
                _runUpSet = false;
                ResetJudgeStats();
                _judgeSamples = 0;
                _judgeSumPx = 0f;
                _judgeAvgPx = 0f;
                _visible.Clear();
            }

            float refScale = Screen.height / 1080f;
            float pxPerSec = Mathf.Max(1f, _cfg.NoteSpeedPxPerSec.Value * refScale);
            double fallTime = _view.FallDistance / pxPerSec;
            double fadeOut = fallTime * 0.5;

            // Log every phase change: this is what makes countdown behaviour diagnosable
            // without a human having to describe what they saw.
            if (clock.StateName != _lastState)
            {
                _lastState = clock.StateName;
                FallingPlugin.Log.LogInfo(string.Format(
                    "phase -> {0} (raw={1:F3}s smoothed={2:F3}s lead={3:F3} playing={4} inlevel={5})",
                    clock.StateName, clock.SongPosition, clock.SongTime, clock.LeadRoom,
                    clock.Playing, clock.InLevelScene));

                // Where is the furthest-along note the moment this phase begins? A note that
                // starts at the top of the screen means the run-up worked.
                if (_chart.Count > 0)
                {
                    double t = clock.SongTime;
                    double firstGap = double.MaxValue;
                    List<NoteInstance> all = _chart.Notes;
                    for (int i = 0; i < all.Count; i++)
                    {
                        double gap = all[i].HitTime - t;
                        if (gap < firstGap)
                            firstGap = gap;
                    }
                    FallingPlugin.Log.LogInfo(string.Format(
                        "  first note is {0:F3}s away = {1:F0}px above the line (fall distance {2:F0}px, notes {3})",
                        firstGap, firstGap * pxPerSec, _view.FallDistance, _chart.Count));
                }
            }

            double now = clock.SongTime;

            if (!clock.Playing && !clock.InLevelScene)
            {
                _visible.Clear();
                _judgeFlash = 0f;
                _laneFlashActive = false;
                _hitFlashActive = false;
                clock.ClearLead();
                _runUpSet = false;
                return;
            }

            // Run-up: shift the display clock back so the chart's first note starts at the top
            // edge of the screen. Recomputed until the level is really running, because the
            // chart only becomes readable part-way through the countdown; after that the offset
            // is frozen so the timeline does not move under the player.
            // Run-up: shift the display clock back so the chart's first note starts at the top
            // edge of the screen. Applied once per play attempt, and NOT gated on the level
            // having started: the chart can become readable only after gameplay begins, and
            // that is exactly the case where the player otherwise gets no reaction time at all.
            if (_cfg.CountdownLead.Value && clock.LeadOffset == 0.0 && !_runUpSet)
            {
                double first = _chart.FirstHitTime;
                if (_chart.Count > 0 && !double.IsNaN(first))
                {
                    RecomputeLead(clock, first);
                    _runUpSet = true;
                    FallingPlugin.Log.LogInfo(string.Format(
                        "run-up: chart {0} notes, first {1:F3}s, fall {2:F3}s, fraction {3:F2}, trim {4:F3}s -> offset {5:F3}s",
                        _chart.Count, first, fallTime, _cfg.LeadFraction.Value,
                        _cfg.LeadOffsetManual.Value, clock.LeadTarget));
                }
            }

            now = clock.SongTime;

            // Apply the hits the game reported before culling, so a note that was just hit
            // disappears in this same frame instead of one frame later.
            while (clock.ConsumeHit())
                ApplyHit(clock);

            _visible.Clear();
            List<NoteInstance> notes = _chart.Notes;
            for (int i = 0; i < notes.Count; i++)
            {
                NoteInstance n = notes[i];
                if (n.Consumed)
                    continue;               // already finished: it disappears at the line

                double lead = n.HitTime - now;

                if (n.HoldSeconds > 0.0)
                {
                    // Default: the hold bar falls exactly like a normal note and is removed the
                    // moment it is pressed (see ApplyHit).
                    double end = n.HitTime + n.HoldSeconds;
                    if (lead > fallTime)
                        continue;           // not on screen yet

                    if (!_cfg.HoldShrinkWhileHeld.Value)
                    {
                        if (lead < -fadeOut)
                            continue;       // pressed and gone
                        n.DistancePx = (float)(lead * pxPerSec);
                        n.Active = true;
                        _visible.Add(n);
                        continue;
                    }

                    // Optional mode: keep it alive for the whole hold, measured to its far end so
                    // the bar always grows out of the judgement line.
                    if (now > end + 0.05)
                        continue;           // released: remove it
                    double goal = now > n.HitTime ? end : n.HitTime;
                    n.DistancePx = (float)((goal - now) * pxPerSec);
                    n.Active = true;
                    _visible.Add(n);
                    continue;
                }

                if (lead > fallTime)
                    continue;               // not on screen yet
                if (lead < -fadeOut)
                    continue;               // long past
                n.DistancePx = (float)(lead * pxPerSec);
                n.Active = true;
                _visible.Add(n);
            }

            // Fallback pulse for when the game's hit callback is unavailable.
            if (!clock.HitHookAttached)
            {
                for (int i = 0; i < _visible.Count; i++)
                {
                    if (Math.Abs(_visible[i].DistancePx) < pxPerSec * dt * 1.5f)
                    {
                        SpawnEffect(_visible[i].Lane, "HIT", _cfg.NoteColorValue);
                        break;
                    }
                }
            }

            // In DemoMode no real hit ever arrives, so synthesise one each time a note
            // crosses the line: that drives the same code path as a real hit, which makes
            // the animation and the sound testable without playing a level.
            if (clock.DemoDriven && _cfg.DemoHits.Value)
            {
                _demoHitTimer += dt;
                if (_demoHitTimer >= _demoHitInterval)
                {
                    _demoHitTimer = 0f;
                    int lane = FindLaneCrossingLine(pxPerSec);
                    int seq = NextDemoSeq(clock);
                    clock.RegisterSyntheticHit(seq, 0.0, "Perfect");
                    ApplyHit(clock);
                }
            }

            if (_cfg.DebugHud.Value && Time.unscaledTime > _nextDiag)
            {
                _nextDiag = Time.unscaledTime + 2f;
                string firstText = _chart.Count > 0 ? _chart.FirstHitTime.ToString("F3") : "n/a";
                FallingPlugin.Log.LogInfo(string.Format(
                    "[hud] state={0} t={1:F3}s raw={2:F3}s shift={3:F3} bpm={4:F1} seq={5} notes={6} first={7} shown={8} fx={9} hits={10} hook={11} chart={12}",
                    clock.StateName, now, clock.SongPosition, -clock.LeadOffset, clock.Bpm,
                    clock.SeqId, _chart.Count, firstText, _visible.Count, _effects.Count,
                    clock.HitCount, clock.HitHookAttached, _chart.Status));
            }
        }

        /// <summary>
        /// Turn one game hit into feedback: hide the note that was hit, flash its lane and
        /// the judgement line, burst at the line, show the grade and play the hit sound.
        /// </summary>
        private void ApplyHit(GameClock clock)
        {
            NoteInstance hitNote;
            int lane = ResolveHitLane(clock, out hitNote);
            SpawnEffect(lane, GradeLabel(clock.HitGrade), GradeColor(clock.HitGrade, _cfg));

            // A hold is judged twice in this game (press and release). By default the bar
            // disappears like a normal note the moment it is pressed; with
            // HoldShrinkWhileHeld the note stays and the bar shrinks for the whole hold.
            if (hitNote.HoldSeconds <= 0.0 || !_cfg.HoldShrinkWhileHeld.Value)
                _chart.MarkConsumed(hitNote);

            // Measure the timing error directly: where was the note when the game judged it?
            // Positive = the note had not reached the line yet (overlay lags the music);
            // negative = it had already passed the line (overlay leads the music).
            if (hitNote.HitTime > 0.0 || hitNote.SeqId >= 0)
            {
                float refScale = Screen.height / 1080f;
                float pxPerSec = Mathf.Max(1f, _cfg.NoteSpeedPxPerSec.Value * refScale);
                _lastJudgeOffsetPx = (float)((hitNote.HitTime - clock.SongTime) * pxPerSec);
                _judgeSamples++;
                _judgeSumPx += _lastJudgeOffsetPx;
                _judgeAvgPx = _judgeSumPx / _judgeSamples;

                if (_cfg.DebugHud.Value && _judgeSamples % 10 == 1)
                {
                    FallingPlugin.Log.LogInfo(string.Format(
                        "judge alignment: note {0:F0}px {1} the line (avg {2:F0}px over {3} hits)",
                        Math.Abs(_lastJudgeOffsetPx),
                        _lastJudgeOffsetPx >= 0f ? "ABOVE" : "BELOW",
                        _judgeAvgPx, _judgeSamples));
                }

                // Hold tiles: log the timing and the hold length that came from the chart, so a
                // mismatch shows up as numbers rather than "it looks wrong".
                if (hitNote.HoldSeconds > 0.0)
                {
                    double measured = _lastHoldHitTime > 0.0
                        ? clock.SongTime - _lastHoldHitTime : 0.0;
                    _lastHoldHitTime = clock.SongTime;
                    FallingPlugin.Log.LogInfo(string.Format(
                        "hold hit: seq={0} lane={1} entry={2:F3}s judged at {3:F3}s (err {4:F0}px) hold={5:F3}s ({6:F0}px) prev gap={7:F3}s",
                        hitNote.SeqId, hitNote.Lane, hitNote.HitTime, clock.SongTime,
                        _lastJudgeOffsetPx, hitNote.HoldSeconds,
                        hitNote.HoldSeconds * pxPerSec, measured));
                }
            }
        }

        private double _lastHoldHitTime;

        private float _lastJudgeOffsetPx;
        private float _judgeSumPx;
        private float _judgeAvgPx;
        private int _judgeSamples;

        /// <summary>Average px the note sat away from the line when judged (diagnostics).</summary>
        public float JudgeOffsetPx { get { return _lastJudgeOffsetPx; } }
        public float JudgeAvgPx { get { return _judgeAvgPx; } }
        public int JudgeSamples { get { return _judgeSamples; } }

        // --- runtime alignment tuning (F5 panel) ------------------------------

        private float _cachedFallTime;
        private double _cachedFirst;

        /// <summary>Re-apply the offset from the current config; used while tuning.</summary>
        public void RecomputeLead(GameClock clock, double firstHitTime)
        {
            _cachedFirst = firstHitTime;
            _cachedFallTime = (float)(_view.FallDistance /
                Mathf.Max(1f, _cfg.NoteSpeedPxPerSec.Value * (Screen.height / 1080f)));
            clock.SetLead(firstHitTime, _cachedFallTime, _cfg.LeadInSeconds.Value,
                _cfg.LeadFraction.Value, _cfg.LeadOffsetManual.Value);
        }

        /// <summary>Called by the plugin while the tuning panel is open.</summary>
        public void Retune(GameClock clock)
        {
            if (clock == null || _chart.Count == 0)
                return;
            double first = _chart.FirstHitTime;
            if (double.IsNaN(first))
                return;
            RecomputeLead(clock, first);
        }

        /// <summary>Reset the measured alignment error, so a new trim can be judged afresh.</summary>
        public void ResetJudgeStats()
        {
            _judgeSamples = 0;
            _judgeSumPx = 0f;
            _judgeAvgPx = 0f;
            _lastJudgeOffsetPx = 0f;
        }

        public double ManualTrim { get { return _cfg.LeadOffsetManual.Value; } }
        public double LeadFraction { get { return _cfg.LeadFraction.Value; } }
        public double HoldTrim { get { return _cfg.HoldOffsetSeconds.Value; } }
        public double HoldScale { get { return _cfg.HoldScale.Value; } }

        /// <summary>
        /// Lane of the tile just hit: from the chart note when the sequence id is known,
        /// otherwise derived from the tile's entry direction.
        /// </summary>
        private int ResolveHitLane(GameClock clock, out NoteInstance note)
        {
            note = default(NoteInstance);
            if (clock.HitSeqId >= 0)
            {
                NoteInstance found;
                if (_chart.TryGetBySeqId(clock.HitSeqId, out found))
                {
                    note = found;
                    return found.Lane;
                }
            }
            return ChartReader.LaneForSeq(clock.HitSeqId, clock.HitAngle);
        }

        /// <summary>Lane whose closest note is currently at the judgement line.</summary>
        private int FindLaneCrossingLine(float pxPerSec)
        {
            int bestLane = 0;
            double best = double.MaxValue;
            for (int i = 0; i < _visible.Count; i++)
            {
                double d = Math.Abs(_visible[i].DistancePx);
                if (d < best)
                {
                    best = d;
                    bestLane = _visible[i].Lane;
                }
            }
            return bestLane;
        }

        /// <summary>Sequence id of the next note about to cross the line (DemoMode).</summary>
        private int NextDemoSeq(GameClock clock)
        {
            List<NoteInstance> notes = _chart.Notes;
            int bestSeq = -1;
            double bestLead = double.MaxValue;
            for (int i = 0; i < notes.Count; i++)
            {
                NoteInstance n = notes[i];
                if (n.Consumed)
                    continue;
                double lead = n.HitTime - clock.SongTime;
                if (lead < -0.3)
                    continue;
                if (lead < bestLead)
                {
                    bestLead = lead;
                    bestSeq = n.SeqId;
                }
            }
            if (bestSeq < 0)
                bestSeq = _demoSeq++;
            return bestSeq;
        }

        private static string GradeLabel(string grade)
        {
            if (string.IsNullOrEmpty(grade))
                return "HIT";
            switch (grade)
            {
                case "Perfect":
                case "EarlyPerfect":
                case "LatePerfect":
                    return "PERFECT";
                case "VeryEarly":
                case "TooEarly":
                    return "EARLY";
                case "VeryLate":
                case "TooLate":
                    return "LATE";
                case "Multipress":
                case "OverPress":
                    return "MULTI";
                case "Auto":
                    return "AUTO";
                default:
                    return grade.ToUpperInvariant();
            }
        }

        private static Color GradeColor(string grade, FallingConfig cfg)
        {
            if (string.IsNullOrEmpty(grade))
                return cfg.NoteColorValue;
            if (grade == "Perfect" || grade == "EarlyPerfect" || grade == "LatePerfect")
                return new Color(1f, 0.95f, 0.45f, 1f);
            if (grade == "Auto")
                return new Color(0.6f, 0.8f, 1f, 1f);
            return new Color(1f, 0.55f, 0.35f, 1f);
        }

        private void SpawnEffect(int lane, string text, Color colour)
        {
            _hitColour = colour;

            HitEffect e;
            e.Lane = lane;
            e.Age = 0f;
            e.Lifetime = 0.55f;
            e.Text = text;
            e.Color = colour;
            _effects.Add(e);

            _laneFlashActive = true;
            _laneFlashLane = lane;
            _laneFlash = 1f;

            _hitFlashActive = true;
            _hitFlashLane = lane;
            _hitFlash = 1f;

            _judgeFlash = 1f;
        }

        private void DecayEffects(float dt)
        {
            if (_judgeFlash > 0f)
                _judgeFlash = Mathf.Max(0f, _judgeFlash - dt * 2.2f);

            if (_laneFlash > 0f)
            {
                _laneFlash = Mathf.Max(0f, _laneFlash - dt * 2.4f);
                if (_laneFlash <= 0f)
                    _laneFlashActive = false;
            }

            if (_hitFlash > 0f)
            {
                _hitFlash = Mathf.Max(0f, _hitFlash - dt * 3.6f);
                if (_hitFlash <= 0f)
                    _hitFlashActive = false;
            }

            for (int i = _effects.Count - 1; i >= 0; i--)
            {
                HitEffect e = _effects[i];
                e.Age += dt;
                if (e.Age >= e.Lifetime)
                    _effects.RemoveAt(i);
                else
                    _effects[i] = e;
            }
        }
    }
}
