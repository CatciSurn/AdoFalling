using System;
using System.Collections.Generic;
using System.Reflection;

namespace AdoFalling
{
    /// <summary>
    /// Read-only adapter over the game's own singletons, using the members verified in
    /// the decompiled assembly:
    ///
    ///   scrConductor.instance           : static scrConductor
    ///   scrConductor.songposition_minusv: double  - song clock in seconds, visual-corrected
    ///   scrConductor.bpm                : float
    ///   scrConductor.crotchetAtStart    : double  - seconds per beat at chart start
    ///   scrController.instance          : static scrController
    ///   scrController.currentState      : States (States.PlayerControl == playing)
    ///   scrController.currentSeqID      : int
    ///   scrController.currentFloorID    : int
    ///   scrController.paused            : bool
    ///
    /// Reflection is cached so the per-frame cost is a handful of field reads.
    /// </summary>
    internal sealed class GameState
    {
        public bool Playing;
        /// <summary>True once a level scene is being played, including its countdown.</summary>
        public bool InLevelScene;
        public double SongPosition;     // seconds on the game's song clock
        public double Bpm;
        public double Crotchet;
        public int SeqId = -1;
        public int FloorId = -1;
        public bool Paused;
        public string StateName = "";
        public string Diagnostics = "";

        private static bool _probed;
        private static bool _failed;
        private static bool _lmProbed;
        private static MemberInfo _lmInstance;
        private static MemberInfo _lmFloors;

        private static MemberInfo _conductorInstance;
        private static MemberInfo _songPosition;
        private static MemberInfo _bpm;
        private static MemberInfo _crotchet;

        private static MemberInfo _controllerInstance;
        private static MemberInfo _currentState;
        private static MemberInfo _paused;
        private static MemberInfo _currentSeqId;
        private static MemberInfo _currentFloorId;

        public static GameState Capture()
        {
            var st = new GameState();
            if (!_probed)
                Probe();
            if (_failed)
                return st;

            try
            {
                object cond = Resolve(_conductorInstance);
                if (cond != null)
                {
                    st.SongPosition = ToDouble(Read(cond, _songPosition));
                    st.Bpm = ToDouble(Read(cond, _bpm));
                    st.Crotchet = ToDouble(Read(cond, _crotchet));
                    if (st.Crotchet <= 0.0001 && st.Bpm > 0.0001)
                        st.Crotchet = 60.0 / st.Bpm;
                }

                object ctrl = Resolve(_controllerInstance);
                if (ctrl != null)
                {
                    object stateObj = Read(ctrl, _currentState);
                    st.StateName = stateObj != null ? stateObj.ToString() : "";
                    st.Paused = ToBool(Read(ctrl, _paused));
                    st.SeqId = (int)ToDouble(Read(ctrl, _currentSeqId));
                    st.FloorId = (int)ToDouble(Read(ctrl, _currentFloorId));
                    st.Playing = st.StateName == "PlayerControl" && !st.Paused;

                    // The chart is built while the level scene loads, well before the player
                    // gains control, so read it as soon as the level maker exists rather than
                    // waiting for gameplay. That gives the run-up offset time to settle before
                    // the countdown reaches the first note.
                    st.InLevelScene = st.Playing
                                   || st.StateName == "Start"
                                   || st.StateName == "Countdown"
                                   || st.StateName == "Checkpoint"
                                   || LevelMakerExists();
                }
            }
            catch (Exception ex)
            {
                st.Diagnostics = "error: " + ex.Message;
            }
            return st;
        }

        /// <summary>
        /// True when the level object exists and already holds tiles. The level is built while
        /// the scene loads, so this becomes true before gameplay starts - which is exactly when
        /// the overlay should start reading the chart.
        /// </summary>
        private static bool LevelMakerExists()
        {
            try
            {
                if (!_lmProbed)
                {
                    _lmProbed = true;
                    Assembly asm = GameAssembly();
                    Type lm = asm != null ? FindType(asm, "scrLevelMaker", "LevelMaker") : null;
                    if (lm != null)
                    {
                        _lmInstance = FindMember(lm, "instance");
                        _lmFloors = FindMember(lm, "listFloors", "floors");
                    }
                }

                object maker = Read(null, _lmInstance);
                UnityEngine.Object unityObj = maker as UnityEngine.Object;
                if (unityObj != null && unityObj == null)
                    return false;                       // destroyed between scenes
                if (maker == null)
                    return false;

                var list = Read(maker, _lmFloors) as System.Collections.ICollection;
                return list != null && list.Count > 0;
            }
            catch
            {
                return false;
            }
        }

        private static Assembly _gameAssembly;

        /// <summary>Cached lookup of Assembly-CSharp; this runs every frame.</summary>
        private static Assembly GameAssembly()
        {
            if (_gameAssembly != null)
                return _gameAssembly;
            foreach (Assembly a in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (a.GetName().Name == "Assembly-CSharp")
                {
                    _gameAssembly = a;
                    return a;
                }
            }
            return null;
        }

