using System.Collections.Generic;
using UnityEngine;

namespace AdoFalling
{
    /// <summary>
    /// Draws the falling overlay with immediate-mode GUI.
    ///
    /// The game's render setup does not composite UGUI canvases reliably from a plugin
    /// (a screen-space overlay canvas and a plugin-owned camera canvas were both built
    /// with correct geometry but produced no visible output), while IMGUI draws on top of
    /// the finished frame every time. This renderer therefore owns the overlay drawing and
    /// needs no canvas, camera, sprite or shader asset.
    ///
    /// Layout: lanes are a vertical strip that starts at the top edge of the screen and
    /// runs down to the judgement line; notes fall from the top towards that line.
    /// Only the judgement line, the lanes and the notes are drawn - everything else stays
    /// transparent so the original ADOFAI chart remains visible underneath.
    /// </summary>
    internal sealed class OverlayRenderer
    {
        private Texture2D _white;
        private GUIStyle _textStyle;
        private float _fps;
        private double _clockNow;

        private Texture2D White()
        {
            if (_white == null)
            {
                _white = new Texture2D(1, 1, TextureFormat.RGBA32, false);
                _white.SetPixel(0, 0, Color.white);
                _white.Apply();
                _white.hideFlags = HideFlags.HideAndDontSave;
            }
            return _white;
        }

        private GUIStyle TextStyle()
        {
            if (_textStyle == null)
            {
                _textStyle = new GUIStyle(GUI.skin.label);
                _textStyle.alignment = TextAnchor.MiddleCenter;
                _textStyle.fontStyle = FontStyle.Bold;
                _textStyle.richText = false;
            }
            return _textStyle;
        }

        /// <summary>Fill a screen-space rectangle. IMGUI y grows downward.</summary>
        private void Fill(Rect r, Color c)
        {
            if (c.a <= 0.002f || r.width <= 0f || r.height <= 0f)
                return;
            Color prev = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(r, White());
            GUI.color = prev;
        }

        /// <summary>Fill a rectangle rotated about its own centre.</summary>
        private void FillRotated(Rect r, Color c, float degrees)
        {
            if (c.a <= 0.002f || r.width <= 0f || r.height <= 0f)
                return;
            Matrix4x4 prev = GUI.matrix;
            GUIUtility.RotateAroundPivot(degrees, new Vector2(r.x + r.width * 0.5f, r.y + r.height * 0.5f));
            Fill(r, c);
            GUI.matrix = prev;
        }

        /// <summary>Horizontal offset of a lane in screen pixels, honouring the mirror option.</summary>
        private float LaneX(FallingConfig cfg, float x0, float laneW, float laneGap, int lane)
        {
            if (lane < 0) lane = 0;
            if (lane >= FallingView.LaneCount) lane = FallingView.LaneCount - 1;
            int idx = cfg.MirrorDirection.Value ? (FallingView.LaneCount - 1 - lane) : lane;
            // Same 0<->2, 1<->3 lane order as FallingView.Layout, so notes, lane washes,
            // bursts and judgement text all land on the same swapped columns.
            idx ^= 2;
            return x0 + idx * (laneW + laneGap);
        }

