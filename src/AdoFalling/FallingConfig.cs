using BepInEx.Configuration;
using UnityEngine;

namespace AdoFalling
{
    /// <summary>All tunables exposed through the BepInEx config file.</summary>
    internal sealed class FallingConfig
    {
        public readonly ConfigEntry<KeyCode> ToggleKey;
        public readonly ConfigEntry<float> PollInterval;
        public readonly ConfigEntry<KeyCode> LoadKey;
        public readonly ConfigEntry<bool> DebugHud;
        public readonly ConfigEntry<bool> DemoMode;
        public readonly ConfigEntry<double> DemoBpm;
        public readonly ConfigEntry<bool> ForceDemo;
        public readonly ConfigEntry<bool> DemoHits;
        public readonly ConfigEntry<bool> AutoPlay;

        public readonly ConfigEntry<float> JudgeLineY;
        public readonly ConfigEntry<float> NoteSpeedPxPerSec;
        public readonly ConfigEntry<float> NoteHeight;
        public readonly ConfigEntry<float> LaneWidth;
        public readonly ConfigEntry<float> LaneGap;
        public readonly ConfigEntry<bool> MirrorDirection;
        public readonly ConfigEntry<LaneMapping> LaneMapping;
        public readonly ConfigEntry<double> FingeringWindowMs;
        public readonly ConfigEntry<string> FilePath;
        public readonly ConfigEntry<double> FileOffsetSeconds;
        public readonly ConfigEntry<bool> NoteRotation;
        public readonly ConfigEntry<bool> SkipAutoTiles;
        public readonly ConfigEntry<bool> MergeSameBeat;
        public readonly ConfigEntry<double> SameBeatEpsilon;
        public readonly ConfigEntry<bool> CountdownLead;
        public readonly ConfigEntry<double> LeadInSeconds;
        public readonly ConfigEntry<double> LeadFraction;
        public readonly ConfigEntry<double> LeadOffsetManual;
        public readonly ConfigEntry<double> HoldOffsetSeconds;
        public readonly ConfigEntry<double> HoldScale;
        public readonly ConfigEntry<bool> HoldShrinkWhileHeld;
        public readonly ConfigEntry<bool> SkipHoldReleaseTiles;
        public readonly ConfigEntry<KeyCode> HoldLaterKey;
        public readonly ConfigEntry<KeyCode> HoldEarlierKey;
        public readonly ConfigEntry<KeyCode> TuneKey;
        public readonly ConfigEntry<KeyCode> TuneEarlierKey;
        public readonly ConfigEntry<KeyCode> TuneLaterKey;
        public readonly ConfigEntry<KeyCode> ControlPanelKey;
        public readonly ConfigEntry<bool> ShowJudgeLine;
        public readonly ConfigEntry<bool> ShowNotes;
        public readonly ConfigEntry<bool> ShowHitEffects;
        public readonly ConfigEntry<bool> ShowJudgeText;
        public readonly ConfigEntry<bool> ShowLanes;

        public readonly ConfigEntry<float> LaneOpacity;
        public readonly ConfigEntry<float> LaneAlpha;
        public readonly ConfigEntry<float> JudgeOpacity;
        public readonly ConfigEntry<float> NoteOpacity;
        public readonly ConfigEntry<float> HoldBodyOpacity;
        public readonly ConfigEntry<Color> LaneColor;
        public readonly ConfigEntry<Color> LaneEdgeColor;
        public readonly ConfigEntry<Color> JudgeColor;
        public readonly ConfigEntry<Color> NoteColor;
        public readonly ConfigEntry<Color> NoteAltColor;
public readonly ConfigEntry<float> LaneFlashStrength;
        public readonly ConfigEntry<float> JudgeFlashStrength;