        /// <summary>
        /// Read only the song clock and the play flags.
        ///
        /// This is cheap enough to run every frame, which matters: deriving note positions from
        /// an extrapolation of a 20Hz sample always drifts, no matter how the rate is filtered.
        /// Reading the game's own clock each frame makes the displayed time exact by
        /// construction, so the overlay cannot slide out of sync with the music.
        /// </summary>
        public static bool CaptureTime(out double songPosition, out bool playing, out bool inLevel)
        {
            songPosition = 0.0;
            playing = false;
            inLevel = false;
            if (!_probed)
                Probe();
            if (_failed)
                return false;

            try
            {
                object cond = Resolve(_conductorInstance);
                if (cond == null)
                    return false;
                songPosition = ToDouble(Read(cond, _songPosition));

                object stateObj = Read(Resolve(_controllerInstance), _currentState);
                string stateName = stateObj != null ? stateObj.ToString() : "";
                playing = stateName == "PlayerControl" && !ToBool(Read(Resolve(_controllerInstance), _paused));
                inLevel = playing || stateName == "Start" || stateName == "Countdown"
                       || stateName == "Checkpoint" || LevelMakerExists();
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static void Probe()        {
            _probed = true;
            Assembly asm = null;
            foreach (Assembly a in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (a.GetName().Name == "Assembly-CSharp")
                {
                    asm = a;
                    break;
                }
            }
            if (asm == null)
            {
                _failed = true;
                FallingPlugin.Log.LogError("Assembly-CSharp is not loaded");
                return;
            }

            Type condType = FindType(asm, "scrConductor");
            Type ctrlType = FindType(asm, "scrController");

            if (condType != null)
            {
                _conductorInstance = FindStatic(condType, "instance");
                _songPosition = FindMember(condType, "songposition_minusi", "songposition_minusv", "songposition");
                _bpm = FindMember(condType, "bpm");
                _crotchet = FindMember(condType, "crotchetAtStart");
            }

            if (ctrlType != null)
            {
                _controllerInstance = FindStatic(ctrlType, "instance");
                _currentState = FindMember(ctrlType, "currentState");
                _paused = FindMember(ctrlType, "paused");
                _currentSeqId = FindMember(ctrlType, "currentSeqID");
                _currentFloorId = FindMember(ctrlType, "currentFloorID");
            }

            FallingPlugin.Log.LogInfo(string.Format(
                "GameState: conductor={0} songPos={1} bpm={2} controller={3} state={4} seqID={5}",
                condType != null ? condType.Name : "?",
                Name(_songPosition), Name(_bpm),
                ctrlType != null ? ctrlType.Name : "?",
                Name(_currentState), Name(_currentSeqId)));

            if (condType == null || ctrlType == null)
                _failed = true;
        }

        private static string Name(MemberInfo m)
        {
            return m == null ? "MISSING" : m.Name;
        }

        internal static Type FindType(Assembly asm, params string[] names)
        {
            foreach (Type t in asm.GetTypes())
                foreach (string n in names)
                    if (t.Name == n)
                        return t;
            return null;
        }

        internal static MemberInfo FindMember(Type t, params string[] names)
        {
            const BindingFlags F = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
            foreach (string n in names)
            {
                PropertyInfo p = t.GetProperty(n, F);
                if (p != null && p.CanRead)
                    return p;
                FieldInfo f = t.GetField(n, F);
                if (f != null)
                    return f;
            }
            return null;
        }

        private static MemberInfo FindStatic(Type t, string name)
        {
            const BindingFlags F = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
            PropertyInfo p = t.GetProperty(name, F);
            if (p != null)
                return p;
            return t.GetField(name, F);
        }

        internal static object Read(object target, MemberInfo m)
        {
            if (target == null || m == null)
                return null;
            PropertyInfo p = m as PropertyInfo;
            if (p != null && p.CanRead)
                return p.GetValue(target, null);
            FieldInfo f = m as FieldInfo;
            return f != null ? f.GetValue(target) : null;
        }

        private static object Resolve(MemberInfo m)
        {
            if (m == null)
                return null;
            try
            {
                PropertyInfo p = m as PropertyInfo;
                if (p != null)
                    return p.GetValue(null, null);
                FieldInfo f = m as FieldInfo;
                return f != null ? f.GetValue(null) : null;
            }
            catch
            {
                return null;
            }
        }

        private static double ToDouble(object o)
        {
            if (o == null)
                return 0.0;
            if (o is double) return (double)o;
            if (o is float) return (float)o;
            if (o is int) return (int)o;
            if (o is long) return (long)o;
            try { return Convert.ToDouble(o); }
            catch { return 0.0; }
        }

        private static bool ToBool(object o)
        {
            if (o == null)
                return false;
            if (o is bool)
                return (bool)o;
            try { return Convert.ToBoolean(o); }
            catch { return false; }
        }
    }
}