        public void Draw(List<NoteInstance> notes, FallingEngine engine, FallingView view,
            FallingConfig cfg, double clockNow)
        {
            _clockNow = clockNow;
            float w = Screen.width;
            float h = Screen.height;
            float refScale = h / 1080f;
            _fps = Mathf.Lerp(_fps, 1f / Mathf.Max(0.0001f, Time.unscaledDeltaTime), 0.1f);

            // Judgement line: view geometry is measured from the bottom, IMGUI from the top.
            float judgeY = h - view.JudgeY;
            float laneW = view.LaneWidthPx;
            float laneGap = cfg.LaneGap.Value * refScale;
            float noteH = Mathf.Max(6f, cfg.NoteHeight.Value * refScale);

            float total = FallingView.LaneCount * laneW + (FallingView.LaneCount - 1) * laneGap;
            float x0 = (w - total) * 0.5f;

            // The strip runs from the top edge of the screen down to the judgement line.
            float stripTop = 0f;
            float stripHeight = judgeY - stripTop;
            if (stripHeight < 1f)
                stripHeight = 1f;

            // ---- lane bodies -------------------------------------------------------
            // Lanes have their own switch: hiding the notes while keeping the lanes is a useful
            // minimal-overlay setup, so the two are independent.
            if (cfg.ShowLanes.Value)
            {
                Color body = cfg.LaneColor.Value;
                body.a = cfg.LaneOpacity.Value;
                for (int i = 0; i < FallingView.LaneCount; i++)
                {
                    float x = LaneX(cfg, x0, laneW, laneGap, i);
                    Fill(new Rect(x, stripTop, laneW, stripHeight), body);
                }

                Color edge = cfg.LaneEdgeColor.Value;
                edge.a = cfg.LaneAlpha.Value;
                float t = Mathf.Max(1f, 2f * refScale);
                for (int i = 0; i < FallingView.LaneCount; i++)
                {
                    float x = LaneX(cfg, x0, laneW, laneGap, i);
                    Fill(new Rect(x, stripTop, t, stripHeight), edge);
                    Fill(new Rect(x + laneW - t, stripTop, t, stripHeight), edge);
                    // bottom edge of the lane, sitting on the judgement line
                    Fill(new Rect(x, judgeY - t, laneW, t), edge);
                }
            }

            // ---- per-lane hit flash ------------------------------------------------
            if (engine.LaneFlashActive && cfg.ShowHitEffects.Value)
            {
                float k = engine.LaneFlash;
                float x = LaneX(cfg, x0, laneW, laneGap, engine.LaneFlashLane);
                float strength = cfg.LaneFlashStrength.Value;

                // The lane washes white with a tint of the judgement colour. White is what
                // actually reads as "this lane fired", so it carries most of the intensity.
                Color wash = Color.Lerp(Color.white, engine.HitFlashColor, 0.45f);
                wash.a = Mathf.Clamp01(strength * k);
                Fill(new Rect(x, stripTop, laneW, stripHeight), wash);

                // near the line it stays bright almost the whole time so the eye lands there
                Color near = Color.white;
                near.a = Mathf.Clamp01(strength * (0.35f + 0.65f * k));
                float nearH = Mathf.Min(stripHeight, h * 0.45f);
                Fill(new Rect(x, judgeY - nearH, laneW, nearH), near);

                // bright borders on the flashing lane make the column unmistakable
                Color rim = Color.white;
                rim.a = Mathf.Clamp01(0.9f * k);
                float rw = Mathf.Max(2f, 3f * refScale);
                Fill(new Rect(x, stripTop, rw, stripHeight), rim);
                Fill(new Rect(x + laneW - rw, stripTop, rw, stripHeight), rim);
            }

            // ---- judgement line ----------------------------------------------------
            if (cfg.ShowJudgeLine.Value)
            {
                Color jc = cfg.JudgeColor.Value;
                jc.a = cfg.JudgeOpacity.Value;
                float jt = Mathf.Max(2f, 4f * refScale);
                Fill(new Rect(0f, judgeY - jt * 0.5f, w, jt), jc);

                if (engine.JudgeFlash > 0.01f && cfg.ShowHitEffects.Value)
                {
                    float k = engine.JudgeFlash;
                    float strength = cfg.JudgeFlashStrength.Value;

                    // wide soft glow across the whole line
                    Color glow = jc;
                    glow.a = Mathf.Clamp01(strength * 0.6f * k);
                    float gh = Mathf.Max(18f, 170f * refScale) * (0.5f + 0.5f * k);
                    Fill(new Rect(0f, judgeY - gh * 0.5f, w, gh), glow);

                    // solid white core that pops on the hit
                    Color core = Color.white;
                    core.a = Mathf.Clamp01(strength * (0.4f + 0.6f * k));
                    float ch = jt * (4f + 9f * k);
                    Fill(new Rect(0f, judgeY - ch * 0.5f, w, ch), core);
                }
            }

            // ---- the hit lane gets a beam through the line even with the line hidden ----
            if (engine.JudgeFlash > 0.01f && engine.HitFlashActive && cfg.ShowHitEffects.Value)
            {
                float jt2 = Mathf.Max(2f, 4f * refScale);
                float hx = LaneX(cfg, x0, laneW, laneGap, engine.HitFlashLane);
                Color beam = engine.HitFlashColor;
                beam.a = Mathf.Clamp01(0.95f * engine.HitFlash);

                float wide = laneW * (1f + 0.6f * (1f - engine.HitFlash));
                float x2 = hx + (laneW - wide) * 0.5f;
                float hh = jt2 * (4f + 16f * engine.HitFlash);
                Fill(new Rect(x2, judgeY - hh * 0.5f, wide, hh), beam);

                Color beamWhite = Color.white;
                beamWhite.a = Mathf.Clamp01(0.8f * engine.HitFlash * engine.HitFlash);
                float wh = jt2 * (2f + 8f * engine.HitFlash);
                Fill(new Rect(x2, judgeY - wh * 0.5f, wide, wh), beamWhite);
            }

            // ---- hold bars ---------------------------------------------------------
            // Drawn before the heads so a long bar can never cover another note's head.
            //
            // Geometry (HoldShrinkWhileHeld):
            //   falling   - the bar's head is the leading (lower) end, so the whole bar descends
            //               with the note and the head meets the judgement line at entryTime;
            //   holding   - the judged end is pinned to the judgement line and the bar shrinks
            //               towards it, so the tail is what gets consumed while the key is down.
            for (int i = 0; i < notes.Count; i++)
            {
                NoteInstance n = notes[i];
                if (!n.Active || n.HoldSeconds <= 0.0 || cfg.NoteRotation.Value)
                    continue;

                float pxPerSec = Mathf.Max(1f, cfg.NoteSpeedPxPerSec.Value * refScale);
                Color col = n.Color;
                col.a *= cfg.NoteOpacity.Value;

                double remaining = n.HoldSeconds;
                double progress = ChartReader.ReadHoldCompletion(n.Floor);
                bool holding = progress > 0.0 && _clockNow > n.HitTime;
                if (holding)
                    remaining = n.HoldSeconds * (1.0 - System.Math.Min(1.0, progress));

                float length = (float)(remaining * cfg.HoldScale.Value * pxPerSec);
                if (length < 1f)
                    continue;

                // The head (leading end) position for this frame.
                float headY = judgeY - n.DistancePx;
                headY -= (float)(cfg.HoldOffsetSeconds.Value * pxPerSec);

                // While holding, the judged end clamps to the line and the bar shrinks into it.
                float headEnd = holding ? judgeY : headY;
                float tailEnd = headEnd - length;

                if (headEnd < stripTop - length || tailEnd > h)
                    continue;

                float x = LaneX(cfg, x0, laneW, laneGap, n.Lane);
                Rect bar = new Rect(x, tailEnd, laneW, length);

                Color barBody = col;
                barBody.a = col.a * Mathf.Clamp01(cfg.HoldBodyOpacity.Value);
                Fill(bar, barBody);

                Color rim = col;
                rim.a = Mathf.Min(1f, col.a * 1.1f);
                float rw = Mathf.Max(2f, 3f * refScale);
                Fill(new Rect(bar.x, bar.y, rw, bar.height), rim);
                Fill(new Rect(bar.xMax - rw, bar.y, rw, bar.height), rim);

                // bright cap on the far (tail) end
                Fill(new Rect(bar.x, bar.y, laneW, Mathf.Max(1f, 3f * refScale)), col);

                // bright block on the judged end, marking the moment the key must go down
                if (!holding)
                    Fill(new Rect(bar.x, bar.yMax - noteH * 0.5f, laneW, noteH), col);
            }

            for (int i = 0; i < notes.Count; i++)
            {
                NoteInstance n = notes[i];
                if (!n.Active)
                    continue;

                float x = LaneX(cfg, x0, laneW, laneGap, n.Lane);

                // n.DistancePx is measured upward from the line; screen y grows downward.
                float y = judgeY - n.DistancePx;
                if (y < stripTop - noteH || y > judgeY + noteH * 3f)
                    continue;                       // outside the strip

                Color col = n.Color;
                col.a *= cfg.NoteOpacity.Value;

                if (cfg.NoteRotation.Value)
                {
                    float deg = Mathf.Clamp(-n.DistancePx / Mathf.Max(1f, h) * 22f, -22f, 22f);
                    FillRotated(new Rect(x, y - noteH * 0.5f, laneW, noteH), col, deg);
                }
                else
                {
                    // The head only: the hold tail was already drawn in the pass above, so it
                    // can never cover another note's head.
                    Fill(new Rect(x, y - noteH * 0.5f, laneW, noteH), col);
                }
                // brighter leading edge (the side that reaches the line first)
                Color core = col;
                core.a = Mathf.Min(1f, col.a * 1.35f);
                Fill(new Rect(x, y + noteH * 0.5f - Mathf.Max(1f, 3f * refScale),
                    laneW, Mathf.Max(1f, 3f * refScale)), core);
            }

            // ---- hit bursts --------------------------------------------------------
            // Graphics are drawn first and the judgement text afterwards, so a later burst
            // can never paint over an earlier label. Both have their own visibility switch.
            List<HitEffect> fx = engine.Effects;
            for (int i = 0; i < (cfg.ShowHitEffects.Value ? fx.Count : 0); i++)
            {
                HitEffect e = fx[i];
                float progress = Mathf.Clamp01(e.Age / e.Lifetime);
                float k = 1f - progress;                               // 1 -> 0
                float x = LaneX(cfg, x0, laneW, laneGap, e.Lane);

                // bright bar that expands and fades at the judgement line
                Color burst = e.Color;
                burst.a = 0.9f * k;
                float bh = noteH * (0.7f + 1.2f * progress);
                Fill(new Rect(x, judgeY - bh * 0.5f, laneW, bh), burst);

                // expanding ring over the lane
                Color ring = cfg.JudgeColor.Value;
                ring.a = 0.8f * k * k;
                float ringT = Mathf.Max(1f, 2f * refScale);
                float rw = laneW * (0.6f + 1.3f * progress);
                Fill(new Rect(x + (laneW - rw) * 0.5f, judgeY - ringT * 0.5f, rw, ringT), ring);
            }

            // ---- judgement text, always on top --------------------------------------
            for (int i = 0; i < (cfg.ShowJudgeText.Value ? fx.Count : 0); i++)
            {
                HitEffect e = fx[i];
                if (string.IsNullOrEmpty(e.Text))
                    continue;

                float progress = Mathf.Clamp01(e.Age / e.Lifetime);
                float k = 1f - progress;
                float x = LaneX(cfg, x0, laneW, laneGap, e.Lane);

                GUIStyle style = TextStyle();
                int size = Mathf.RoundToInt(Mathf.Lerp(40f, 26f, progress) * refScale);
                if (size < 10) size = 10;
                style.fontSize = size;

                float alpha = Mathf.Clamp01(k * 1.4f);
                style.normal.textColor = new Color(e.Color.r, e.Color.g, e.Color.b, alpha);

                // a dark shadow keeps the label readable over a bright chart or a flash
                GUIStyle shadow = TextStyle();
                shadow.fontSize = size;
                shadow.normal.textColor = new Color(0f, 0f, 0f, alpha * 0.75f);

                var label = new Rect(x, judgeY - noteH * 2f - progress * 70f * refScale,
                    laneW, noteH * 2.2f);
                var shadowRect = new Rect(label.x + 2f, label.y + 2f, label.width, label.height);
                GUI.Label(shadowRect, e.Text, shadow);
                GUI.Label(label, e.Text, style);
            }

            if (cfg.DebugHud.Value && !(FallingPlugin.Instance != null && FallingPlugin.Instance.Tuning))
            {
                Color refCol = new Color(1f, 0f, 1f, 0.9f);
                float rw = 26f * refScale;
                float rh = 3f * refScale;
                Fill(new Rect(0f, judgeY - rh * 0.5f, rw, rh), refCol);
                Fill(new Rect(w - rw, judgeY - rh * 0.5f, rw, rh), refCol);
                DrawDiagnostics(engine, cfg, refScale, judgeY, laneW);
            }
        }

