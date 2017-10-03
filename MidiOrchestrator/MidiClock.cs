using System;
using System.Diagnostics;
using Windows.System.Threading;

namespace MidiOrchestrator
{
    class MidiClock
    {
        public delegate void TickHandler(MidiClock m, EventArgs e);
        public event TickHandler Tick;


        // Tempo in beat per minute. Default 120 bpm
        public UInt32 Tempo {
            get { return _Tempo; }
            set { _Tempo = value; UpdateInternals(); }
        }
        UInt32 _Tempo = 120;


        // Time signature. Default 4/4
        public Tuple<UInt32, UInt32> TimeSignature {
            get { return _TimeSignature; }
            set { _TimeSignature = value; UpdateInternals(); }
        }
        Tuple<UInt32, UInt32> _TimeSignature = new Tuple<UInt32, UInt32>(4, 4);


        // Number of ticks per quarter note. Default 96
        public UInt32 TicksPerQuarter {
            get { return _TicksPerQuarter; }
            set { _TicksPerQuarter = value; UpdateInternals(); }
        }
        UInt32 _TicksPerQuarter = 96;


        public UInt32 Ticks {
            get { return _Ticks; }
            private set { _Ticks = value; }
        }
        UInt32 _Ticks = 0;

        public UInt32 Measure { get => (Ticks + ticksPerMeasure) / ticksPerMeasure; }

        public UInt32 BeatInMeasure { get => (((Ticks + ticksPerMeasure) % ticksPerMeasure) + ticksPerBeat) / ticksPerBeat; }


        private UInt32 beatsPerMeasure;
        private UInt32 beatsPerQuarter;
        private Single quartersPerMeasure;

        private UInt32 ticksPerMeasure;
        private UInt32 ticksPerBeat;

        private Double tickDurationInMs;

        private ThreadPoolTimer timer;


        public MidiClock()
        {
            UpdateInternals();
        }

        private void UpdateInternals()
        {
            beatsPerMeasure = _TimeSignature.Item1;
            beatsPerQuarter = _TimeSignature.Item2 / 4;
            quartersPerMeasure = (Single)beatsPerMeasure / beatsPerQuarter;
            // 4/4 : 4 beats per measure, 4/4=1 beat per quarter -> 4/1=4 quarters per measure
            // 6/8 : 6 beats per measure, 8/4=2 beats per quarter -> 6/2=3 quarters per measure
            // 2/2 : 2 beats per measure, 2/4=0.5 beat per quarter -> 2/0.5=4 quarters per measure

            ticksPerMeasure = (UInt32)(_TicksPerQuarter * quartersPerMeasure);
            ticksPerBeat = ticksPerMeasure / beatsPerMeasure;

            tickDurationInMs = 60000.0 / (_Tempo * _TicksPerQuarter);
        }

        public void Seek(UInt32 measure, UInt32 beatInMeasure)
        {
            Ticks = measure * ticksPerMeasure + beatInMeasure * ticksPerBeat;
        }

        public void Start()
        {
            timer = ThreadPoolTimer.CreatePeriodicTimer(t => {
                Tick?.Invoke(this, EventArgs.Empty);
                Ticks++;
            }, TimeSpan.FromMilliseconds(tickDurationInMs), t => {
                Debug.Write("Destroyed");
            });
        }

        public void Pause()
        {
            timer.Cancel();
        }

        public void Stop()
        {
            timer.Cancel();
            Ticks = 0;
        }
    }
}
