using MidiSharp;
using MidiSharp.Events;
using MidiSharp.Events.Meta;
using MidiSharp.Events.Meta.Text;
using MidiSharp.Events.Voice;
using MidiSharp.Events.Voice.Note;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using System.Threading.Tasks;
using Windows.Devices.Midi;
using Windows.Storage.Streams;

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

        public IEnumerable<Tuple<Int64, MidiEvent>> Timeline => timeline;

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

            ProcessEventsAt(0);
        }

        private void Clock_Tick(MidiClock clk, EventArgs args)
        {
            ProcessEventsAt(clk.Ticks);
        }

        private void ProcessEventsAt(UInt32 ticks)
        {
            foreach (var e in timeline.Where(i => i.Item1 == ticks).Select(i => i.Item2))
            {
                IMidiMessage msg = null;

                switch (e)
                {
                // MIDI messages
                case OffNoteVoiceMidiEvent ev:          msg = new MidiNoteOnMessage(ev.Channel, ev.Note, ev.Velocity); break;
                case OnNoteVoiceMidiEvent ev:           msg = new MidiNoteOnMessage(ev.Channel, ev.Note, ev.Velocity); break;
                case AftertouchNoteVoiceMidiEvent ev:   msg = new MidiPolyphonicKeyPressureMessage(ev.Channel, ev.Note, ev.Pressure); break;
                case ControllerVoiceMidiEvent ev:       msg = new MidiControlChangeMessage(ev.Channel, ev.Number, ev.Value); break;
                case ProgramChangeVoiceMidiEvent ev:    msg = new MidiProgramChangeMessage(ev.Channel, ev.Number); break;
                case ChannelPressureVoiceMidiEvent ev:  msg = new MidiChannelPressureMessage(ev.Channel, ev.Pressure); break;
                case PitchWheelVoiceMidiEvent ev:       msg = new MidiPitchBendChangeMessage(ev.Channel, (UInt16)ev.Position); break;

                // SysEx messages
                case SystemExclusiveMidiEvent ev:       msg = new MidiSystemExclusiveMessage(ev.Data.AsBuffer()); break;

                // Meta events
                //case SequenceNumberMetaMidiEvent ev:
                //case TextMetaMidiEvent ev:
                //case CopyrightTextMetaMidiEvent ev:
                //case SequenceTrackNameTextMetaMidiEvent ev:
                //case InstrumentTextMetaMidiEvent ev:
                //case LyricTextMetaMidiEvent ev:
                //case MarkerTextMetaMidiEvent ev:
                //case CuePointTextMetaMidiEvent ev:
                //case ProgramNameTextMetaMidiEvent ev:
                //case DeviceNameTextMidiEvent ev:
                //case ChannelPrefixMetaMidiEvent ev:
                case MidiPortMetaMidiEvent ev:          break;
                case EndOfTrackMetaMidiEvent ev:        break;

                case TempoMetaMidiEvent ev:             clock.SetTempo((UInt32)ev.Value); break; //clock.SetTempo(60_000_000u / (UInt32)ev.Value); break;
                //case SMPTEOffsetMetaMidiEvent ev:
                case TimeSignatureMetaMidiEvent ev:     clock.SetTimeSignature(ev.Numerator, 1u << ev.Denominator); break;
                //case KeySignatureMetaMidiEvent ev:  break;
                //case ProprietaryMetaMidiEvent ev:


                default:
                    Debug.WriteLine($"Do not know how to convert event of type {e.GetType()}");
                    break;
                }


                if (msg != null)
                    port.SendMessage(msg);
            }
        }
    }
}
