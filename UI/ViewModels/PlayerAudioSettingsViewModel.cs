using System;
using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using Mesen.Config;
using Mesen.Logic;
using Mesen.Utilities;

namespace Mesen.ViewModels
{
	//PRD Part B §13.5.2 W-P8: Settings › Audio - Sound, Volume, Output device,
	//bound to the same AudioConfig the classic Audio page edits, so a value set
	//in Options is the current item here and is never rewritten by opening the
	//tab. The equalizer, reverb, crossfeed, latency and sample rate stay in
	//"More in Options…". The device list is injected: this does no ConfigApi I/O.
	public partial class PlayerAudioSettingsViewModel : DisposableViewModel
	{
		private readonly bool _loading;

		public AudioConfig Config { get; }
		//The snapshot Cancel/IsDirty compare against (as AudioConfigViewModel's).
		public AudioConfig OriginalConfig { get; }
		public List<string> Devices { get; }

		[ObservableProperty] public partial double Volume { get; set; }
		[ObservableProperty] public partial string? SelectedDevice { get; set; }

		public PlayerAudioSettingsViewModel(AudioConfig config, IReadOnlyList<string> devices)
		{
			Config = config;
			OriginalConfig = config.Clone();
			Devices = PlayerAudioSettings.Devices(devices, config.AudioDevice);

			_loading = true;
			Volume = config.MasterVolume;
			//An empty setting shows the first device without writing it.
			SelectedDevice = config.AudioDevice != "" ? config.AudioDevice : Devices.FirstOrDefault();
			_loading = false;

			if(!Avalonia.Controls.Design.IsDesignMode) {
				AddDisposable(ReactiveHelper.RegisterRecursiveObserver(Config, (s, e) => { Config.ApplyConfig(); }));
			}
		}

		partial void OnVolumeChanged(double value)
		{
			if(!_loading) {
				Config.MasterVolume = PlayerSliders.ToConfig(value, 100);
			}
		}

		partial void OnSelectedDeviceChanged(string? value)
		{
			//A popup that loses its item reports null; that is not a choice.
			if(!_loading && value != null) {
				Config.AudioDevice = value;
			}
		}
	}
}
