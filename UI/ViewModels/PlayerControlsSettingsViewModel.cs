using CommunityToolkit.Mvvm.ComponentModel;
using Mesen.Config;
using Mesen.Localization;
using Mesen.Logic;
using Mesen.Utilities;

namespace Mesen.ViewModels
{
	//PRD Part B §13.5.2 W-P8: Settings › Controls - which pads are connected,
	//rumble strength and stick deadzone, bound to the same InputConfig the
	//classic Input page edits. Per-player controller types live in each
	//console's own config, and button mapping is the classic page's job: both
	//stay in "More in Options…". The pad count is injected (no InputApi I/O).
	public partial class PlayerControlsSettingsViewModel : DisposableViewModel
	{
		private readonly bool _loading;

		public InputConfig Config { get; }
		//The snapshot Cancel/IsDirty compare against (as InputConfigViewModel's).
		public InputConfig OriginalConfig { get; }
		public string ControllersText { get; }

		//#1112: the Menu tick row exists only while the pad in hand is aimable; the
		//sheet reads that once and hands it to both this row and its own height.
		public bool MenuTickAvailable { get; }

		[ObservableProperty, NotifyPropertyChangedFor(nameof(RumbleText), nameof(MenuTickEnabled), nameof(MenuTickRumbleOff))] public partial double Rumble { get; set; }

		//Disabled with its reason while Rumble is 0 (the tick is a rumble).
		public bool MenuTickEnabled => PlayerSliders.ToConfig(Rumble, 10) > 0;
		public bool MenuTickRumbleOff => !MenuTickEnabled;
		[ObservableProperty] public partial double Deadzone { get; set; }

		public string RumbleText => PlayerSliders.ToConfig(Rumble, 10) == 0 ? ResourceHelper.GetMessage("PlayerRumbleOff") : PlayerSliders.ToConfig(Rumble, 10).ToString();

		public PlayerControlsSettingsViewModel(InputConfig config, int connectedPads, bool menuTickAvailable)
		{
			MenuTickAvailable = menuTickAvailable;
			Config = config;
			OriginalConfig = config.Clone();
			ControllersText = connectedPads switch {
				<= 0 => ResourceHelper.GetMessage("PlayerControllersNone"),
				1 => ResourceHelper.GetMessage("PlayerControllersOne"),
				_ => ResourceHelper.GetMessage("PlayerControllersMany", connectedPads)
			};

			_loading = true;
			Rumble = config.ForceFeedbackIntensity;
			Deadzone = config.ControllerDeadzoneSize;
			_loading = false;

			if(!Avalonia.Controls.Design.IsDesignMode) {
				AddDisposable(ReactiveHelper.RegisterRecursiveObserver(Config, (s, e) => { Config.ApplyConfig(); }));
			}
		}

		partial void OnRumbleChanged(double value)
		{
			if(!_loading) {
				Config.ForceFeedbackIntensity = PlayerSliders.ToConfig(value, 10);
			}
		}

		partial void OnDeadzoneChanged(double value)
		{
			if(!_loading) {
				Config.ControllerDeadzoneSize = PlayerSliders.ToConfig(value, 4);
			}
		}
	}
}
