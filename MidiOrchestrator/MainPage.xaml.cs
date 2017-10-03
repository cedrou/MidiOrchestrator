﻿using MidiSharp;
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
        MidiSynthesizer midiSynth;
        MidiSequence sequence;
        MidiClock clock;
        IEnumerable<TrackRunner> tracks;
        string sequenceDump;

        public MainPage()
        {
            this.InitializeComponent();
        }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);

            midiSynth = await MidiSynthesizer.CreateAsync();
        }

        protected override void OnNavigatingFrom(NavigatingCancelEventArgs e)
        {
            base.OnNavigatingFrom(e);

            clock.Stop();

            midiSynth.Dispose();
            midiSynth = null;
        }


        private async void btnLoad_Click(object sender, RoutedEventArgs e)
        {
            // Select the file
            var picker = new FileOpenPicker() {
                ViewMode = PickerViewMode.Thumbnail,
                SuggestedStartLocation = PickerLocationId.PicturesLibrary,
                FileTypeFilter = { ".mid" }
            };

            var file = await picker.PickSingleFileAsync();
            if (file == null)
            { Debug.WriteLine("PickSingleFile cancelled"); return; }

            //TODO: Add picked file to MRU

            // Open the file
            sequence = null;
            try
            {
                using (var inputStream = await file.OpenReadAsync())
                {
                    sequence = MidiSequence.Open(inputStream.AsStreamForRead());
                }
            } catch (Exception exc) { Debug.WriteLine($"Failed to read MIDI sequence: {exc.Message}"); return; }

            sequenceDump = sequence.ToString();

            // Create a new clock 
            clock = new MidiClock(sequence);

            // Create the track list
            tracks = sequence.Select(t => new TrackRunner(sequence, t, clock, midiSynth)).ToList();
        }

        private void btnStart_Click(object sender, RoutedEventArgs e)
        {
            clock.Start();
        }

        private void btnPause_Click(object sender, RoutedEventArgs e)
        {
            midiSynth.SendMessage(new MidiStopMessage());
            clock.Pause();
        }

        private void btnStop_Click(object sender, RoutedEventArgs e)
        {
            clock.Stop();
        }
    }
}
