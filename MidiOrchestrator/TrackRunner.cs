using MidiSharp;
using MidiSharp.Events;
using MidiSharp.Events.Meta;
using MidiSharp.Events.Meta.Text;
using MidiSharp.Events.Voice;
using MidiSharp.Events.Voice.Note;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using System.Threading.Tasks;
using Windows.Devices.Midi;
using Windows.Storage.Streams;

namespace MidiOrchestrator
{
    public class TrackRunner : INotifyPropertyChanged
    {
        MidiSequencer sequencer;
        MidiTrack track;
        List<Tuple<Int64, MidiEvent>> timeline;
        int timelinePointer;
        private byte[] velocities = new byte[128];

        public String Name { get; private set; }

        public Int32 Channel { get; }

        public Boolean IsVoiceTrack { get; }

        public event PropertyChangedEventHandler PropertyChanged;

        public IEnumerable<Tuple<Int64, MidiEvent>> Timeline => timeline;

        public TrackRunner(MidiTrack track, MidiSequencer sequencer)
        {
            this.sequencer = sequencer;
            this.track = track;

            Name = "";
            Channel = -1;

            Int64 tick = 0;
            timeline = new List<Tuple<Int64, MidiEvent>>();
            foreach (var e in track.Events)
            {
                tick += e.DeltaTime;
                timeline.Add(new Tuple<Int64, MidiEvent>(tick, e));
            }

            var firstVoiceEvent = track.FirstOrDefault(e => e is VoiceMidiEvent) as VoiceMidiEvent;
            IsVoiceTrack = firstVoiceEvent != null;
            Channel = firstVoiceEvent?.Channel ?? -1;

            timelinePointer = 0;
        }

        public UInt32 Run()
        {
            do
            {
                ProcessEvent(track.Events[timelinePointer]);
                timelinePointer++;
            } while (timelinePointer < track.Events.Count && track.Events[timelinePointer].DeltaTime == 0);

            return timelinePointer < track.Events.Count ? (UInt32)track.Events[timelinePointer].DeltaTime : UInt32.MaxValue;
        }

        public void Stop()
        {
            for (byte i = 0; i < 128; i++)
            {
                if (velocities[i] > 0)
                {
                    sequencer.SendMessage(new MidiNoteOnMessage((byte)Channel, i, 0));
                    velocities[i] = 0;
                }
            }
        }

        private void ProcessEvent(MidiEvent e)
        {
            switch (e)
            {
            // MIDI messages
            case OffNoteVoiceMidiEvent ev:
                sequencer.SendMessage(new MidiNoteOffMessage(ev.Channel, ev.Note, ev.Velocity));
                velocities[ev.Note] = 0;
                break;
            case OnNoteVoiceMidiEvent ev:
                sequencer.SendMessage(new MidiNoteOnMessage(ev.Channel, ev.Note, ev.Velocity));
                velocities[ev.Note] = ev.Velocity;
                break;

            case AftertouchNoteVoiceMidiEvent ev:   sequencer.SendMessage(new MidiPolyphonicKeyPressureMessage(ev.Channel, ev.Note, ev.Pressure)); break;
            case ControllerVoiceMidiEvent ev:       sequencer.SendMessage(new MidiControlChangeMessage(ev.Channel, ev.Number, ev.Value)); break;
            case ProgramChangeVoiceMidiEvent ev:    sequencer.SendMessage(new MidiProgramChangeMessage(ev.Channel, ev.Number)); break;
            case ChannelPressureVoiceMidiEvent ev:  sequencer.SendMessage(new MidiChannelPressureMessage(ev.Channel, ev.Pressure)); break;
            case PitchWheelVoiceMidiEvent ev:       sequencer.SendMessage(new MidiPitchBendChangeMessage(ev.Channel, (UInt16)ev.Position)); break;

            // SysEx messages
            case SystemExclusiveMidiEvent ev:       sequencer.SendMessage(new MidiSystemExclusiveMessage(ev.Data.AsBuffer())); break;

            // Meta events
            //case SequenceNumberMetaMidiEvent ev:
            //case TextMetaMidiEvent ev:
            //case CopyrightTextMetaMidiEvent ev:
            case SequenceTrackNameTextMetaMidiEvent ev: Name = ev.Text; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("TrackName")); break;
            //case InstrumentTextMetaMidiEvent ev:
            //case LyricTextMetaMidiEvent ev:
            //case MarkerTextMetaMidiEvent ev:
            //case CuePointTextMetaMidiEvent ev:
            //case ProgramNameTextMetaMidiEvent ev:
            //case DeviceNameTextMidiEvent ev:
            //case ChannelPrefixMetaMidiEvent ev:
            case MidiPortMetaMidiEvent ev:          break;
            case EndOfTrackMetaMidiEvent ev:        break;

            case TempoMetaMidiEvent ev:             sequencer.SetQuarterDuration((UInt32)ev.Value); break;
            //case SMPTEOffsetMetaMidiEvent ev:
            case TimeSignatureMetaMidiEvent ev:     sequencer.SetTimeSignature(ev.Numerator, 1u << ev.Denominator); break;
            //case KeySignatureMetaMidiEvent ev:  break;
            //case ProprietaryMetaMidiEvent ev:


            default:
                Debug.WriteLine($"Do not know how to convert event of type {e.GetType()}");
                break;
            }
        }
    }
}
