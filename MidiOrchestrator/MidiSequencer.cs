using MidiSharp;
using MidiSharp.Events.Meta;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;

using System.Threading.Tasks;
using Windows.Devices.Midi;
using Windows.Storage;
using Windows.System.Threading;

namespace MidiOrchestrator
{
    public class MidiSequencer : INotifyPropertyChanged
    {
        public UInt32 Tempo { get => (UInt32)(60_000_000 * beatsPerQuarter / µsPerQuarter); }

        public Tuple<UInt32, UInt32> TimeSignature { get => new Tuple<UInt32, UInt32>(beatsPerMeasure, (UInt32)(beatsPerQuarter * 4)); }

        public UInt32 Ticks { get; private set; }

        public UInt32 Measure { get => 1 + Ticks / ticksPerMeasure; }

        public UInt32 BeatInMeasure { get => 1 + (Ticks % ticksPerMeasure) / ticksPerBeat; }

        public IEnumerable<TrackRunner> VoiceTracks { get => tracks.Where(t => t.IsVoiceTrack); }

        public String MarkerText { get; private set; }



        // Set by MIDI file or user
        private UInt32 ticksPerQuarter;
        private UInt32 µsPerQuarter;
        private UInt32 beatsPerMeasure;
        private Double beatsPerQuarter;

        private UInt32 next_µsPerQuarter;
        private UInt32 next_beatsPerMeasure;
        private Double next_beatsPerQuarter;

        // Internal cached values
        private UInt32 ticksPerMeasure;
        private UInt32 ticksPerBeat;

        private UInt32 µsPerTick;

        private MidiSynthesizer midiSynth;
        private MidiSequence sequence;
        private string sequenceDump;
        private bool isPlaying = false;

        private List<TrackRunner> tracks;
        private List<UInt32> nextEventTicks;

        public event PropertyChangedEventHandler PropertyChanged;
        void NotifyPropertyChanged(string property)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
        }

        public MidiSequencer(MidiSynthesizer midiSynth)
        {
            this.midiSynth = midiSynth;

            next_µsPerQuarter = 500_000;
            next_beatsPerMeasure = 4;
            next_beatsPerQuarter = 1.0;
            ticksPerQuarter = 96;

            Ticks = 0;

            UpdateInternals();
        }

        static IEnumerable<int> Zeros(IList<UInt32> list)
        {
            var zeros = new List<int>();
            for (var i = 0; i < list.Count; i++)
            {
                if (list[i] == 0)
                    zeros.Add(i);
            }
            return zeros;
        }

        public async Task OpenAsync(StorageFile file)
        {
            sequence = null;
            try
            {
                using (var inputStream = await file.OpenReadAsync())
                {
                    sequence = MidiSequence.Open(inputStream.AsStreamForRead());
                }
            } catch (Exception exc) { Debug.WriteLine($"Failed to read MIDI sequence: {exc.Message}"); return; }

            sequenceDump = sequence.ToString();

            next_µsPerQuarter = 500_000;
            next_beatsPerMeasure = 4;
            next_beatsPerQuarter = 1;
            ticksPerQuarter = 96;

            Ticks = 0;

            if (sequence.DivisionType == DivisionType.TicksPerBeat)
                ticksPerQuarter = (UInt32)sequence.TicksPerBeatOrFrame;


            // Create the track list
            tracks = new List<TrackRunner>();
            nextEventTicks = new List<UInt32>();

            for (var trackIndex = 0; trackIndex < sequence.Tracks.Count; trackIndex++)
            {
                tracks.Add(new TrackRunner(sequence.Tracks[trackIndex], this));
                nextEventTicks.Add((UInt32)sequence.Tracks[trackIndex].Events[0].DeltaTime);
            }

            foreach (var i in Zeros(nextEventTicks))
            {
                nextEventTicks[i] = tracks[i].Run(false);
            }

            UpdateInternals();

            // Compute duration
            var maxEventTick = VoiceTracks.Select(t => t.Timeline.Last()).Max();

            var NbMeasures = maxEventTick / ticksPerMeasure;
            var NbBeats = maxEventTick / ticksPerBeat;

            var deltaticks = 0L;
            var current_µsPerTick = 500_000 / ticksPerQuarter;
            var totalTimeInµs = 0L;
            var ticksToEnd = maxEventTick;
            foreach (var e in sequence.Tracks[0].Events)
            {
                deltaticks += e.DeltaTime;
                ticksToEnd -= e.DeltaTime;
                if (e is TempoMetaMidiEvent tempoEvent)
                {
                    totalTimeInµs += current_µsPerTick * deltaticks;
                    deltaticks = 0;

                    current_µsPerTick = (uint)tempoEvent.Value / ticksPerQuarter;
                }
            }

            totalTimeInµs += current_µsPerTick * ticksToEnd;

            var totalTime = TimeSpan.FromMilliseconds(totalTimeInµs / 1_000.0);
        }

