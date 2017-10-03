using MidiSharp;
using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Windows.System.Threading;

namespace MidiOrchestrator
{
    class MidiClock
    {
        public delegate void TickHandler(MidiClock m, EventArgs e);
        public event TickHandler Tick;


        // Tempo in beat per minute. Default 120 bpm
        public UInt32 Tempo { get; private set; }
        UInt32 nextTempo = 0;
        UInt32 nextQuarterDurationInUs = 0;


        // Time signature. Default 4/4
        public Tuple<UInt32, UInt32> TimeSignature { get; private set; }
        Tuple<UInt32, UInt32> nextTimeSignature = null;

        public UInt32 Ticks { get; private set; }

        public UInt32 Measure { get => (Ticks + ticksPerMeasure) / ticksPerMeasure; }

        public UInt32 BeatInMeasure { get => (((Ticks + ticksPerMeasure) % ticksPerMeasure) + ticksPerBeat) / ticksPerBeat; }


        private UInt32 beatsPerMeasure;
        private UInt32 beatsPerQuarter;
        private Single quartersPerMeasure;

        private UInt32 ticksPerQuarter;
        private UInt32 ticksPerMeasure;
        private UInt32 ticksPerBeat;

        private UInt32 tickDurationInMs;

        //private ThreadPoolTimer timer;


        public MidiClock(MidiSequence sequence)
        {
            Tempo = 120;
            TimeSignature = new Tuple<uint, uint>(4, 4);
            Ticks = 0;

            if (sequence.DivisionType == DivisionType.TicksPerBeat)
                ticksPerQuarter = (UInt32)sequence.TicksPerBeatOrFrame;
            else
                ticksPerQuarter = 96;

            UpdateInternals();
        }

        private void UpdateInternals()
        {
            beatsPerMeasure = TimeSignature.Item1;
            beatsPerQuarter = TimeSignature.Item2 / 4;
            quartersPerMeasure = (Single)beatsPerMeasure / beatsPerQuarter;
            // 4/4 : 4 beats per measure, 4/4=1 beat per quarter -> 4/1=4 quarters per measure
            // 6/8 : 6 beats per measure, 8/4=2 beats per quarter -> 6/2=3 quarters per measure
            // 2/2 : 2 beats per measure, 2/4=0.5 beat per quarter -> 2/0.5=4 quarters per measure

            ticksPerMeasure = (UInt32)(ticksPerQuarter * quartersPerMeasure);
            ticksPerBeat = ticksPerMeasure / beatsPerMeasure;

            //var newTickDurationInMs = Math.Max(1, 60000 / (Tempo * ticksPerQuarter));
            var newTickDurationInMs = Math.Max(1, nextQuarterDurationInUs /*us/q*/ / (1000 * ticksPerQuarter /*tk/q*/));
            if (newTickDurationInMs != tickDurationInMs)
            {
                var bRestart = isRunning;

                Pause();
                tickDurationInMs = newTickDurationInMs;
                if (bRestart) Start();
            }
        }

        public void Seek(UInt32 measure, UInt32 beatInMeasure)
        {
            Ticks = measure * ticksPerMeasure + beatInMeasure * ticksPerBeat;
        }

        public void SetTempo(UInt32 quarterDurationInUs)
        {
            nextQuarterDurationInUs = quarterDurationInUs;
        }

        public void SetTimeSignature(UInt32 num, UInt32 den)
        {
            nextTimeSignature = new Tuple<uint, uint>(num, den);
        }

        public void Start()
        {
            isRunning = true;
            //timer = ThreadPoolTimer.CreatePeriodicTimer(t => {
            //    Ticks++;
            //    Tick?.Invoke(this, EventArgs.Empty);

            //    bool doUpdateInternals = false;
            //    if (nextTempo > 0)
            //    {
            //        Tempo = nextTempo;
            //        nextTempo = 0;
            //        doUpdateInternals = true;
            //    }
            //    if (nextTimeSignature != null)
            //    {
            //        TimeSignature = nextTimeSignature;
            //        nextTimeSignature = null;
            //        doUpdateInternals = true;
            //    }
            //    if (doUpdateInternals)
            //        UpdateInternals();

            //}, TimeSpan.FromMilliseconds(tickDurationInMs), t => {
            //    Debug.Write("Destroyed");
            //});
            var _ = ClockLoopAsync();
        }

        bool isRunning = false;

        async Task ClockLoopAsync()
        {
            var sw = Stopwatch.StartNew();

            while (isRunning)
            {
                var entryTime = sw.ElapsedMilliseconds;

                Ticks++;

                Tick?.Invoke(this, EventArgs.Empty);

                bool doUpdateInternals = false;
                if (nextTempo > 0)
                {
                    Tempo = nextTempo;
                    nextTempo = 0;
                    doUpdateInternals = true;
                }
                if (nextTimeSignature != null)
                {
                    TimeSignature = nextTimeSignature;
                    nextTimeSignature = null;
                    doUpdateInternals = true;
                }
                if (doUpdateInternals)
                    UpdateInternals();

                while (sw.ElapsedMilliseconds < entryTime + tickDurationInMs)
                    await Task.Yield();
            }
        }

        public void Pause()
        {
            isRunning = false;
            //timer?.Cancel();
            //timer = null;
        }

        public void Stop()
        {
            isRunning = false;
            //timer?.Cancel();
            //timer = null;
            Ticks = 0;
        }
    }
}
