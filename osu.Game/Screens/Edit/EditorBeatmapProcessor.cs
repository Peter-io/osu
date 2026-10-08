// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using osu.Game.Beatmaps;
using osu.Game.Beatmaps.Timing;
using osu.Game.Rulesets;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Objects.Types;

namespace osu.Game.Screens.Edit
{
    public class EditorBeatmapProcessor : IBeatmapProcessor
    {
        public EditorBeatmap Beatmap { get; }

        IBeatmap IBeatmapProcessor.Beatmap => Beatmap;

        private readonly IBeatmapProcessor? rulesetBeatmapProcessor;

        /// <summary>
        /// Kept for the purposes of reducing redundant regeneration of automatic breaks.
        /// </summary>
        private HashSet<(double, double)> objectDurationCache = new HashSet<(double, double)>();

        public EditorBeatmapProcessor(EditorBeatmap beatmap, Ruleset ruleset)
        {
            Beatmap = beatmap;

            Beatmap.HitObjectAdded += updateBreak;
            Beatmap.HitObjectRemoved += updateBreak;
            Beatmap.HitObjectShifted += updateBreak;

            rulesetBeatmapProcessor = ruleset.CreateBeatmapProcessor(beatmap);
        }

        public void PreProcess()
        {
            rulesetBeatmapProcessor?.PreProcess();
        }

        public void PostProcess()
        {
            rulesetBeatmapProcessor?.PostProcess();

            ensureNewComboAfterBreaks();
        }

        private void updateBreak(HitObject currentHitObject)
        {
            var previousObject = Beatmap.HitObjects.LastOrDefault(h => h.GetEndTime() < currentHitObject.StartTime) ?? currentHitObject;
            var nextObject = Beatmap.HitObjects.FirstOrDefault(h => h.StartTime > currentHitObject.StartTime) ?? currentHitObject;

            if (Beatmap.FindIndex(currentHitObject) >= 0)
            {
                insertBreakBetweenObjects(previousObject, currentHitObject);
                insertBreakBetweenObjects(currentHitObject, nextObject);
            }
            else insertBreakBetweenObjects(previousObject, nextObject);
        }

        private void insertBreakBetweenObjects(HitObject Start, HitObject End)
        {
            // Keep track of the maximum end time encountered thus far.
            // This handles cases like osu!mania's hold notes, which could have concurrent other objects after their start time.
            // Note that we're relying on the implicit assumption that objects are sorted by start time,
            // which is why similar tracking is not done for start time.
            double currentMaxEndTime = Math.Max(double.MinValue, Start.GetEndTime());

            if (Beatmap.Breaks.Any(b => b.Intersects(new BreakPeriod(Start.GetEndTime(), End.StartTime))))
                Beatmap.Breaks.RemoveAll(b => b.Intersects(new BreakPeriod(Start.GetEndTime(), End.StartTime)));

            if ((End.StartTime - currentMaxEndTime) < BreakPeriod.MIN_GAP_DURATION)
                return;

            double breakStartTime = currentMaxEndTime + BreakPeriod.GAP_BEFORE_BREAK;

            double breakEndTime = End.StartTime;

            if (End is IHasTimePreempt hasTimePreempt)
                breakEndTime -= hasTimePreempt.TimePreempt;
            else
                breakEndTime -= Math.Max(BreakPeriod.GAP_AFTER_BREAK, Beatmap.ControlPointInfo.TimingPointAt(End.StartTime).BeatLength * 2);

            if (breakEndTime - breakStartTime < BreakPeriod.MIN_BREAK_DURATION)
                return;

            var breakPeriod = new BreakPeriod(breakStartTime, breakEndTime);

            Beatmap.Breaks.Add(breakPeriod);
        }

        private void ensureNewComboAfterBreaks()
        {
            var breakEnds = Beatmap.Breaks.Select(b => b.EndTime).OrderBy(t => t).ToList();

            if (breakEnds.Count == 0)
                return;

            int currentBreak = 0;

            IHasComboInformation? lastObj = null;
            bool comboInformationUpdateRequired = false;

            foreach (var hitObject in Beatmap.HitObjects)
            {
                if (hitObject is not IHasComboInformation hasCombo)
                    continue;

                if (currentBreak < breakEnds.Count && hitObject.StartTime >= breakEnds[currentBreak])
                {
                    if (!hasCombo.NewCombo)
                    {
                        hasCombo.NewCombo = true;
                        comboInformationUpdateRequired = true;
                    }

                    currentBreak += 1;
                }

                if (comboInformationUpdateRequired)
                    hasCombo.UpdateComboInformation(lastObj);

                lastObj = hasCombo;
            }
        }
    }
}
