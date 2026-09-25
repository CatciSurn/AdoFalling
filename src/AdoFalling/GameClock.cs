using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace AdoFalling
{
    /// <summary>
    /// Tracks the song clock and the game's own hit events.
    ///
    /// The clock is polled from the game at a low rate (a few reflection reads) but is
    /// advanced every frame with the frame delta, so notes move smoothly instead of
    /// stepping at the polling rate. A large disagreement between the polled and the
    /// extrapolated time (level restart, scene change) snaps instead of interpolating.
    ///
    /// Hit events come from the game itself: scrPlayer.onHit is a public
    /// Action&lt;scrFloor&gt; that the game invokes on every successful tile, so the
    /// overlay's judgement animation is driven by the real judgement rather than a guess.
    /// </summary>
    internal sealed class GameClock
    {
        public bool Playing;
        public bool InLevelScene;
        public double SongPosition;  // last value polled straight from the game
        public double SongTime;      // displayed song position (game clock + LeadOffset)
        public double Bpm;
        public double Crotchet;
        public int SeqId = -1;
        public string StateName = "";
        public string Diagnostics = "";

        /// <summary>Set when DemoMode drives the clock, so real polling is ignored.</summary>
        public bool DemoDriven;

        /// <summary>
        /// Display-clock offset in seconds, applied so the chart's first note starts at the top
        /// of the screen.
        ///
        /// Offsets are used instead of clamping the clock: the whole timeline is shifted by a
        /// constant, so a long countdown can never make notes run past the judgement line, and
        /// it does not matter how late the chart becomes readable.
        /// </summary>
        public double LeadOffset;

        /// <summary>Room available before the song starts (never positive).</summary>
        public double LeadRoom
        {
            get { return LeadOffset < 0.0 ? -LeadOffset : 0.0; }
        }

        /// <summary>True once the offset has been fixed for the current play attempt.</summary>
        public bool LeadFrozen;

        /// <summary>Frame time and displayed-time step, for on-screen diagnostics.</summary>
        public float MeasuredDt;
        public double MeasuredStep;

        /// <summary>Increments on every hit the game reports.</summary>
        public int HitCount { get; private set; }

        private const double SnapThreshold = 0.25;   // seconds of disagreement => hard sync
        private const double SmoothRate = 2.5;       // how fast to close a small gap
        private const double FrameAdvance = 0.25;    // fraction of the remaining gap closed per frame

        private GameState _polled;
        private int _seenHit;

        // --- polling ----------------------------------------------------------

        /// <summary>Poll the game state. Returns true when a level is being played.</summary>
        public bool Poll()
        {
            GameState st = GameState.Capture();
            _polled = st;
            Playing = st.Playing;
            InLevelScene = st.InLevelScene;
            SongPosition = st.SongPosition;

            // A fresh play attempt re-arms the run-up.
            if (!st.InLevelScene)
                LeadFrozen = false;
            Bpm = st.Bpm;
            Crotchet = st.Crotchet;
            SeqId = st.SeqId;
            StateName = st.StateName;
            Diagnostics = st.Diagnostics;

            if (Playing)
            {
                ProbeHitHook();
                BindHitHook();
            }
            return Playing;
        }

        /// <summary>Force the displayed song position (used by DemoMode).</summary>
        public void ForceTime(double seconds)
        {
            SongTime = seconds;
        }

        /// <summary>
        /// Advance the displayed clock by one frame.
        ///
        /// The song clock is read from the game every frame and the display is simply that value
        /// plus the offset. Sampling it only at PollInterval and extrapolating in between always
        /// drifts, because the extrapolated speed never matches the song exactly; reading it per
        /// frame makes the displayed time exact by construction, so notes stay locked to the
        /// music. The game's own value is driven by the audio DSP clock, so it advances per frame
        /// and there is nothing to interpolate.
        /// </summary>
        public double AdvanceFrame()
        {
            if (DemoDriven)
            {
                SongTime += Time.unscaledDeltaTime;   // DemoMode is a free-running clock
                return SongTime;
            }

            double raw;
            bool playing, inLevel;
            if (!GameState.CaptureTime(out raw, out playing, out inLevel))
            {
                SongTime = raw + LeadOffset;
                return SongTime;
            }

            Playing = playing;
            InLevelScene = inLevel;
            SongPosition = raw;

            SongTime = raw + LeadOffset;
            return SongTime;
        }

        /// <summary>
        /// Set the display offset. <paramref name="fraction"/> scales how much of the fall time
        /// is used as lead (1 = a full fall), and <paramref name="trimSeconds"/> is the manual
        /// fine trim from the F5 panel.
        /// </summary>
        public void SetLead(double firstHitTime, double fallTime, double leadIn,
            double fraction, double trimSeconds)
        {
            double lead = (fallTime + leadIn) * fraction;
            double offset = firstHitTime - lead + trimSeconds;
            if (offset > 0.0)
                offset = 0.0;          // never push the chart forward of the song
            if (offset < -12.0)
                offset = -12.0;        // sanity bound for absurd fall times
            LeadOffset = offset;
            LeadTarget = offset;
        }

        /// <summary>Applied offset, for diagnostics.</summary>
        public double LeadTarget;

        /// <summary>Disarm the offset so the display clock matches the game exactly.</summary>
        public void ClearLead()
        {
            LeadOffset = 0.0;
        }

        // --- hit hook ---------------------------------------------------------

        private static bool _probeDone;
        private static bool _probeFailed;
        private static FieldInfo _onHitField;
        private static string _handlerName;
        private object _boundPlayer;

        private static void ProbeHitHook()
        {
            if (_probeDone)
                return;
            _probeDone = true;
            try
            {
                Assembly asm = GameAssembly();
                if (asm == null)
                {
                    _probeFailed = true;
                    return;
                }

                Type playerType = GameState.FindType(asm, "scrPlayer");
                if (playerType == null)
                {
                    _probeFailed = true;
                    return;
                }

                const BindingFlags F = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
                foreach (FieldInfo f in playerType.GetFields(F))
                {
                    if (f.Name != "onHit" || !typeof(Delegate).IsAssignableFrom(f.FieldType))
                        continue;

                    ParameterInfo[] ps = f.FieldType.GetMethod("Invoke").GetParameters();
                    if (ps.Length == 1 && ps[0].ParameterType.Name == "scrFloor")
                    {
                        _onHitField = f;
                        _handlerName = "HandleHitFloor";
                    }
                    else if (ps.Length == 2 && ps[0].ParameterType.Name == "Boolean")
                    {
                        _onHitField = f;
                        _handlerName = "HandleHitBool";
                    }
                    break;
                }

                if (_onHitField == null)
                {
                    _probeFailed = true;
                    FallingPlugin.Log.LogWarning("scrPlayer.onHit not found; judgement animation off");
                }
            }
            catch (Exception ex)
            {
                _probeFailed = true;
                FallingPlugin.Log.LogWarning("hit hook probe failed: " + ex.Message);
            }
        }

        private void BindHitHook()
        {
            if (_probeFailed || _onHitField == null)
                return;
            try
            {
                object player = FindPlayer(_onHitField.DeclaringType);
                if (player == null || ReferenceEquals(player, _boundPlayer))
                    return;

                MethodInfo handler = typeof(GameClock).GetMethod(_handlerName,
                    BindingFlags.NonPublic | BindingFlags.Instance);
                Delegate d = Delegate.CreateDelegate(_onHitField.FieldType, this, handler);
                object existing = _onHitField.GetValue(player);
                _onHitField.SetValue(player, Delegate.Combine(existing as Delegate, d));
                _boundPlayer = player;
                FallingPlugin.Log.LogInfo("hit hook attached to scrPlayer.onHit");
            }
            catch (Exception ex)
            {
                _probeFailed = true;
                FallingPlugin.Log.LogWarning("hit hook bind failed: " + ex.Message);
            }
        }

        /// <summary>
        /// Resolve the active scrPlayer through scrPlayerManager.players[0], with
        /// scrController.playerOne as a fallback.
        /// </summary>
        private static object FindPlayer(Type playerType)
        {
            try
            {
                Assembly asm = GameAssembly();
                if (asm == null)
                    return null;

                Type mgr = GameState.FindType(asm, "scrPlayerManager");
                if (mgr != null)
                {
                    object inst = ReadStatic(mgr, "playerManager") ?? ReadStatic(mgr, "instance");
                    object players = GameState.Read(inst, GameState.FindMember(mgr, "players"));
                    var list = players as System.Collections.IList;
                    if (list != null && list.Count > 0)
                        return list[0];
                }

                Type ctrl = GameState.FindType(asm, "scrController");
                if (ctrl != null)
                {
                    object c = ReadStatic(ctrl, "instance");
                    object p = GameState.Read(c, GameState.FindMember(ctrl, "playerOne"));
                    if (p != null && playerType.IsInstanceOfType(p))
                        return p;
                }
            }
            catch
            {
                // fall through
            }
            return null;
        }

        private static object ReadStatic(Type t, string name)
        {
            const BindingFlags F = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
            PropertyInfo p = t.GetProperty(name, F);
            if (p != null)
                return p.GetValue(null, null);
            FieldInfo f = t.GetField(name, F);
            return f != null ? f.GetValue(null) : null;
        }

        private void HandleHitFloor(object floor)
        {
            int seq = -1;
            double angle = double.NaN;
            string grade = null;
            try
            {
                Assembly asm = GameAssembly();
                if (asm != null && floor != null)
                {
                    Type ft = GameState.FindType(asm, "scrFloor");
                    if (ft != null)
                    {
                        object seqObj = GameState.Read(floor, GameState.FindMember(ft, "seqID"));
                        object angObj = GameState.Read(floor, GameState.FindMember(ft, "entryangle", "entryAngle"));
                        object gradeObj = GameState.Read(floor, GameState.FindMember(ft, "grade"));
                        if (seqObj != null)
                            seq = Convert.ToInt32(seqObj);
                        if (angObj != null)
                            angle = Convert.ToDouble(angObj);
                        if (gradeObj != null)
                            grade = gradeObj.ToString();
                    }
                }
            }
            catch
            {
                // The animation only needs a lane index, so a failed read is survivable.
            }

            HitCount++;
            LastHitSeqId = seq;
            LastHitAngle = angle;
            LastHitGrade = grade;
            LastHitValid = true;
        }

        private void HandleHitBool(bool isAuto)
        {
            HitCount++;
            LastHitSeqId = -1;
            LastHitAngle = double.NaN;
            LastHitGrade = null;
            LastHitValid = true;
        }

        /// <summary>
        /// Report a hit that did not come from the game - used by DemoMode so the judgement
        /// animation can be previewed without playing a level.
        /// </summary>
        public void RegisterSyntheticHit(int seqId, double angle, string grade)
        {
            HitCount++;
            LastHitSeqId = seqId;
            LastHitAngle = angle;
            LastHitGrade = grade;
            LastHitValid = true;
        }

        /// <summary>Seq id of the tile hit most recently, or -1 when it could not be read.</summary>
        public int LastHitSeqId { get; private set; }
        public double LastHitAngle { get; private set; }
        public string LastHitGrade { get; private set; }
        public bool LastHitValid { get; private set; }

        private static Assembly GameAssembly()
        {
            foreach (Assembly a in AppDomain.CurrentDomain.GetAssemblies())
                if (a.GetName().Name == "Assembly-CSharp")
                    return a;
            return null;
        }

        /// <summary>True when the game's own hit callback is wired up.</summary>
        public bool HitHookAttached { get { return !_probeFailed && _onHitField != null && _boundPlayer != null; } }

        /// <summary>
        /// Pending hit detail. Read it inside a <see cref="ConsumeHit"/> loop; the values
        /// describe the hit that ConsumeHit just reported.
        /// </summary>
        public int HitSeqId { get { return LastHitSeqId; } }
        public double HitAngle { get { return LastHitAngle; } }
        public string HitGrade { get { return LastHitGrade; } }

        /// <summary>True exactly once for each newly observed hit.</summary>
        public bool ConsumeHit()
        {
            int cur = HitCount;
            if (cur != _seenHit)
            {
                _seenHit = cur;
                return true;
            }
            return false;
        }
    }
}
