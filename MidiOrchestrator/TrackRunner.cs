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
        MidiSequencer clock;
        MidiTrack track;
        List<Tuple<Int64, MidiEvent>> timeline;
        TrackControl control;
        int timelinePointer;

        public IEnumerable<Tuple<Int64, MidiEvent>> Timeline => timeline;

        public TrackRunner(MidiTrack track, MidiSequencer clock, TrackControl control)
        {
            this.clock = clock;
            this.track = track;
            this.control = control;

            Int64 tick = 0;
            timeline = new List<Tuple<Int64, MidiEvent>>();
            foreach (var e in track.Events)
            {
                tick += e.DeltaTime;
                timeline.Add(new Tuple<Int64, MidiEvent>(tick, e));
            }

            //this.clock.Tick += Clock_Tick;
            timelinePointer = 0;
            //ProcessEventsAt(0);
        }

        //private void Clock_Tick(MidiSequencer clk, EventArgs args)
        //{
        //    foreach (var e in timeline.Where(i => i.Item1 == clk.Ticks).Select(i => i.Item2))
        //    {
        //        ProcessEvent(e);
        //    }
        //}

        public UInt32 Run()
        {
            do
            {
                ProcessEvent(track.Events[timelinePointer]);
                timelinePointer++;
            } while (timelinePointer < track.Events.Count && track.Events[timelinePointer].DeltaTime == 0);

            return timelinePointer < track.Events.Count ? (UInt32)track.Events[timelinePointer].DeltaTime : UInt32.MaxValue;
        }

        private void ProcessEvent(MidiEvent e)
        {
            switch (e)
            {
            // MIDI messages
            case OffNoteVoiceMidiEvent ev:          clock.SendMessage(new MidiNoteOnMessage(ev.Channel, ev.Note, ev.Velocity)); break;
            case OnNoteVoiceMidiEvent ev:           clock.SendMessage(new MidiNoteOnMessage(ev.Channel, ev.Note, ev.Velocity)); break;
            case AftertouchNoteVoiceMidiEvent ev:   clock.SendMessage(new MidiPolyphonicKeyPressureMessage(ev.Channel, ev.Note, ev.Pressure)); break;
            case ControllerVoiceMidiEvent ev:       clock.SendMessage(new MidiControlChangeMessage(ev.Channel, ev.Number, ev.Value)); break;
            case ProgramChangeVoiceMidiEvent ev:    clock.SendMessage(new MidiProgramChangeMessage(ev.Channel, ev.Number)); break;
            case ChannelPressureVoiceMidiEvent ev:  clock.SendMessage(new MidiChannelPressureMessage(ev.Channel, ev.Pressure)); break;
            case PitchWheelVoiceMidiEvent ev:       clock.SendMessage(new MidiPitchBendChangeMessage(ev.Channel, (UInt16)ev.Position)); break;

            // SysEx messages
            case SystemExclusiveMidiEvent ev:       clock.SendMessage(new MidiSystemExclusiveMessage(ev.Data.AsBuffer())); break;

            // Meta events
            //case SequenceNumberMetaMidiEvent ev:
            //case TextMetaMidiEvent ev:
            //case CopyrightTextMetaMidiEvent ev:
            case SequenceTrackNameTextMetaMidiEvent ev: control.TrackName = ev.Text; break;
            //case InstrumentTextMetaMidiEvent ev:
            //case LyricTextMetaMidiEvent ev:
            //case MarkerTextMetaMidiEvent ev:
            //case CuePointTextMetaMidiEvent ev:
            //case ProgramNameTextMetaMidiEvent ev:
            //case DeviceNameTextMidiEvent ev:
            //case ChannelPrefixMetaMidiEvent ev:
            case MidiPortMetaMidiEvent ev:          break;
            case EndOfTrackMetaMidiEvent ev:        break;

            case TempoMetaMidiEvent ev:             clock.SetQuarterDuration((UInt32)ev.Value); break;
            //case SMPTEOffsetMetaMidiEvent ev:
            case TimeSignatureMetaMidiEvent ev:     clock.SetTimeSignature(ev.Numerator, 1u << ev.Denominator); break;
            //case KeySignatureMetaMidiEvent ev:  break;
            //case ProprietaryMetaMidiEvent ev:


            default:
                Debug.WriteLine($"Do not know how to convert event of type {e.GetType()}");
                break;
            }
        }
    }
}
