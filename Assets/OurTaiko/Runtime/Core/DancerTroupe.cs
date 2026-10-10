using System;
using System.Collections.Generic;

namespace OurTaiko
{
    // One dancer's rig timeline, in rig frames: the hop in, the looped dance, the way out.
    public readonly struct DancerRig
    {
        public readonly int Frames, Dance, Out;
        public DancerRig(int frames, int dance, int leave) { Frames = frames; Dance = dance; Out = leave; }
    }

    // Nijiiro bg_objects/dancer.lua ArcadeDancerGroup: the whole troupe shares one playhead that
    // advances BeatFrames per beat, and every dancer's frame is a pure function of it, so dancers
    // in the same state are on the same frame. Slots are in spawn order: centre, left, right,
    // far left, far right.
    public sealed class DancerTroupe
    {
        public const int BeatFrames = 40, Phrase = 80, MinDancers = 3;

        enum State { Idle, In, Dance, Out }
        struct Dancer { public State State; public double Enter, Join, Exit; }

        readonly DancerRig[] rigs;
        readonly Dancer[] dancers;
        double now = double.NaN;

        public double Frame { get; private set; }
        public int Active { get; private set; }
        public int Slots => rigs.Length;

        public DancerTroupe(IReadOnlyList<DancerRig> slots)
        {
            rigs = new DancerRig[slots.Count];
            for (int i = 0; i < rigs.Length; i++) rigs[i] = slots[i];
            dancers = new Dancer[rigs.Length];
            // The playhead is still 0, so the first three hop in together.
            SetCount(MinDancers);
        }

        // Background:handle_dancer_count with the engine's clear_progress of 1: the fourth dancer
        // from half a gauge, the fifth with the clear state.
        public static int CountFor(double gauge, bool clear)
            => clear ? 5 : Math.Max(MinDancers, Math.Min(5, (int)Math.Floor(2 * gauge + 3)));

        // Time never runs the playhead backwards; a tempo change retimes the whole troupe at once.
        public void Advance(double time, double bpm)
        {
            double elapsed = double.IsNaN(now) ? 0 : Math.Max(0, time - now);
            now = time;
            bpm = Math.Abs(bpm);
            Frame += elapsed * (bpm > 0 ? bpm : 120) / 60 * BeatFrames;
            for (int i = 0; i < dancers.Length; i++)
            {
                ref var dancer = ref dancers[i];
                if (dancer.State == State.In && Frame >= dancer.Join) dancer.State = State.Dance;
                else if (dancer.State == State.Out && Frame >= dancer.Exit + (rigs[i].Frames - rigs[i].Out)) dancer.State = State.Idle;
            }
        }

        // Grows and shrinks slot by slot, from the centre outwards.
        public void SetCount(int count)
        {
            count = Math.Max(Math.Min(MinDancers, rigs.Length), Math.Min(rigs.Length, count));
            while (Active < count) Enter(Active++);
            while (Active > count) Leave(--Active);
        }

        // The rig frame this slot shows, or -1 while it is off stage.
        public int FrameOf(int slot)
        {
            var dancer = dancers[slot];
            var rig = rigs[slot];
            switch (dancer.State)
            {
                case State.Idle: return -1;
                // Earned, still waiting for the phrase.
                case State.In: return Frame < dancer.Enter ? -1 : (int)Math.Floor(Frame - dancer.Enter);
                case State.Out when Frame >= dancer.Exit:
                    int frame = rig.Out + (int)Math.Floor(Frame - dancer.Exit);
                    return frame >= rig.Frames ? -1 : frame;
                default:
                    double looped = (Frame - rig.Dance) % (rig.Out - rig.Dance);
                    return rig.Dance + (int)Math.Floor(looped);
            }
        }

        void Enter(int slot)
        {
            ref var dancer = ref dancers[slot];
            dancer.Join = PhraseAtOrAfter(Frame + Phrase, rigs[slot].Dance);
            dancer.Enter = dancer.Join - Phrase;
            dancer.State = State.In;
        }

        void Leave(int slot)
        {
            ref var dancer = ref dancers[slot];
            if (dancer.State == State.Idle || dancer.State == State.Out) return;
            // Earned and lost again before it ever landed: it simply never arrives.
            if (dancer.State == State.In && Frame < dancer.Join) { dancer.State = State.Idle; return; }
            dancer.Exit = PhraseAtOrAfter(Frame, rigs[slot].Dance);
            dancer.State = State.Out;
        }

        // Smallest frame at or after this one that is a whole number of phrases from the dance start.
        static double PhraseAtOrAfter(double frame, int dance)
            => dance + Math.Ceiling((frame - dance) / Phrase) * Phrase;
    }
}
