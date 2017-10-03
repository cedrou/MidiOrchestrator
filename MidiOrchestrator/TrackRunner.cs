using MidiSharp;
using MidiSharp.Events;
using MidiSharp.Events.Voice.Note;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Windows.Devices.Midi;

namespace MidiOrchestrator
{
    class TrackRunner
    {
        //MidiSequence sequence;
        //MidiTrack track;
        MidiClock clock;
        IMidiOutPort port;
        List<Tuple<Int64, MidiEvent>> timeline;
        //Dictionary<Int64, MidiEvent> timeline;

        public TrackRunner(MidiSequence sequence, MidiTrack track, MidiClock clock, IMidiOutPort port)
        {
            //this.sequence = sequence;
            //this.track = track;
            this.clock = clock;
            this.port = port;

            Int64 tick = 0;
            timeline = new List<Tuple<Int64, MidiEvent>>();
            foreach (var e in track.Events)
            {
                tick += e.DeltaTime;
                timeline.Add(new Tuple<Int64, MidiEvent>(tick, e));
            }

            this.clock.Tick += Clock_Tick;
        }

        private void Clock_Tick(MidiClock clk, EventArgs args)
        {
            foreach (var e in timeline.Where(i => i.Item1 == clk.Ticks).Select(i => i.Item2))
            {
                switch (e)
                {
                case OnNoteVoiceMidiEvent ev:
                    port.SendMessage(new MidiNoteOnMessage(ev.Channel, ev.Note, ev.Velocity));
                    break;
                case OffNoteVoiceMidiEvent ev:
                    port.SendMessage(new MidiNoteOnMessage(ev.Channel, ev.Note, ev.Velocity));
                    break;
                }
            }
        }
    }
}
