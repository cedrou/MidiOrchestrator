using MidiSharp;
using MidiSharp.Events;
using MidiSharp.Events.Meta.Text;
using MidiSharp.Events.Voice.Note;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using Windows.Devices.Enumeration;
using Windows.Devices.Midi;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.Storage.Pickers;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;

// The Blank Page item template is documented at https://go.microsoft.com/fwlink/?LinkId=402352&clcid=0x409

namespace MidiOrchestrator
{
    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class MainPage : Page
    {
        public MainPage()
        {
            this.InitializeComponent();
        }

        MidiSequence sequence;
        MidiClock clock;
        IEnumerable<TrackRunner> tracks;

        private async void Button_Click(object _1, RoutedEventArgs _2)
        {
            // Find all output MIDI devices
            var midiOutportQueryString = MidiOutPort.GetDeviceSelector();
            var midiOutputDevices = await DeviceInformation.FindAllAsync(midiOutportQueryString);
            var devInfo = midiOutputDevices.FirstOrDefault();
            if (devInfo == null)
            { Debug.WriteLine("No MIDI devices found!"); return; }

            var midiOutPort = await MidiOutPort.FromIdAsync(devInfo.Id);
            if (midiOutPort == null)
            { Debug.WriteLine("Unable to create MidiOutPort"); return; }

            byte channel = 0;
            byte note = 60;
            byte velocity = 127;
            var midiMessageToSend = new MidiNoteOnMessage(channel, note, velocity);

            //midiOutPort.SendMessage(midiMessageToSend);


            var picker = new FileOpenPicker() {
                ViewMode = PickerViewMode.Thumbnail,
                SuggestedStartLocation = PickerLocationId.PicturesLibrary,
                FileTypeFilter = { ".mid" }
            };

            var file = await picker.PickSingleFileAsync();
            if (file == null)
            { Debug.WriteLine("PickSingleFile cancelled"); return; }

            //TODO: Add picked file to MRU

            sequence = null;
            try
            {
                using (var inputStream = await file.OpenReadAsync())
                {
                    sequence = MidiSequence.Open(inputStream.AsStreamForRead());
                }
            } catch (Exception exc) { Debug.WriteLine($"Failed to read MIDI sequence: {exc.Message}"); return; }

            clock = new MidiClock();

            tracks = sequence.Select(t => new TrackRunner(sequence, t, clock, midiOutPort) ).ToList();
            //new {
            //    Name = t.OfType<SequenceTrackNameTextMetaMidiEvent>().Select(e => e.Text).FirstOrDefault(),
            //    Messages = t.Select<MidiEvent, IMidiMessage>(e => {
            //        switch (e.GetType().Name)
            //        {
            //        case nameof(OnNoteVoiceMidiEvent):
            //            {
            //                var ev = e as OnNoteVoiceMidiEvent;
            //                return new MidiNoteOnMessage(ev.Channel, ev.Note, ev.Velocity);
            //            }
            //        case nameof(OffNoteVoiceMidiEvent):
            //            {
            //                var ev = e as OffNoteVoiceMidiEvent;
            //                return new MidiNoteOffMessage(ev.Channel, ev.Note, ev.Velocity);
            //            }
            //        }

            //        return null;
            //    })
            //}

            clock.Start();
            var full = sequence.ToString();
        }
    }
}
