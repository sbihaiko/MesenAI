using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Mesen.Interop;
using Mesen.Localization;
using Mesen.Logic;
using Mesen.Utilities;

namespace Mesen.ViewModels
{
	//One key of the sheet's drawn pad. Placed by ControllerPadLayout - the same
	//geometry W-P15 draws with - and lit by the pad's own button, so the sheet
	//adds a state to that drawing rather than a second one.
	public partial class ControllerPadLight : ObservableObject
	{
		public SetupButton Button { get; }
		public string Name { get; }
		public bool ShowsLabel => ControllerPadLayout.ShowsLabel(Button);

		private PadKey Key => ControllerPadLayout.Of(Button);
		public double Left => Key.Left;
		public double Top => Key.Top;
		public double Width => Key.Width;
		public double Height => Key.Height;
		public bool IsDPad => Key.Shape == PadKeyShape.DPad;
		public bool IsRound => Key.Shape == PadKeyShape.Round;
		public bool IsShoulder => Key.Shape == PadKeyShape.Shoulder;

		[ObservableProperty] public partial bool IsLit { get; set; }

		public ControllerPadLight(SetupButton button)
		{
			Button = button;
			Name = ControllerLivePad.KeyName(button);
		}

		//Follows the pad's own button (the tester's list, in the core's bit
		//order). A pad reporting fewer buttons than the core's list, or a key
		//this pad has no counterpart for, leaves the key dark instead of throwing.
		public void Follow(GamepadTestItem? pad)
		{
			int? bit = ControllerLivePad.BitOf(Button, pad?.BackendKind ?? GamepadBackend.None);
			IsLit = pad != null && bit is int index && index < pad.Buttons.Count && pad.Buttons[index].IsPressed;
		}
	}

	//ADR-0255 slice 1 (PRD Part B §13.5.2 W-P17): the Play-native Controller
	//sheet. It shows the live pad (the drawn keys above) and its values (the
	//host tester's own button and axis readouts), and it is what Play ›
	//Settings › Controls › More in Options… lands on now that it replaces the
	//classic Input page - which stays one link away, inside the sheet.
	//
	//Reads are scoped, as ADR-0255's consequences require: the tester's own
	//60 Hz poll is gated on a visible Test tab, and a sheet over a running game
	//would break that scoping, so this sheet reads on its own timer and only
	//while it is visible *and* the game is paused - a state it makes true when it
	//opens, and lets go of the moment it stops being true (a game that starts
	//running under it stops the reads on the next tick, and starts them again if
	//it pauses once more). The timer itself keeps running while the sheet is up,
	//because a tick is the only thing that can see that second pause. It never
	//resumes the game: the pause outlives the sheet, as the one W-P4 opened it
	//over did.
	public partial class ControllerSheetViewModel : DisposableViewModel
	{
		[ObservableProperty] public partial bool IsVisible { get; set; }

		//The pad the sheet shows: the first connected one. Which pad plays as
		//which player is slice 2, so until it lands the sheet says how many it is
		//not showing rather than pretending there is only one.
		[ObservableProperty, NotifyPropertyChangedFor(nameof(HasPad))] public partial GamepadTestItem? Pad { get; private set; }
		[ObservableProperty] public partial string MorePadsText { get; private set; } = "";

		public bool HasPad => Pad != null;

		public IReadOnlyList<ControllerPadLight> PadKeys { get; } = ControllerLivePad.Keys.Select(button => new ControllerPadLight(button)).ToList();

		//The host tester, reused whole: its item type is what draws the values
		//list, and its 16 ms interval is the poll this sheet re-scopes.
		public GamepadTesterViewModel Tester { get; } = new();

		//Injectable for the headless tests; the Core's own answer by default.
		public Func<bool> IsPaused { get; set; } = EmuApi.IsPaused;
		public Action Pause { get; set; } = EmuApi.Pause;

		private DispatcherTimer? _poll;

		public ControllerSheetViewModel()
		{
			AddDisposable(Tester);
			//The tester's own tab gate stays shut: this sheet owns its polling.
			AddDisposable(this.ObserveProp(nameof(IsVisible), UpdatePolling));
		}

		public void Open()
		{
			//The sheet reads the host devices, so the game has to be paused while it
			//is up (ADR-0255 Consequences) - and paused *before* the sheet opens,
			//because the poll starts from the state it finds and nothing re-reads it
			//afterwards. Opened over W-P4 the game already is; with a game running
			//under a task door's Settings… this is the pause. The caller only opens
			//the sheet for a loaded game, so this never pauses nothing.
			if(!IsPaused()) {
				Pause();
			}
			//The tester re-reads a pad's identity (name, VID:PID) only when the
			//set of connected pads changes, so a sheet reopened after swapping one
			//pad for another would keep showing the old name. An empty list is a
			//change; the poll that starts right after fills it again.
			Tester.Gamepads.Clear();
			Pad = null;
			IsVisible = true;
		}

		public void Close()
		{
			IsVisible = false;
		}

		//The timer's lifetime follows the sheet's visibility, not the stricter read
		//condition: nothing observes the pause state, so the tick is the only thing
		//that can notice a game pausing again under the sheet. A timer that stopped
		//the moment the game resumed never restarted (ControllerSheetReads.Polls).
		private void UpdatePolling()
		{
			if(!ControllerSheetReads.Polls(IsVisible)) {
				StopPolling();
				return;
			}
			if(_poll != null) {
				return;
			}
			if(ControllerSheetReads.Wanted(IsVisible, IsPaused())) {
				Refresh();
			}
			_poll = new DispatcherTimer();
			_poll.Interval = TimeSpan.FromMilliseconds(16); //the tester's own ~60 Hz
			_poll.Tick += (s, e) => Tick();
			_poll.Start();
		}

		private void Tick()
		{
			//A shortcut resumed the game under the sheet (the Esc router does not
			//route it): the reads stop with it, as the tester's tab gate does - and
			//start again on a later tick if the game pauses once more.
			if(ControllerSheetReads.Wanted(IsVisible, IsPaused())) {
				Refresh();
			}
		}

		private void StopPolling()
		{
			_poll?.Stop();
			_poll = null;
		}

		//The tab may be torn down while still visible (the game went, the window
		//closed): stop the timer so it does not keep polling a disposed view-model.
		protected override void DisposeView()
		{
			StopPolling();
		}

		//One read of the host devices into the sheet's own state. Only called with
		//the sheet visible over a paused game (UpdatePolling, Tick).
		public void Refresh()
		{
			Tester.Refresh();
			ApplyPad();
		}

		//The sheet's own state from the tester's list: the pad it shows, its name,
		//how many it is not showing, and which drawn key each of its buttons
		//lights. Split from Refresh because the read is the one part a test cannot
		//make (it needs a physical pad): the test holds the list still and applies
		//it, the same way the Test tab's own tests inject a pad.
		public void ApplyPad()
		{
			GamepadTestItem? pad = Tester.Gamepads.Count > 0 ? Tester.Gamepads[0] : null;
			Pad = pad;
			MorePadsText = Tester.Gamepads.Count > 1 ? ResourceHelper.GetMessage("ControllerSheetMorePads", Tester.Gamepads.Count) : "";
			foreach(ControllerPadLight key in PadKeys) {
				key.Follow(pad);
			}
		}
	}
}
