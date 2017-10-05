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

        public Int32 Volume { get; private set; }
        public Int32 Expression { get; private set; }
        public Int32 Pan { get; private set; }
        public Int32 Program { get; private set; }

        public Int32 VuMeter
        {
            get {
                return (Int32)(100 * Math.Sqrt(velocities.Where(v => v > 0).Select(v => v*v).DefaultIfEmpty().Average()) / 127);
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        void NotifyPropertyChanged(string property)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
        }

        public IEnumerable<Tuple<Int64, MidiEvent>> Timeline => timeline;

        public TrackRunner(MidiTrack track, MidiSequencer sequencer)
        {
            this.sequencer = sequencer;
            this.track = track;

            Name = "";

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

            Volume = 100;
            Expression = 127;
            Pan = 64;
            Program = 0;

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

        public void StopAllNotes()
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
            // Voice messages
            case OffNoteVoiceMidiEvent ev:
                sequencer.SendMessage(new MidiNoteOffMessage(ev.Channel, ev.Note, ev.Velocity));
                velocities[ev.Note] = 0;
                NotifyPropertyChanged("VuMeter");
                break;
            case OnNoteVoiceMidiEvent ev:
                sequencer.SendMessage(new MidiNoteOnMessage(ev.Channel, ev.Note, ev.Velocity));
                velocities[ev.Note] = ev.Velocity;
                NotifyPropertyChanged("VuMeter");
                break;

            case ControllerVoiceMidiEvent ev:
                sequencer.SendMessage(new MidiControlChangeMessage(ev.Channel, ev.Number, ev.Value));
                switch ((Controller)ev.Number)
                {
                case Controller.VolumeCourse:       Volume = ev.Value; NotifyPropertyChanged(nameof(Volume)); break;
                case Controller.ExpressionCourse:   Expression = ev.Value; NotifyPropertyChanged(nameof(Expression)); break;
                case Controller.PanPositionCourse:  Pan = ev.Value; NotifyPropertyChanged(nameof(Pan)); break;
                }
                break;

            case ProgramChangeVoiceMidiEvent ev:
                sequencer.SendMessage(new MidiProgramChangeMessage(ev.Channel, ev.Number));
                Program = ev.Number;
                NotifyPropertyChanged(nameof(Program));
                break;

            case AftertouchNoteVoiceMidiEvent ev:   sequencer.SendMessage(new MidiPolyphonicKeyPressureMessage(ev.Channel, ev.Note, ev.Pressure)); break;
            case ChannelPressureVoiceMidiEvent ev:  sequencer.SendMessage(new MidiChannelPressureMessage(ev.Channel, ev.Pressure)); break;
            case PitchWheelVoiceMidiEvent ev:       sequencer.SendMessage(new MidiPitchBendChangeMessage(ev.Channel, (UInt16)ev.Position)); break;

            // SysEx messages
            case SystemExclusiveMidiEvent ev:       sequencer.SendMessage(new MidiSystemExclusiveMessage(ev.Data.AsBuffer())); break;

            // Meta events
            //case SequenceNumberMetaMidiEvent ev:
            //case TextMetaMidiEvent ev:
            //case CopyrightTextMetaMidiEvent ev:
            case SequenceTrackNameTextMetaMidiEvent ev: Name = ev.Text; NotifyPropertyChanged(nameof(Name)); break;
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
