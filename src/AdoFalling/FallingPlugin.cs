using System;
using System.Reflection;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace AdoFalling
{
    /// <summary>
    /// Draws a falling-note ("下落式") rhythm overlay on top of the stock ADOFAI chart.
    ///
    /// Only the judgement line, the lanes and the falling notes are drawn; everything else
    /// stays transparent, so the original chart (planets, tiles, path) remains visible and
    /// playable underneath. The overlay is a pure visualisation: ADOFAI keeps handling
    /// input and judgement.
    /// </summary>
    [BepInPlugin(PluginInfo.Guid, PluginInfo.Name, PluginInfo.Version)]
    public class FallingPlugin : BaseUnityPlugin
    {
        internal static ManualLogSource Log;
        internal static FallingPlugin Instance;
        internal static FallingConfig Cfg;

        private FallingView _view;
        private ChartReader _chart;
        private FallingEngine _engine;
        private OverlayRenderer _renderer;
        private GameClock _clock;
        private readonly AutoPlayDriver _autoPlay = new AutoPlayDriver();
        private readonly ControlPanel _panel = new ControlPanel();
        private float _nextPoll;
        private float _nextWarn;
        private double _demoTime;
        private double _prevNow;
        private double _rawBeforeFrame;
        private string _lastAutoStatus = "";

        private void Awake()
        {
            Instance = this;
            Log = Logger;
            Cfg = new FallingConfig(Config);
            Log.LogInfo(PluginInfo.Name + " " + PluginInfo.Version + " loading");
            Log.LogInfo(string.Format(
                "config: DebugHud={0} DemoMode={1} AutoPlay={2} CountdownLead={3} NoteSpeed={4} JudgeLineY={5} LaneMapping={6}",
                Cfg.DebugHud.Value, Cfg.DemoMode.Value, Cfg.AutoPlay.Value, Cfg.CountdownLead.Value,
                Cfg.NoteSpeedPxPerSec.Value, Cfg.JudgeLineY.Value, Cfg.LaneMapping.Value));

            _view = new FallingView();
            _chart = new ChartReader();
            _clock = new GameClock();
            _engine = new FallingEngine(_view, _chart, Cfg);
            _renderer = new OverlayRenderer();
            _view.Build();

            var harmony = new Harmony(PluginInfo.Guid);
            try
            {
                harmony.PatchAll(Assembly.GetExecutingAssembly());
                Log.LogInfo("harmony patches applied");
            }
            catch (Exception ex)
            {
                Log.LogWarning("harmony PatchAll: " + ex.Message);
            }
        }

        private void Update()
        {
            try
            {
                // Hotkey is polled every frame so it stays responsive.
                if (Input.GetKeyDown(Cfg.ToggleKey.Value))
                    _view.SetVisible(!_view.Visible);

                if (Input.GetKeyDown(Cfg.LoadKey.Value))
                    LoadChartFile();

                HandleTuningKeys();

                // Polling the game costs a few reflection reads, so it runs at a modest rate.
                if (Time.unscaledTime >= _nextPoll)
                {
                    _nextPoll = Time.unscaledTime + Cfg.PollInterval.Value;
                    bool playing = _clock.Poll();
                    _clock.DemoDriven = false;

                    // Leaving the level ends a file-chart run; press the load key again next time.
                    if (!_clock.InLevelScene && MacroPlan.Active)
                    {
                        MacroPlan.Active = false;
                        Log.LogInfo("file chart: left the level - press the load key to start again");
                    }
                    if (Cfg.DemoMode.Value && (!playing || Cfg.ForceDemo.Value))
                    {
                        _clock.DemoDriven = true;
                        StartDemoTick();
                    }
                    else
                    {
                        _demoTime = 0.0;
                    }

                    // Optional: let the game play itself (RDC.auto), which is also how the
                    // countdown timing can be observed without a human playing.
                    _autoPlay.Apply(Cfg.AutoPlay.Value && !_clock.DemoDriven);
                    if (Cfg.AutoPlay.Value && _autoPlay.Status != _lastAutoStatus)
                    {
                        _lastAutoStatus = _autoPlay.Status;
                        Log.LogInfo("auto-play: " + _autoPlay.Status
                            + (_autoPlay.Available ? "" : " (RDC.auto not found)"));
                    }
                }

                // ...but the overlay itself is advanced and drawn every single frame, which
                // is what keeps the fall smooth instead of stepping at the polling rate.
                _rawBeforeFrame = _clock.SongPosition;
                double now = _clock.AdvanceFrame();
                _clock.MeasuredDt = Time.unscaledDeltaTime;
                _clock.MeasuredStep = now - _prevNow;
                _prevNow = now;
                _chart.Refresh(_clock);
                _engine.Tick(_clock, Time.unscaledDeltaTime, _rawBeforeFrame);
            }
            catch (Exception ex)
            {
                if (Time.unscaledTime > _nextWarn)
                {
                    _nextWarn = Time.unscaledTime + 5f;
                    Log.LogWarning("update: " + ex);
                }
            }
        }

        /// <summary>
        /// Load / refresh the .adofai chart file in Chart/FilePath and start file mode: the
        /// overlay's chart and 排键 come from the file, the first press of the macro plan is
        /// due at the moment this key is pressed (press it on the first tile, like the macro).
        /// </summary>
        private void LoadChartFile()
        {
            string path = Cfg.FilePath.Value;
            if (string.IsNullOrEmpty(path))
            {
                if (MacroPlan.Active)
                {
                    MacroPlan.Active = false;
                    _chart.Rebuild();
                    Log.LogInfo("file chart mode off (Chart/FilePath is empty)");
                }
                else
                {
                    Log.LogWarning("load key pressed, but Chart/FilePath is empty");
                }
                return;
            }

            string message;
            MacroPlan plan = MacroPlan.TryLoad(path, out message);
            Log.LogInfo("chart file: " + message);
            if (plan == null)
                return;

            MacroPlan.Active = true;
            MacroPlan.AnchorSongTime = _clock.SongPosition;
            Cfg.LaneMapping.Value = LaneMapping.Macro;
            _view.SetVisible(true);   // starting the reader also shows the overlay
            _chart.Rebuild();         // drop the old notes so the file chart is built fresh
            Log.LogInfo(string.Format(
                "file chart started at {0:F3}s ({1} notes) - press {2} again to restart",
                MacroPlan.AnchorSongTime, plan.PressCount, Cfg.LoadKey.Value));
        }

        /// <summary>
        /// In-game alignment tuning. Changes apply immediately so the effect is visible on the
        /// next note, and the values are written back to the config file when the panel closes.
        /// </summary>
        private void HandleTuningKeys()
        {
            if (Input.GetKeyDown(Cfg.ControlPanelKey.Value))
            {
                _panel.Visible = !_panel.Visible;
                if (_panel.Visible)
                {
                    _panel.SyncFromConfig(Cfg);
                    Log.LogInfo("control panel opened");
                }
                else
                {
                    Config.Save();
                    Log.LogInfo("control panel closed - settings saved");
                }
            }

            float step = (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift))
                ? 0.05f : 0.01f;

            // Hold trim works whether or not the panel is open, so it can be dialled in while
            // actually playing a chart with holds.
            bool holdChanged = false;
            if (Input.GetKeyDown(Cfg.HoldLaterKey.Value))
            {
                Cfg.HoldOffsetSeconds.Value = Cfg.HoldOffsetSeconds.Value - step;
                holdChanged = true;
            }
            if (Input.GetKeyDown(Cfg.HoldEarlierKey.Value))
            {
                Cfg.HoldOffsetSeconds.Value = Cfg.HoldOffsetSeconds.Value + step;
                holdChanged = true;
            }
            if (Input.GetKeyDown(KeyCode.Backspace))
            {
                Cfg.HoldOffsetSeconds.Value = 0.0;
                holdChanged = true;
            }
            if (Input.GetKeyDown(KeyCode.LeftBracket))
            {
                Cfg.HoldScale.Value = System.Math.Max(0.2, Cfg.HoldScale.Value - 0.05);
                holdChanged = true;
            }
            if (Input.GetKeyDown(KeyCode.RightBracket))
            {
                Cfg.HoldScale.Value = System.Math.Min(3.0, Cfg.HoldScale.Value + 0.05);
                holdChanged = true;
            }
            // Snap the trims to a sane precision before saving: repeated +/- steps leave float
            // dust behind (4.4E-16 instead of 0) which then shows up in the config file.
            Cfg.HoldOffsetSeconds.Value = System.Math.Round(Cfg.HoldOffsetSeconds.Value, 3);
            Cfg.LeadOffsetManual.Value = System.Math.Round(Cfg.LeadOffsetManual.Value, 3);

            if (holdChanged)
            {
                Config.Save();
                Log.LogInfo(string.Format(
                    "hold trim: offset={0:F3}s scale={1:F2}  ('-' later, '=' earlier, Backspace reset, [ ] scale)",
                    Cfg.HoldOffsetSeconds.Value, Cfg.HoldScale.Value));
            }

            // Note timing keys still work outside the panel, mirroring the F5 sliders.
            bool noteChanged = false;
            if (Input.GetKeyDown(Cfg.TuneEarlierKey.Value))
            {
                Cfg.LeadOffsetManual.Value = Cfg.LeadOffsetManual.Value + step;
                noteChanged = true;
            }
            if (Input.GetKeyDown(Cfg.TuneLaterKey.Value))
            {
                Cfg.LeadOffsetManual.Value = Cfg.LeadOffsetManual.Value - step;
                noteChanged = true;
            }
            if (Input.GetKeyDown(KeyCode.End))
            {
                Cfg.LeadOffsetManual.Value = 0.0;
                noteChanged = true;
            }
            if (Input.GetKeyDown(KeyCode.PageUp))
            {
                Cfg.LeadFraction.Value = System.Math.Min(1.5, Cfg.LeadFraction.Value + 0.05);
                noteChanged = true;
            }
            if (Input.GetKeyDown(KeyCode.PageDown))
            {
                Cfg.LeadFraction.Value = System.Math.Max(0.0, Cfg.LeadFraction.Value - 0.05);
                noteChanged = true;
            }
            if (noteChanged)
            {
                Cfg.LeadOffsetManual.Value = System.Math.Round(Cfg.LeadOffsetManual.Value, 3);
                _engine.Retune(_clock);
                _engine.ResetJudgeStats();
                _panel.SyncFromConfig(Cfg);
                Log.LogInfo(string.Format("note trim: offset={0:F3}s fraction={1:F2}",
                    Cfg.LeadOffsetManual.Value, Cfg.LeadFraction.Value));
            }

            if (holdChanged)
                _panel.SyncFromConfig(Cfg);
        }

        /// <summary>True while the control panel is open.</summary>
        internal bool Tuning { get { return _panel != null && _panel.Visible; } }

        /// <summary>
        /// IMGUI draws over the finished frame, which is the only path that reliably
        /// composites a plugin's overlay in this game's render setup.
        /// </summary>
        private void OnGUI()
        {
            try
            {
                // The control panel must stay reachable even while the overlay itself is hidden,
                // otherwise there would be no way to switch the overlay back on.
                if (_view != null && _view.Visible)
                    _renderer.Draw(_engine.Visible, _engine, _view, Cfg, _clock.SongTime);

                if (_panel.Visible)
                {
                    _panel.SetClock(_clock);
                    _panel.Draw(_engine, Cfg, _clock);
                }
                else if (_chart.Count == 0)
                {
                    // No chart parsed means we are on a menu or in a cutscene: the game reports
                    // PlayerControl on the title screen too, so the chart is the reliable signal
                    // for "actually playing a level".
                    DrawHint();
                }
            }
            catch (Exception ex)
            {
                if (Time.unscaledTime > _nextWarn)
                {
                    _nextWarn = Time.unscaledTime + 5f;
                    Log.LogWarning("OnGUI: " + ex.Message);
                }
            }
        }

        /// <summary>
        /// A short hint in the top-left corner of the game's menus, so the control panel is
        /// discoverable. It disappears as soon as a level is entered, where it would only be in
        /// the way.
        /// </summary>
        private void DrawHint()
        {
            GUIStyle style = _panel.TextStyle();
            float refScale = Screen.height / 1080f;
            style.fontSize = Mathf.RoundToInt(16f * refScale);
            style.alignment = TextAnchor.UpperLeft;

            const string hint = "按 F5 调出控制面板";
            Vector2 size = style.CalcSize(new GUIContent(hint));

            float pad = 8f * refScale;
            var box = new Rect(12f * refScale, 12f * refScale, size.x + pad * 2f, size.y + pad);

            Color prev = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.45f);
            GUI.DrawTexture(box, Texture2D.whiteTexture);
            GUI.color = prev;

            Color prevText = style.normal.textColor;
            style.normal.textColor = new Color(1f, 1f, 1f, 0.92f);
            GUI.Label(new Rect(box.x + pad, box.y + pad * 0.5f, size.x, size.y), hint, style);
            style.normal.textColor = prevText;
        }

        /// <summary>
        /// DemoMode: pretend a 4/4 chart is playing so the overlay can be previewed or
        /// tuned without loading a level.
        /// </summary>
        private void StartDemoTick()
        {
            _demoTime += Cfg.PollInterval.Value;
            _clock.Playing = true;
            _clock.StateName = "DemoMode";
            _clock.Bpm = Cfg.DemoBpm.Value;
            _clock.Crotchet = _clock.Bpm > 0.0 ? 60.0 / _clock.Bpm : 0.4;
            _clock.SeqId = (int)(_demoTime / _clock.Crotchet);
            _clock.ForceTime(_demoTime + 1.5);
        }    }

    internal static class PluginInfo
    {
        public const string Guid = "com.dsh.adofalling";
        public const string Name = "AdoFalling - falling-note overlay";
        public const string Version = "0.5.1";
    }
}