        private void DrawDiagnostics(FallingEngine engine, FallingConfig cfg, float refScale,
            float judgeY, float laneW)
        {
            float h = Screen.height;
            GUIStyle style = TextStyle();
            style.alignment = TextAnchor.UpperLeft;
            style.fontSize = Mathf.RoundToInt(17f * refScale);
            style.normal.textColor = Color.white;

            string first = engine.NoteCount > 0 ? engine.FirstHitTime.ToString("F3") : "n/a";
            string text = string.Format(
                "AdoFalling  {0:F0} FPS  dt={1:F4}s  step={2:F5}s\n" +
                "state={3}   lead={4:F3}s   raw={5:F3}s\n" +
                "notes={6}   holds={7}   onscreen={8}\n" +
                "judge err={9:F0}px (avg {10:F0}, n={11})   judgeY={12:F0}",
                _fps, engine.MeasuredDt, engine.MeasuredStep,
                engine.StateName, engine.LeadShift, engine.RawTime,
                engine.NoteCount, engine.HoldCount, engine.VisibleCount,
                engine.JudgeOffsetPx, engine.JudgeAvgPx, engine.JudgeSamples, judgeY);

            // Tall enough for four lines at any window size, so the game's own centre text
            // cannot clip the readout.
            float lineH = style.fontSize * 1.25f;
            var box = new Rect(10f * refScale, 10f * refScale,
                460f * refScale, lineH * 4f + 14f * refScale);
            Fill(box, new Color(0f, 0f, 0f, 0.62f));
            GUI.Label(new Rect(box.x + 8f * refScale, box.y + 5f * refScale, box.width - 12f, box.height), text, style);
        }
    }
}