        public FallingConfig(ConfigFile f)
        {
            ToggleKey = f.Bind("General", "ToggleKey", KeyCode.F8,
                "Show/hide the falling overlay");
            LoadKey = f.Bind("General", "LoadKey", KeyCode.F,
                "Load/refresh the .adofai file in Chart/FilePath and switch the lane mapping " +
                "to Macro (ReADOFAIMacro's own fingering computed from the file)");
            PollInterval = f.Bind("General", "PollInterval", 0.05f,
                "Seconds between game-state polls (chart / time)");
            DebugHud = f.Bind("General", "DebugHud", false,
                "Log chart / timing diagnostics to the BepInEx console");
            DemoMode = f.Bind("General", "DemoMode", false,
                "Draw a synthetic chart on any screen - use it to preview or tune the " +
                "layout without loading a level");
            DemoBpm = f.Bind("General", "DemoBpm", 150.0,
                "Tempo used by DemoMode");
            ForceDemo = f.Bind("General", "ForceDemo", false,
                "Use the DemoMode chart even while a level is running (layout preview only)");
            DemoHits = f.Bind("General", "DemoHits", false,
                "In DemoMode, synthesise a hit whenever a note reaches the line, so the " +
                "judgement animation and hit sound can be previewed without playing");
            AutoPlay = f.Bind("General", "AutoPlay", false,
                "Let the game play the level by itself (the game's own RDC.auto switch). " +
                "Useful for watching the overlay without playing");

            JudgeLineY = f.Bind("Layout", "JudgeLineY", 0.18f,
                "Judgement line height as a fraction of screen height (0 = bottom)");
            NoteSpeedPxPerSec = f.Bind("Layout", "NoteSpeed", 620f,
                "Fall speed in pixels per second at 1080p reference height");
            NoteHeight = f.Bind("Layout", "NoteHeight", 26f,
                "Note height in pixels at 1080p reference height");
            LaneWidth = f.Bind("Layout", "LaneWidth", 110f, "Lane width in pixels");
            LaneGap = f.Bind("Layout", "LaneGap", 8f, "Gap between lanes in pixels");
            MirrorDirection = f.Bind("Layout", "RightToLeft", false,
                "Lay lanes out right-to-left instead of left-to-right");
            LaneMapping = f.Bind("Layout", "LaneMapping", AdoFalling.LaneMapping.Fingering,
                "How chart tiles pick a lane: Fingering = human fingering (rolls on fast notes, " +
                "two-finger alternation on slower ones), Position = from the tile's entry " +
                "direction, Cycle = tile number modulo 4, Sequential = one after another");
            FingeringWindowMs = f.Bind("Chart", "FingeringWindowMs", 180.0,
                "LaneMapping=Fingering: notes closer than this many milliseconds are played as " +
                "a roll across the lanes; slower notes alternate between lanes 1 and 2. Lower = " +
                "roll more, higher = tap more. Tune it live in the F5 panel's 排键 section");
            FilePath = f.Bind("Chart", "FilePath", "",
                "Path to the .adofai chart file used by LaneMapping=Macro: the plan is " +
                "ReADOFAIMacro's own fingering computed from the file (twirl/pause/SetSpeed " +
                "included). Load it with the load key (F by default) while in a level");
            FileOffsetSeconds = f.Bind("Chart", "FileOffsetSeconds", 0.0,
                "Fine trim for the file chart timeline, in seconds. Positive makes every note " +
                "later. Only used by the file mode (load key)");
            NoteRotation = f.Bind("Layout", "NoteRotation", false,
                "Tilt notes towards the judgement line (VSRG look)");
            SkipAutoTiles = f.Bind("Chart", "SkipAutoTiles", true,
                "Do not emit notes for tiles the game advances through by itself " +
                "(auto tiles and midspins). Turning this off makes those tiles appear as " +
                "extra presses that the original chart never asks for");
            MergeSameBeat = f.Bind("Chart", "MergeSameBeat", true,
                "Fold tiles that share one entry time into a single note. The planet lands on " +
                "them from one press, so separate notes would show as an unwanted double");
            SameBeatEpsilon = f.Bind("Chart", "SameBeatEpsilon", 0.003,
                "Maximum time difference (seconds) treated as the same beat");
            CountdownLead = f.Bind("Chart", "CountdownLead", true,
                "Shift the falling timeline back so the first note reaches the judgement line " +
                "after a full fall, instead of the player having to react the instant the " +
                "music starts. The shift happens before the level begins, so nothing is skipped");
            LeadInSeconds = f.Bind("Chart", "LeadInSeconds", 0.35,
                "Extra margin on top of a full fall before the first note is due, in seconds");
            LeadFraction = f.Bind("Chart", "LeadFraction", 0.0,
                "How much of the fall time is used as lead: 0 = notes cross the judgement line " +
                "exactly on the song clock (perfect alignment with the game's judgement), " +
                "1 = a full fall earlier. Raise it only if you want extra visual warning time");
            LeadOffsetManual = f.Bind("Chart", "LeadOffsetSeconds", 0.07,
                "Manual fine trim added on top of LeadFraction, in seconds. Positive makes notes " +
                "cross the judgement line EARLIER, negative makes them cross LATER. Tune it in " +
                "game with the F5 panel; the value is written back here when you save. " +
                "0.07 is the value calibrated on this installation (avg judge error -16px)");
            TuneKey = f.Bind("Tuning", "TuneKey", KeyCode.F6,
                "Log the current alignment values to the BepInEx console (the F5 panel is the " +
                "main UI now)");
            ControlPanelKey = f.Bind("Tuning", "ControlPanelKey", KeyCode.F5,
                "Open/close the Chinese control panel");
            ShowJudgeLine = f.Bind("View", "ShowJudgeLine", true,
                "Draw the judgement line");
            ShowNotes = f.Bind("View", "ShowNotes", true,
                "Draw the falling notes and hold bars");
            ShowLanes = f.Bind("View", "ShowLanes", true,
                "Draw the lane columns (bodies and outlines)");
            ShowHitEffects = f.Bind("View", "ShowHitEffects", true,
                "Draw the hit feedback: lane wash, judgement line flash and burst bars");
            ShowJudgeText = f.Bind("View", "ShowJudgeText", true,
                "Draw the judgement labels (PERFECT / EARLY / LATE ...)");
            TuneEarlierKey = f.Bind("Tuning", "TuneEarlierKey", KeyCode.F7,
                "Make notes cross the judgement line earlier");
            TuneLaterKey = f.Bind("Tuning", "TuneLaterKey", KeyCode.F9,
                "Make notes cross the judgement line later");

            // Hold notes get their own trim so they can be lined up independently of the
            // regular notes.
            HoldOffsetSeconds = f.Bind("Tuning", "HoldOffsetSeconds", 0.0,
                "Fine trim for HOLD notes only, in seconds. Positive shifts the hold bar EARLIER. " +
                "Bound to '-' (later) and '=' (earlier) in game. 0 = calibrated on this install");
            HoldScale = f.Bind("Tuning", "HoldScale", 1.0,
                "Hold bar length multiplier. 1 = the length that comes from the chart");
            HoldShrinkWhileHeld = f.Bind("Tuning", "HoldShrinkWhileHeld", true,
                "true (default): the hold bar falls head-first like a normal note, then while you " +
                "hold it its judged end stays on the judgement line and the bar shrinks until the " +
                "release. false: the bar is removed the moment it is pressed");
            SkipHoldReleaseTiles = f.Bind("Chart", "SkipHoldReleaseTiles", true,
                "Do not emit a note for the tile that follows a hold: the game judges the hold's " +
                "release on the hold tile itself, so that tile would show the same judgement twice");
            HoldLaterKey = f.Bind("Tuning", "HoldLaterKey", KeyCode.Minus,
                "Shift hold bars later");
            HoldEarlierKey = f.Bind("Tuning", "HoldEarlierKey", KeyCode.Equals,
                "Shift hold bars earlier");

            LaneOpacity = f.Bind("Style", "LaneOpacity", 0.18f,
                "Lane body alpha (0 = invisible, 1 = opaque)");
            LaneAlpha = f.Bind("Style", "LaneEdgeAlpha", 0.7f,
                "Lane outline alpha - this is what makes the tracks read clearly");
            JudgeOpacity = f.Bind("Style", "JudgeLineOpacity", 1f,
                "Judgement line alpha");
            NoteOpacity = f.Bind("Style", "NoteOpacity", 1f, "Falling note alpha");
            HoldBodyOpacity = f.Bind("Style", "HoldBodyOpacity", 0.45f,
                "Alpha of the hold tail, relative to NoteOpacity");
            LaneColor = f.Bind("Style", "LaneColor", new Color(0.05f, 0.06f, 0.09f, 1f),
                "Lane body colour (alpha comes from LaneOpacity)");
            LaneEdgeColor = f.Bind("Style", "LaneEdgeColor", new Color(0.35f, 0.55f, 0.95f, 1f),
                "Lane outline colour (alpha comes from LaneEdgeAlpha)");
            JudgeColor = f.Bind("Style", "JudgeLineColor", new Color(1f, 1f, 1f, 1f),
                "Judgement line colour");
            NoteColor = f.Bind("Style", "NoteColor", new Color(1f, 0.45f, 0.2f, 1f),
                "Primary note colour");
            NoteAltColor = f.Bind("Style", "NoteAltColor", new Color(0.4f, 0.7f, 1f, 1f),
                "Secondary note colour (alternates per tile)");

            LaneFlashStrength = f.Bind("Feedback", "LaneFlashStrength", 0.7f,
                "How bright the whole lane flashes on a hit (0 = off, 1 = full wash)");
            JudgeFlashStrength = f.Bind("Feedback", "JudgeFlashStrength", 0.85f,
                "How bright the judgement line flashes on a hit (0 = off, 1 = full white)");
        }
    }
}
