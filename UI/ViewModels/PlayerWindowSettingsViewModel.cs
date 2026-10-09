using System;
using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using Mesen.Config;
using Mesen.Localization;
using Mesen.Logic;
using Mesen.Utilities;

namespace Mesen.ViewModels
{
	//One row of W-P8's Scale popup.
	public sealed record PlayerScaleChoice(double Value, string Label)
	{
		public override string ToString() => Label;
	}

	//One row of the Interface size popup (#1111).
	public sealed record PlayerInterfaceSizeChoice(InterfaceSize Value, string Label)
	{
		public override string ToString() => Label;
	}

	//G.4 (PRD Part B §13.5.2 W-P8): Settings › Display - the window's size,
	//shape and full screen. Look (W-P10) owns what the pixels look like, so the
	//shader and the filter are not here. Everything is injected (the config to
	//edit, the window's state and the two window actions), so the constructor
	//does no EmuApi or window I/O. A value set elsewhere that is not in a short
	//list is shown as the current item and never rewritten
	//(PlayDisplaySettings.ItemsWithCurrent).
	public partial class PlayerWindowSettingsViewModel : DisposableViewModel
	{
		private static readonly VideoAspectRatio[] ShortAspectRatios = {
			VideoAspectRatio.Auto, VideoAspectRatio.NoStretching, VideoAspectRatio.Standard, VideoAspectRatio.Widescreen
		};

		private readonly Action _toggleFullscreen;
		private readonly Action<double> _setScale;
		private bool _loading;

		public VideoConfig Config { get; }
		public Enum[] AspectRatios { get; }
		public List<PlayerScaleChoice> Scales { get; }

		//#1111: Interface size scales Play's chrome only; it never reaches
		//_setScale, which is the picture's Scale row.
		public PreferencesConfig Preferences { get; }
		public List<PlayerInterfaceSizeChoice> InterfaceSizes { get; } = Enum.GetValues<InterfaceSize>().Select(s => new PlayerInterfaceSizeChoice(s, ResourceHelper.GetEnumText(s))).ToList();

		[ObservableProperty] public partial bool IsFullscreen { get; set; }
		[ObservableProperty] public partial PlayerScaleChoice? SelectedScale { get; set; }
		[ObservableProperty] public partial PlayerInterfaceSizeChoice? SelectedInterfaceSizeChoice { get; set; }

		public InterfaceSize SelectedInterfaceSize {
			get => SelectedInterfaceSizeChoice?.Value ?? InterfaceSize.Standard;
			set => SelectedInterfaceSizeChoice = InterfaceSizes.FirstOrDefault(c => c.Value == value) ?? InterfaceSizes[0];
		}

		public PlayerWindowSettingsViewModel(VideoConfig config, bool isFullscreen, double currentScale, Action toggleFullscreen, Action<double> setScale, PreferencesConfig? preferences = null)
		{
			Config = config;
			Preferences = preferences ?? new PreferencesConfig();
			_toggleFullscreen = toggleFullscreen;
			_setScale = setScale;

			AspectRatios = PlayDisplaySettings.ItemsWithCurrent(ShortAspectRatios, config.AspectRatio).Cast<Enum>().ToArray();

			double current = PlayDisplaySettings.WholeScale(currentScale) ?? Math.Round(currentScale, 2);
			List<double> values = current >= 1 ? PlayDisplaySettings.ItemsWithCurrent(PlayDisplaySettings.Scales, current) : new List<double>(PlayDisplaySettings.Scales);
			Scales = values.Select(v => new PlayerScaleChoice(v, v.ToString("0.##") + "×")).ToList();

			_loading = true;
			IsFullscreen = isFullscreen;
			//Never blank: under 1× the nearest offered scale shows (W-P8).
			double selected = PlayDisplaySettings.Nearest(values, current);
			SelectedScale = Scales.First(s => s.Value == selected);
			SelectedInterfaceSize = Preferences.InterfaceSize;
			_loading = false;

			if(!Avalonia.Controls.Design.IsDesignMode) {
				AddDisposable(ReactiveHelper.RegisterRecursiveObserver(Config, (s, e) => { Config.ApplyConfig(); }));
			}
		}

		//#910: the sheet's Exit fullscreen control. Turning the switch off is the
		//same single toggle, so the window has one path back to windowed.
		public void ExitFullscreen()
		{
			if(IsFullscreen) {
				IsFullscreen = false;
			}
		}

		partial void OnIsFullscreenChanged(bool value)
		{
			if(!_loading) {
				_toggleFullscreen();
			}
		}

		partial void OnSelectedInterfaceSizeChoiceChanged(PlayerInterfaceSizeChoice? value)
		{
			if(!_loading && value != null) {
				Preferences.InterfaceSize = value.Value;
			}
		}

		partial void OnSelectedScaleChanged(PlayerScaleChoice? value)
		{
			if(!_loading && value != null) {
				_setScale(value.Value);
			}
		}
	}
}