        private void UpdateInternals()
        {
            if (next_µsPerQuarter > 0) µsPerQuarter = next_µsPerQuarter;
            if (next_beatsPerMeasure > 0) beatsPerMeasure = next_beatsPerMeasure;
            if (next_beatsPerQuarter > 0) beatsPerQuarter = next_beatsPerQuarter;

            var quartersPerMeasure = beatsPerMeasure / beatsPerQuarter;

            ticksPerMeasure = (UInt32)(ticksPerQuarter * quartersPerMeasure);
            ticksPerBeat = ticksPerMeasure / beatsPerMeasure;

            µsPerTick = µsPerQuarter / ticksPerQuarter;

            NotifyPropertyChanged("Tempo");
            NotifyPropertyChanged("TimeSignature");
        }

        public void Seek(UInt32 measure, UInt32 beatInMeasure)
        {
            Ticks = (measure-1) * ticksPerMeasure + (beatInMeasure-1) * ticksPerBeat;
        }

        public void SetTempo(UInt32 bpm)
        {
            next_µsPerQuarter = (UInt32)(60_000_000 * beatsPerQuarter / bpm);
        }

        public void SetQuarterDuration(UInt32 µsPerQuarter)
        {
            next_µsPerQuarter = µsPerQuarter;
        }

        public void SetTimeSignature(UInt32 num, UInt32 den)
        {
            next_beatsPerMeasure = num;
            next_beatsPerQuarter = den / 4.0;
        }

        public void SetMarkerText(string text)
        {
            MarkerText = text;
            NotifyPropertyChanged("MarkerText");
        }

        private Task loopTask = null;
        public void Start()
        {
            loopTask = ClockLoopAsync();
        }

        public async Task PauseAsync()
        {
            isPlaying = false;
            await loopTask;
            foreach (var t in VoiceTracks)
            {
                t.StopAllNotes();
            }
        }

        public async Task StopAsync()
        {
            await PauseAsync();
            Ticks = 0;
        }

        async Task ClockLoopAsync()
        {
            isPlaying = true;

            var sw = Stopwatch.StartNew();

            var lastTickTime = sw.ElapsedMilliseconds;

            while (isPlaying)
            {
                var minDeltaTick = nextEventTicks.Min();
                //Debug.Assert(minDeltaTick > 0);

                var msToNextEvent = minDeltaTick * µsPerTick / 1000 - (sw.ElapsedMilliseconds - lastTickTime);
                if (msToNextEvent > 0)
                    await Task.Delay((Int32)msToNextEvent);

                lastTickTime = sw.ElapsedMilliseconds;

                Ticks += minDeltaTick;

                for (var i = 0; i < nextEventTicks.Count; i++)
                {
                    if (nextEventTicks[i] < UInt32.MaxValue)
                        nextEventTicks[i] -= minDeltaTick;
                }

                foreach (var i in Zeros(nextEventTicks))
                {
                    nextEventTicks[i] = tracks[i].Run(true);
                }


                bool doUpdateInternals = false;

                if (next_µsPerQuarter > 0)
                {
                    µsPerQuarter = next_µsPerQuarter;
                    next_µsPerQuarter = 0;
                    doUpdateInternals = true;
                }

                if (next_beatsPerMeasure > 0)
                {
                    beatsPerMeasure = next_beatsPerMeasure;
                    beatsPerQuarter = next_beatsPerQuarter;

                    next_beatsPerMeasure = 0;
                    next_beatsPerQuarter = 0;

                    doUpdateInternals = true;
                }
                if (doUpdateInternals)
                    UpdateInternals();

                //Tick?.Invoke(this, EventArgs.Empty);

                //while (sw.ElapsedMilliseconds < entryTime + µsPerTick)
                //    await Task.Yield();
            }
        }

        public void SendMessage(IMidiMessage msg)
        {
            midiSynth.SendMessage(msg);
        }
    }
}
