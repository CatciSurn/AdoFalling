using System;
using System.Globalization;
using BepInEx.Configuration;
using UnityEngine;

namespace AdoFalling
{
    /// <summary>
    /// The Chinese control panel opened with F5.
    ///
    /// Every tunable is exposed as a slider plus a text field; edits apply immediately so the
    /// effect is visible while playing, and the values are saved to the config file when the panel
    /// is closed.
    ///
    /// IMGUI is used because it is the only UI path that reliably draws over this game. The font
    /// is replaced with an OS font that has Chinese glyphs, since Unity's built-in IMGUI font does
    /// not, and Chinese text would otherwise render as boxes.
    /// </summary>
    internal sealed class ControlPanel
    {
        private const int WindowId = 0x41444F46;   // "ADOF"
        private const float Width = 470f;

        private Rect _window = new Rect(40f, 40f, Width, 640f);
        private Font _font;
        private bool _fontTried;
        private bool _fontInstalled;

        public bool Visible;

        /// <summary>
        /// Keyboard navigation index. The game routes input through Rewired, which swallows
        /// IMGUI mouse events, so the panel must be fully usable from the keyboard.
        /// </summary>
        private int _sel;
        private float _repeatAt;

        /// <summary>Rows in navigation order: (label, kind). Built once for bounds.</summary>
        private const int RowCount = 32;

        // Text-field buffers, mirroring the config values.
        private string _judgeLineY = "", _noteSpeed = "", _noteHeight = "", _laneWidth = "", _laneGap = "";
        private string _judgeOpacity = "", _noteOpacity = "", _laneOpacity = "", _laneEdge = "", _holdOpacity = "";
        private string _leadTrim = "", _leadFraction = "", _leadIn = "", _holdTrim = "", _holdScale = "";
        private string _fingerWindow = "";

        public void SyncFromConfig(FallingConfig c)
        {
            _judgeLineY = Fmt(c.JudgeLineY.Value);
            _noteSpeed = Fmt(c.NoteSpeedPxPerSec.Value);
            _noteHeight = Fmt(c.NoteHeight.Value);
            _laneWidth = Fmt(c.LaneWidth.Value);
            _laneGap = Fmt(c.LaneGap.Value);
            _judgeOpacity = Fmt(c.JudgeOpacity.Value);
            _noteOpacity = Fmt(c.NoteOpacity.Value);
            _laneOpacity = Fmt(c.LaneOpacity.Value);
            _laneEdge = Fmt(c.LaneAlpha.Value);
            _holdOpacity = Fmt(c.HoldBodyOpacity.Value);
            _leadTrim = Fmt(c.LeadOffsetManual.Value);
            _leadFraction = Fmt(c.LeadFraction.Value);
            _leadIn = Fmt(c.LeadInSeconds.Value);
            _holdTrim = Fmt(c.HoldOffsetSeconds.Value);
            _holdScale = Fmt(c.HoldScale.Value);
            _fingerWindow = Fmt(c.FingeringWindowMs.Value);
        }

        private static string Fmt(float v)
        {
            return v.ToString("0.###", CultureInfo.InvariantCulture);
        }

        private static string Fmt(double v)
        {
            return v.ToString("0.###", CultureInfo.InvariantCulture);
        }

        private static float Parse(string s, float fallback)
        {
            float v;
            if (float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v))
                return v;
            return fallback;
        }

        // ---------------------------------------------------------------- font

