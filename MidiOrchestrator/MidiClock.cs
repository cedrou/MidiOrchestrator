using MidiSharp;
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
        public UInt32 Tempo { get => 60_000_000 * beatsPerQuarter / µsPerQuarter; }

        public Tuple<UInt32, UInt32> TimeSignature { get => new Tuple<UInt32, UInt32>(beatsPerMeasure, beatsPerQuarter * 4); }

        public UInt32 Ticks { get; private set; }

        public UInt32 Measure { get => 1 + Ticks / ticksPerMeasure; }

        public UInt32 BeatInMeasure { get => 1 + (Ticks % ticksPerMeasure) / ticksPerBeat; }


        // Set by MIDI file or user
        private UInt32 ticksPerQuarter;
        private UInt32 µsPerQuarter;
        private UInt32 beatsPerMeasure;
        private UInt32 beatsPerQuarter;

        private UInt32 next_µsPerQuarter;
        private UInt32 next_beatsPerMeasure;
        private UInt32 next_beatsPerQuarter;

        // Internal cached values
        private UInt32 ticksPerMeasure;
        private UInt32 ticksPerBeat;

        private UInt32 µsPerTick;

        private MidiSynthesizer midiSynth;
        private MidiSequence sequence;
        private string sequenceDump;
        private bool isPlaying = false;

        private List<TrackRunner> tracks;
        private List<UInt32> deltaTicks;
        private IEnumerable<TrackControl> trackControls;

        public event PropertyChangedEventHandler PropertyChanged;

        public MidiSequencer(MidiSynthesizer midiSynth, IEnumerable<TrackControl> trackControls)
        {
            this.midiSynth = midiSynth;
            this.trackControls = trackControls;

            µsPerQuarter = 500_000;
            beatsPerMeasure = 4;
            beatsPerQuarter = 1;
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

            µsPerQuarter = 500_000;
            beatsPerMeasure = 4;
            beatsPerQuarter = 1;
            ticksPerQuarter = 96;

            Ticks = 0;

            if (sequence.DivisionType == DivisionType.TicksPerBeat)
                ticksPerQuarter = (UInt32)sequence.TicksPerBeatOrFrame;

            UpdateInternals();


            // Create the track list
            tracks = new List<TrackRunner>();
            deltaTicks = new List<UInt32>();

            for (var trackIndex = 0; trackIndex < sequence.Tracks.Count; trackIndex++)
            {
                tracks.Add(new TrackRunner(sequence.Tracks[trackIndex], this, trackControls.ElementAtOrDefault(trackIndex)));
                deltaTicks.Add((UInt32)sequence.Tracks[trackIndex].Events[0].DeltaTime);
            }

            foreach (var i in Zeros(deltaTicks))
            {
                deltaTicks[i] = tracks[i].Run();
            }
        }

        private void UpdateInternals()
        {
            var quartersPerMeasure = (Single)beatsPerMeasure / beatsPerQuarter;

            ticksPerMeasure = (UInt32)(ticksPerQuarter * quartersPerMeasure);
            ticksPerBeat = ticksPerMeasure / beatsPerMeasure;

            µsPerTick = µsPerQuarter / ticksPerQuarter;

            if (PropertyChanged != null)
            {
                PropertyChanged(this, new PropertyChangedEventArgs("Tempo"));
                PropertyChanged(this, new PropertyChangedEventArgs("TimeSignature"));
            }
        }

        public void Seek(UInt32 measure, UInt32 beatInMeasure)
        {
            Ticks = (measure-1) * ticksPerMeasure + (beatInMeasure-1) * ticksPerBeat;
        }

        public void SetTempo(UInt32 bpm)
        {
            next_µsPerQuarter = 60_000_000 * beatsPerQuarter / bpm;
        }

        public void SetQuarterDuration(UInt32 µsPerQuarter)
        {
            next_µsPerQuarter = µsPerQuarter;
        }

        public void SetTimeSignature(UInt32 num, UInt32 den)
        {
            next_beatsPerMeasure = num;
            next_beatsPerQuarter = den / 4;
        }

        public void Start()
        {
            var _ = ClockLoopAsync();
        }

        public void Pause()
        {
            isPlaying = false;
        }

        public void Stop()
        {
            isPlaying = false;
            Ticks = 0;
        }

        async Task ClockLoopAsync()
        {
            isPlaying = true;

            var sw = Stopwatch.StartNew();

            var lastTickTime = sw.ElapsedMilliseconds;

            while (isPlaying)
            {
                var minDeltaTick = deltaTicks.Min();
                Debug.Assert(minDeltaTick > 0);

                var msToNextEvent = minDeltaTick * µsPerTick / 1000 - (sw.ElapsedMilliseconds - lastTickTime);
                if (msToNextEvent > 0)
                    await Task.Delay((Int32)msToNextEvent);

                lastTickTime = sw.ElapsedMilliseconds;

                Ticks += minDeltaTick;

                for (var i = 0; i < deltaTicks.Count; i++)
                {
                    if (deltaTicks[i] < UInt32.MaxValue)
                        deltaTicks[i] -= minDeltaTick;
                }

                foreach (var i in Zeros(deltaTicks))
                {
                    deltaTicks[i] = tracks[i].Run();
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
