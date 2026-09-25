using System;
using System.Reflection;
using UnityEngine;

namespace AdoFalling
{
    /// <summary>
    /// Drives the game's own auto-play flag (RDC.auto) so a level plays itself.
    ///
    /// This exists so the overlay's countdown timing can be checked without a human playing,
    /// and so a user can watch the falling overlay on a chart they cannot clear. It is the
    /// same switch the game's built-in auto-play button uses.
    /// </summary>
    internal sealed class AutoPlayDriver
    {
        private static bool _probed;
        private static MemberInfo _auto;
        private bool _applied;
        private bool _failed;

        public string Status = "off";

        /// <summary>Turn auto-play on or off to match the config.</summary>
        public void Apply(bool enable)
        {
            if (!enable)
            {
                if (_applied)
                {
                    Set(false);
                    _applied = false;
                    Status = "off";
                }
                return;
            }

            if (_failed)
                return;

            if (!_probed)
                Probe();
            if (_failed)
                return;

            // Re-applied on every poll, deliberately: the game's RDC state is rebuilt when it
            // switches scenes, so setting it once at the title screen has no effect in a level.
            Set(true);
            _applied = true;
            Status = "on (RDC.auto)";
        }

        private static void Probe()
        {
            _probed = true;
            try
            {
                Assembly asm = null;
                foreach (Assembly a in AppDomain.CurrentDomain.GetAssemblies())
                    if (a.GetName().Name == "Assembly-CSharp") { asm = a; break; }
                Type t = asm != null ? GameState.FindType(asm, "RDC") : null;
                if (t == null)
                {
                    return;
                }
                _auto = GameState.FindMember(t, "auto");
            }
            catch
            {
                _auto = null;
            }
        }

        private static void Set(bool value)
        {
            if (_auto == null)
                return;
            try
            {
                PropertyInfo p = _auto as PropertyInfo;
                if (p != null && p.CanWrite)
                {
                    p.SetValue(null, value, null);
                    return;
                }
                FieldInfo f = _auto as FieldInfo;
                if (f != null)
                    f.SetValue(null, value);
            }
            catch (Exception ex)
            {
                FallingPlugin.Log.LogWarning("auto-play toggle failed: " + ex.Message);
            }
        }

        public bool Available { get { return _auto != null; } }
    }
}