        private void EnsureFont()
        {
            if (_fontTried)
                return;
            _fontTried = true;

            try
            {
                string[] installed = Font.GetOSInstalledFontNames();
                string[] wanted = { "Microsoft YaHei UI", "Microsoft YaHei", "微软雅黑", "SimHei",
                                    "黑体", "DengXian", "等线", "SimSun", "宋体", "Arial Unicode MS" };
                for (int i = 0; i < wanted.Length; i++)
                {
                    for (int j = 0; j < installed.Length; j++)
                    {
                        if (!string.Equals(installed[j], wanted[i], StringComparison.OrdinalIgnoreCase))
                            continue;
                        Font f = Font.CreateDynamicFontFromOSFont(installed[j], 16);
                        if (f != null)
                        {
                            _font = f;
                            _fontInstalled = true;
                            FallingPlugin.Log.LogInfo("panel font: " + installed[j]);
                            return;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                FallingPlugin.Log.LogWarning("font enumeration failed: " + ex.Message);
            }

            string[] paths = { @"C:\Windows\Fonts\msyh.ttc", @"C:\Windows\Fonts\Deng.ttf",
                                @"C:\Windows\Fonts\simhei.ttf" };
            for (int i = 0; i < paths.Length; i++)
            {
                try
                {
                    Font f = Font.CreateDynamicFontFromOSFont(paths[i], 16);
                    if (f != null)
                    {
                        _font = f;
                        _fontInstalled = true;
                        FallingPlugin.Log.LogInfo("panel font: " + paths[i]);
                        return;
                    }
                }
                catch
                {
                    // try the next path
                }
            }
            FallingPlugin.Log.LogWarning("no Chinese font found; panel text will show as boxes");
        }

        private GUIStyle _label, _title, _section, _windowStyle;
        private Texture2D _panelFill, _borderTex;

        private void EnsureStyles()
        {
            EnsureFont();
            if (_label != null)
                return;

            _label = new GUIStyle(GUI.skin.label);
            _label.fontSize = 14;
            _label.alignment = TextAnchor.MiddleLeft;
            _label.normal.textColor = Color.white;
            if (_font != null)
                _label.font = _font;

            _title = new GUIStyle(_label) { fontSize = 17, fontStyle = FontStyle.Bold };
            _section = new GUIStyle(_label) { fontSize = 14, fontStyle = FontStyle.Bold };
            _section.normal.textColor = new Color(0.62f, 0.84f, 1f, 1f);

            // A flat 40% black fill for the panel body. Unity IMGUI has no border property on a
            // plain style, so the outline is drawn as four thin rectangles on top.
            _panelFill = Solid(new Color(0f, 0f, 0f, 0.40f));
            _borderTex = Solid(new Color(0.55f, 0.78f, 1f, 1f));
            _windowStyle = new GUIStyle(GUI.skin.window);
            _windowStyle.normal.background = _panelFill;
            _windowStyle.hover.background = _panelFill;
            _windowStyle.active.background = _panelFill;
            _windowStyle.focused.background = _panelFill;
            _windowStyle.onNormal.background = _panelFill;
            _windowStyle.normal.textColor = Color.white;
            _windowStyle.fontSize = 17;
            _windowStyle.fontStyle = FontStyle.Bold;
            if (_font != null)
                _windowStyle.font = _font;
            _windowStyle.border = new RectOffset(6, 6, 6, 6);
            _windowStyle.padding = new RectOffset(6, 6, 26, 6);

            if (_font != null)
            {
                GUI.skin.button.font = _font;
                GUI.skin.textField.font = _font;
                GUI.skin.label.font = _font;
                GUI.skin.box.font = _font;
                GUI.skin.toggle.font = _font;
                GUI.skin.window.font = _font;
            }
            GUI.skin.button.fontSize = 14;
            GUI.skin.textField.fontSize = 14;
        }

        // ---------------------------------------------------------------- drawing

        private static Texture2D Solid(Color c)
        {
            var t = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            t.SetPixel(0, 0, c);
            t.Apply();
            t.hideFlags = HideFlags.HideAndDontSave;
            return t;
        }

        /// <summary>
        /// A label style using the panel's Chinese font, for text drawn outside the panel
        /// (for example the F5 hint on the game's menus).
        /// </summary>
        public GUIStyle TextStyle()
        {
            EnsureStyles();
            return _label;
        }

        /// <summary>Draw a 2px outline around the panel, since IMGUI styles have no border.</summary>
        private void DrawBorder()
        {
            float w = _window.width;
            float h = _window.height;
            float t = 2f;
            Color prev = GUI.color;
            GUI.color = Color.white;
            GUI.DrawTexture(new Rect(_window.x, _window.y, w, t), _borderTex);
            GUI.DrawTexture(new Rect(_window.x, _window.y + h - t, w, t), _borderTex);
            GUI.DrawTexture(new Rect(_window.x, _window.y, t, h), _borderTex);
            GUI.DrawTexture(new Rect(_window.x + w - t, _window.y, t, h), _borderTex);
            GUI.color = prev;
        }

        public void Draw(FallingEngine engine, FallingConfig c, GameClock clock)
        {
            EnsureStyles();
            _engineRef = engine;
            HandleKeyboard(c, engine);
            _window = GUI.Window(WindowId, _window, id => Contents(engine, c), " AdoFalling 控制面板 ", _windowStyle);
            DrawBorder();
        }

        /// <summary>
        /// Keyboard control mirroring the mouse: arrow keys pick a row, left/right (or -/=)
        /// change it, Enter toggles a switch, PageDown/PageUp scrolls the whole panel.
        /// </summary>
        private void HandleKeyboard(FallingConfig c, FallingEngine engine)
        {
            float now = Time.unscaledTime;
            if (Input.GetKeyDown(KeyCode.DownArrow))
                _sel++;
            if (Input.GetKeyDown(KeyCode.UpArrow))
                _sel--;
            while (_sel < 0) _sel += RowCount;
            _sel %= RowCount;

            bool repeat = now >= _repeatAt;
            if (Input.GetKey(KeyCode.LeftArrow) || Input.GetKey(KeyCode.RightArrow))
            {
                if (repeat)
                {
                    _repeatAt = now + 0.06f;
                    float d = Input.GetKey(KeyCode.LeftArrow) ? -1f : 1f;
                    Nudge(c, engine, _sel, d);
                }
            }
            else
            {
                _repeatAt = 0f;
            }

            if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
                ToggleAt(c, _sel);
        }

        private void Nudge(FallingConfig c, FallingEngine engine, int sel, float dir)
        {
            float coarse = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift) ? 5f : 1f;
            switch (sel)
            {
                case 5: c.JudgeLineY.Value = Clamp(c.JudgeLineY.Value + dir * 0.005f * coarse, 0f, 1f); break;
                case 6: c.LaneWidth.Value = Clamp(c.LaneWidth.Value + dir * 4f * coarse, 40f, 400f); break;
                case 7: c.LaneGap.Value = Clamp(c.LaneGap.Value + dir * 1f * coarse, 0f, 80f); break;
                case 8: c.NoteSpeedPxPerSec.Value = Clamp(c.NoteSpeedPxPerSec.Value + dir * 20f * coarse, 100f, 3000f); break;
                case 9: c.NoteHeight.Value = Clamp(c.NoteHeight.Value + dir * 1f * coarse, 6f, 120f); break;
                case 10: c.LeadOffsetManual.Value = c.LeadOffsetManual.Value + dir * 0.005f * coarse; break;
                case 11: c.LeadFraction.Value = Clamp(c.LeadFraction.Value + dir * 0.02f * coarse, 0f, 1.5f); break;
                case 12: c.LeadInSeconds.Value = Clamp(c.LeadInSeconds.Value + dir * 0.02f * coarse, 0f, 1f); break;
                case 13: c.HoldOffsetSeconds.Value = c.HoldOffsetSeconds.Value + dir * 0.005f * coarse; break;
                case 14: c.HoldScale.Value = Clamp(c.HoldScale.Value + dir * 0.02f * coarse, 0.2f, 3f); break;
                case 15: c.JudgeOpacity.Value = Clamp(c.JudgeOpacity.Value + dir * 0.02f * coarse, 0f, 1f); break;
                case 16: c.NoteOpacity.Value = Clamp(c.NoteOpacity.Value + dir * 0.02f * coarse, 0f, 1f); break;
                case 17: c.LaneOpacity.Value = Clamp(c.LaneOpacity.Value + dir * 0.02f * coarse, 0f, 1f); break;
                case 18: c.LaneAlpha.Value = Clamp(c.LaneAlpha.Value + dir * 0.02f * coarse, 0f, 1f); break;
                case 19: c.HoldBodyOpacity.Value = Clamp(c.HoldBodyOpacity.Value + dir * 0.02f * coarse, 0f, 1f); break;
                case 27: c.LaneMapping.Value = NextMapping(c.LaneMapping.Value); break;
                case 28: c.FingeringWindowMs.Value = Clamp(c.FingeringWindowMs.Value + dir * 5f * coarse, 60f, 400f); break;
                case 0: case 1: case 2: case 3: case 4:
                case 20: case 21: case 22: case 23: case 24: case 25: case 26:
                    ToggleAt(c, sel);
                    break;
                default: break;   // 29..31 are actions: Enter triggers them, arrows do not
            }
            c.LeadOffsetManual.Value = (float)Math.Round(c.LeadOffsetManual.Value, 3);
            c.HoldOffsetSeconds.Value = (float)Math.Round(c.HoldOffsetSeconds.Value, 3);
            SyncFromConfig(c);
            engine.Retune(_clockRef);
        }

        private void ToggleAt(FallingConfig c, int sel)
        {
            switch (sel)
            {
                case 0: c.ShowJudgeLine.Value = !c.ShowJudgeLine.Value; break;
                case 1: c.ShowLanes.Value = !c.ShowLanes.Value; break;
                case 2: c.ShowNotes.Value = !c.ShowNotes.Value; break;
                case 3: c.ShowHitEffects.Value = !c.ShowHitEffects.Value; break;
                case 4: c.ShowJudgeText.Value = !c.ShowJudgeText.Value; break;
                case 20: c.HoldShrinkWhileHeld.Value = !c.HoldShrinkWhileHeld.Value; break;
                case 21: c.SkipHoldReleaseTiles.Value = !c.SkipHoldReleaseTiles.Value; break;
                case 22: c.CountdownLead.Value = !c.CountdownLead.Value; break;
                case 23: c.NoteRotation.Value = !c.NoteRotation.Value; break;
                case 24: c.MirrorDirection.Value = !c.MirrorDirection.Value; break;
                case 25: c.DebugHud.Value = !c.DebugHud.Value; break;
                case 26: c.AutoPlay.Value = !c.AutoPlay.Value; break;
                case 27: c.LaneMapping.Value = NextMapping(c.LaneMapping.Value); break;
                case 28: c.FingeringWindowMs.Value = FingeringPlanner.DefaultWindowMs; break;
                case 29: RestoreDefaults(c); SyncFromConfig(c); _engineRef.Retune(_clockRef); break;
                case 30:
                    c.HoldOffsetSeconds.Value = 0f; c.HoldScale.Value = 1f; SyncFromConfig(c);
                    _engineRef.Retune(_clockRef);
                    break;
                case 31: _engineRef.ResetJudgeStats(); break;
            }
        }

        /// <summary>Cycle the lane mapping; the chart re-lanes on the next frame.</summary>
        private static LaneMapping NextMapping(LaneMapping m)
        {
            switch (m)
            {
                case LaneMapping.Fingering: return LaneMapping.Macro;
                case LaneMapping.Macro: return LaneMapping.Position;
                case LaneMapping.Position: return LaneMapping.Cycle;
                case LaneMapping.Cycle: return LaneMapping.Sequential;
                default: return LaneMapping.Fingering;
            }
        }

        private static string LaneMappingName(LaneMapping m)
        {
            switch (m)
            {
                case LaneMapping.Fingering: return "人性化（轮指/交替）";
                case LaneMapping.Macro: return "宏排键（谱面文件）";
                case LaneMapping.Position: return "按进入方向";
                case LaneMapping.Cycle: return "按序号循环";
                default: return "按顺序";
            }
        }

        /// <summary>One line about the chart-file plan, shown in the 排键 section.</summary>
        private static string MacroPlanSummary(FallingConfig c)
        {
            MacroPlan plan = MacroPlan.Current;
            if (plan == null)
            {
                if (!string.IsNullOrEmpty(c.FilePath.Value))
                    return "谱面文件：未载入（进关卡按 F 开始）";
                return "谱面文件：未配置（Chart/FilePath）";
            }

            string name = plan.FilePath;
            int slash = name.LastIndexOfAny(new char[] { '\\', '/' });
            if (slash >= 0)
                name = name.Substring(slash + 1);

            if (MacroPlan.Active)
                return string.Format("谱面文件：已开始 {0}（锚点 {1:F2}s）", name, MacroPlan.AnchorSongTime);
            return "谱面文件：已载入 " + name + "，按 F 开始";
        }

        private static float Clamp(float v, float lo, float hi)
        {
            return v < lo ? lo : (v > hi ? hi : v);
        }

        private static double Clamp(double v, double lo, double hi)
        {
            return v < lo ? lo : (v > hi ? hi : v);
        }

        private FallingEngine _engineRef;

        private void Contents(FallingEngine engine, FallingConfig c)
        {
            float y = 32f;
            const float rowH = 25f;
            const float labelW = 150f;
            const float fieldW = 62f;
            const float sliderW = Width - labelW - fieldW - 62f;

            y = Section(y, "显示（一键切换）");
            y = Toggle(y, rowH, "显示判定线", c.ShowJudgeLine);
            y = Toggle(y, rowH, "显示轨道", c.ShowLanes);
            y = Toggle(y, rowH, "显示音符 / 长按", c.ShowNotes);
            y = Toggle(y, rowH, "显示打击特效", c.ShowHitEffects);
            y = Toggle(y, rowH, "显示判定文字", c.ShowJudgeText);

            y = Section(y, "判定线与轨道");
            y = Slider(y, rowH, labelW, sliderW, fieldW, "判定线高度", c.JudgeLineY, 0f, 1f, ref _judgeLineY);
            y = Slider(y, rowH, labelW, sliderW, fieldW, "轨道宽度", c.LaneWidth, 40f, 400f, ref _laneWidth);
            y = Slider(y, rowH, labelW, sliderW, fieldW, "轨道间距", c.LaneGap, 0f, 80f, ref _laneGap);

            y = Section(y, "音符");
            y = Slider(y, rowH, labelW, sliderW, fieldW, "下落速度", c.NoteSpeedPxPerSec, 100f, 3000f, ref _noteSpeed);
            y = Slider(y, rowH, labelW, sliderW, fieldW, "音符高度", c.NoteHeight, 6f, 120f, ref _noteHeight);

            y = Section(y, "对齐（判定与视觉）");
            y = Slider(y, rowH, labelW, sliderW, fieldW, "音符提前量(秒)", c.LeadOffsetManual, -0.5f, 0.5f, ref _leadTrim);
            y = Slider(y, rowH, labelW, sliderW, fieldW, "提前量系数", c.LeadFraction, 0f, 1.5f, ref _leadFraction);
            y = Slider(y, rowH, labelW, sliderW, fieldW, "开场余量(秒)", c.LeadInSeconds, 0f, 1f, ref _leadIn);
            y = Slider(y, rowH, labelW, sliderW, fieldW, "长按提前量(秒)", c.HoldOffsetSeconds, -0.5f, 0.5f, ref _holdTrim);
            y = Slider(y, rowH, labelW, sliderW, fieldW, "长按长度系数", c.HoldScale, 0.2f, 3f, ref _holdScale);

            y = Section(y, "透明度");
            y = Slider(y, rowH, labelW, sliderW, fieldW, "判定线", c.JudgeOpacity, 0f, 1f, ref _judgeOpacity);
            y = Slider(y, rowH, labelW, sliderW, fieldW, "音符", c.NoteOpacity, 0f, 1f, ref _noteOpacity);
            y = Slider(y, rowH, labelW, sliderW, fieldW, "轨道底色", c.LaneOpacity, 0f, 1f, ref _laneOpacity);
            y = Slider(y, rowH, labelW, sliderW, fieldW, "轨道边框", c.LaneAlpha, 0f, 1f, ref _laneEdge);
            y = Slider(y, rowH, labelW, sliderW, fieldW, "长按尾迹", c.HoldBodyOpacity, 0f, 1f, ref _holdOpacity);

            y = Section(y, "行为开关");
            y = Toggle(y, rowH, "长按：按住期间缩短", c.HoldShrinkWhileHeld);
            y = Toggle(y, rowH, "长按：跳过松手方块", c.SkipHoldReleaseTiles);
            y = Toggle(y, rowH, "开场从顶端下落", c.CountdownLead);
            y = Toggle(y, rowH, "音符倾斜", c.NoteRotation);
            y = Toggle(y, rowH, "轨道右到左", c.MirrorDirection);
            y = Toggle(y, rowH, "显示诊断信息", c.DebugHud);
            y = Toggle(y, rowH, "自动播放（游戏自演）", c.AutoPlay);

            y = Section(y, "排键 / 轨道映射");
            y = Choice(y, rowH, "排键方式", c.LaneMapping);
            y = Slider(y, rowH, labelW, sliderW, fieldW, "轮指窗口(毫秒)", c.FingeringWindowMs, 60f, 400f, ref _fingerWindow);
            y = Key(y, "人性化 = 快速连打轮指、较慢音符两指交替（永不连压同轨）");
            y = Key(y, MacroPlanSummary(c));
            y = Key(y, "宏排键需要 Chart/FilePath 里的谱面文件；按 F 载入并切换");

            y = Section(y, "操作");
            float bw = (Width - 60f) / 3f;
            if (GUI.Button(new Rect(20f, y, bw, 26f), "恢复默认", _label))
            {
                RestoreDefaults(c);
                SyncFromConfig(c);
                engine.Retune(_clockRef);
                engine.ResetJudgeStats();
            }
            if (GUI.Button(new Rect(20f + bw + 10f, y, bw, 26f), "长按归零", _label))
            {
                c.HoldOffsetSeconds.Value = 0f;
                c.HoldScale.Value = 1f;
                SyncFromConfig(c);
            }
            if (GUI.Button(new Rect(20f + (bw + 10f) * 2f, y, bw, 26f), "清除统计数据", _label))
            {
                engine.ResetJudgeStats();
            }
            y += 32f;

            y = Note(y, "对齐实测：最近 {0:F0}px  平均 {1:F0}px  样本 {2}   {3}",
                engine.JudgeOffsetPx, engine.JudgeAvgPx, engine.JudgeSamples, Verdict(engine));
            y = Note(y, "音符 {0}   长按 {1}   屏上 {2}   判定线 y={3:F0}",
                engine.NoteCount, engine.HoldCount, engine.VisibleCount, engine.JudgeLineY);
            y = Key(y, "键盘（本游戏会吞掉鼠标点击，请用键盘）：" + RowName(_sel) +
                       "   ↑↓ 选择   ←→ 调整(Shift 加速)   Enter 切换");
            y = Key(y, "关闭面板（F5）自动保存；鼠标若可用也可直接点");
            y = Key(y, "提示：判定线/音符显隐在最后两行，Enter 即可一键切换");

            _window.height = y + 34f;
            GUI.DragWindow(new Rect(0f, 0f, Width, 26f));
        }

        /// <summary>Rows in navigation order, for the keyboard hint.</summary>
        private static string RowName(int sel)
        {
            string[] names =
            {
                "显示→判定线", "显示→轨道", "显示→音符", "显示→打击特效", "显示→判定文字",
                "判定线高度", "轨道宽度", "轨道间距",
                "下落速度", "音符高度",
                "音符提前量", "提前量系数", "开场余量", "长按提前量", "长按长度系数",
                "透明度→判定线", "透明度→音符", "透明度→轨道底色", "透明度→轨道边框", "透明度→长按尾迹",
                "开关→长按缩短", "开关→跳过松手方块", "开关→开场从顶端", "开关→音符倾斜",
                "开关→轨道右到左", "开关→诊断信息", "开关→自动播放",
                "排键方式", "轮指窗口(毫秒)",
                "操作→恢复默认", "操作→长按归零", "操作→清除统计",
            };
            if (sel < 0 || sel >= names.Length)
                return "?";
            return "[" + (sel + 1) + "] " + names[sel];
        }

        private GameClock _clockRef;

        public void SetClock(GameClock clock)
        {
            _clockRef = clock;
        }

        private float Section(float y, string title)
        {
            GUI.Label(new Rect(16f, y, Width - 30f, 20f), title, _section);
            return y + 22f;
        }

        private float Toggle(float y, float rowH, string label, ConfigEntry<bool> entry)
        {
            // Draw an explicit tick / cross: the stock toggle box alone is easy to misread.
            bool on = entry.Value;
            GUIStyle mark = new GUIStyle(_label);
            mark.fontSize = 16;
            mark.fontStyle = FontStyle.Bold;
            mark.normal.textColor = on ? new Color(0.35f, 1f, 0.45f, 1f)
                                       : new Color(1f, 0.42f, 0.38f, 1f);

            bool v = GUI.Toggle(new Rect(24f, y, 300f, rowH), on, " " + label, _label);
            if (v != on)
                entry.Value = v;

            // Written in Chinese on purpose: CJK glyphs are guaranteed to be in the panel font,
            // whereas ✓ / ✗ may be missing and would show up as boxes.
            GUI.Label(new Rect(24f + labelWForToggle, y, 40f, rowH), on ? "[开]" : "[关]", mark);
            return y + rowH;
        }

        private const float labelWForToggle = 214f;

        /// <summary>A one-line selector: click (or Enter / arrow keys) to cycle the value.</summary>
        private float Choice(float y, float rowH, string label, ConfigEntry<LaneMapping> entry)
        {
            GUI.Label(new Rect(16f, y, labelWForToggle, rowH), label, _label);
            if (GUI.Button(new Rect(24f + labelWForToggle, y, 200f, rowH),
                    LaneMappingName(entry.Value), _label))
                entry.Value = NextMapping(entry.Value);
            return y + rowH;
        }

        private float Slider(float y, float rowH, float labelW, float sliderW, float fieldW,            string label, ConfigEntry<float> entry, float min, float max, ref string text)
        {
            if (Mark(y, rowH))
                GUI.Label(new Rect(6f, y, 8f, rowH), "▶", _label);
            GUI.Label(new Rect(16f, y, labelW, rowH), label, _label);

            float v = GUI.HorizontalSlider(new Rect(16f + labelW, y + 8f, sliderW, 16f), entry.Value, min, max);
            if (Math.Abs(v - entry.Value) > 0.00001f)
            {
                entry.Value = v;
                text = Fmt(v);
            }

            string typed = GUI.TextField(new Rect(16f + labelW + sliderW + 8f, y, fieldW, rowH), text, 12, _label);
            if (!string.Equals(typed, text, StringComparison.Ordinal))
            {
                text = typed;
                float parsed = Parse(typed, entry.Value);
                if (parsed < min) parsed = min;
                if (parsed > max) parsed = max;
                entry.Value = parsed;
            }
            return y + rowH;
        }

        private float Slider(float y, float rowH, float labelW, float sliderW, float fieldW,
            string label, ConfigEntry<double> entry, float min, float max, ref string text)
        {
            GUI.Label(new Rect(16f, y, labelW, rowH), label, _label);

            float v = GUI.HorizontalSlider(new Rect(16f + labelW, y + 8f, sliderW, 16f), (float)entry.Value, min, max);
            if (Math.Abs(v - entry.Value) > 0.00001f)
            {
                entry.Value = v;
                text = Fmt(v);
            }

            string typed = GUI.TextField(new Rect(16f + labelW + sliderW + 8f, y, fieldW, rowH), text, 12, _label);
            if (!string.Equals(typed, text, StringComparison.Ordinal))
            {
                text = typed;
                double parsed = Parse(typed, (float)entry.Value);
                if (parsed < min) parsed = min;
                if (parsed > max) parsed = max;
                entry.Value = parsed;
            }
            return y + rowH;
        }

        private float Note(float y, string format, params object[] args)
        {
            GUIStyle st = _label;
            Color prev = st.normal.textColor;
            st.normal.textColor = new Color(0.82f, 0.92f, 1f, 1f);
            GUI.Label(new Rect(20f, y, Width - 40f, 20f), string.Format(format, args), st);
            st.normal.textColor = prev;
            return y + 20f;
        }

        /// <summary>A hint line: smaller and in a dimmer colour.</summary>
        private float Key(float y, string text)
        {
            GUIStyle st = _label;
            Color prev = st.normal.textColor;
            st.normal.textColor = new Color(0.75f, 1f, 0.8f, 1f);
            GUI.Label(new Rect(20f, y, Width - 36f, 20f), text, st);
            st.normal.textColor = prev;
            return y + 19f;
        }

        /// <summary>True when this row is the one the keyboard is pointing at.</summary>
        private bool Mark(float y, float rowH)
        {
            return false;
        }

        private static string Verdict(FallingEngine engine)
        {
            if (engine.JudgeSamples < 5)
                return "请先打几个音符";
            float avg = engine.JudgeAvgPx;
            if (Math.Abs(avg) <= 6f)
                return "已对齐";
            return avg > 0f ? "音符偏晚 → 减小提前量" : "音符偏早 → 增大提前量";
        }

        /// <summary>The calibrated defaults this build ships with.</summary>
        private static void RestoreDefaults(FallingConfig c)
        {
            c.JudgeLineY.Value = 0.18f;
            c.NoteSpeedPxPerSec.Value = 620f;
            c.NoteHeight.Value = 26f;
            c.LaneWidth.Value = 110f;
            c.LaneGap.Value = 8f;
            c.JudgeOpacity.Value = 1f;
            c.NoteOpacity.Value = 1f;
            c.LaneOpacity.Value = 0.18f;
            c.LaneAlpha.Value = 0.7f;
            c.HoldBodyOpacity.Value = 0.45f;
            c.LeadOffsetManual.Value = 0.07;
            c.LeadFraction.Value = 0.0;
            c.LeadInSeconds.Value = 0.35;
            c.HoldOffsetSeconds.Value = 0.0;
            c.HoldScale.Value = 1.0;
            c.HoldShrinkWhileHeld.Value = true;
            c.SkipHoldReleaseTiles.Value = true;
            c.CountdownLead.Value = true;
            c.LaneMapping.Value = LaneMapping.Fingering;
            c.FingeringWindowMs.Value = FingeringPlanner.DefaultWindowMs;
            c.ShowJudgeLine.Value = true;
            c.ShowLanes.Value = true;
            c.ShowNotes.Value = true;
            c.ShowHitEffects.Value = true;
            c.ShowJudgeText.Value = true;
        }
    }
}
