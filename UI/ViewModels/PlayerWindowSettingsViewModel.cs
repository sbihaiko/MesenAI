using System;
using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using Mesen.Config;
using Mesen.Logic;
using Mesen.Utilities;

namespace Mesen.ViewModels
{
	//One row of W-P8's Scale popup.
	public sealed record PlayerScaleChoice(double Value, string Label)
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

		[ObservableProperty] public partial bool IsFullscreen { get; set; }
		[ObservableProperty] public partial PlayerScaleChoice? SelectedScale { get; set; }

		public PlayerWindowSettingsViewModel(VideoConfig config, bool isFullscreen, double currentScale, Action toggleFullscreen, Action<double> setScale)
		{
			Config = config;
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
			_loading = false;

			if(!Avalonia.Controls.Design.IsDesignMode) {
				AddDisposable(ReactiveHelper.RegisterRecursiveObserver(Config, (s, e) => { Config.ApplyConfig(); }));
			}
		}

		partial void OnIsFullscreenChanged(bool value)
		{
			if(!_loading) {
				_toggleFullscreen();
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
