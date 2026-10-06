using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Mesen.Config;
using Mesen.Interop;
using Mesen.Logic;
using Mesen.Utilities;
using Mesen.ViewModels;
using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Mesen.Controls
{
	public class StateGrid : UserControl
	{
		public static readonly StyledProperty<List<RecentGameInfo>> EntriesProperty = AvaloniaProperty.Register<StateGrid, List<RecentGameInfo>>(nameof(Entries));

		public static readonly StyledProperty<string> TitleProperty = AvaloniaProperty.Register<StateGrid, string>(nameof(Title));
		public static readonly StyledProperty<int> SelectedPageProperty = AvaloniaProperty.Register<StateGrid, int>(nameof(SelectedPage));
		public static readonly StyledProperty<bool> ShowArrowsProperty = AvaloniaProperty.Register<StateGrid, bool>(nameof(ShowArrows));
		public static readonly StyledProperty<bool> ShowCloseProperty = AvaloniaProperty.Register<StateGrid, bool>(nameof(ShowClose));
		public static readonly StyledProperty<GameScreenMode> ModeProperty = AvaloniaProperty.Register<StateGrid, GameScreenMode>(nameof(Mode));
		public static readonly StyledProperty<int> SelectedIndexProperty = AvaloniaProperty.Register<StateGrid, int>(nameof(SelectedIndex));

		public string Title
		{
			get { return GetValue(TitleProperty); }
			set { SetValue(TitleProperty, value); }
		}

		public int SelectedPage
		{
			get { return GetValue(SelectedPageProperty); }
			set { SetValue(SelectedPageProperty, value); }
		}

		public int SelectedIndex
		{
			get { return GetValue(SelectedIndexProperty); }
			set { SetValue(SelectedIndexProperty, value); }
		}

		public bool ShowArrows
		{
			get { return GetValue(ShowArrowsProperty); }
			set { SetValue(ShowArrowsProperty, value); }
		}

		public bool ShowClose
		{
			get { return GetValue(ShowCloseProperty); }
			set { SetValue(ShowCloseProperty, value); }
		}

		public GameScreenMode Mode
		{
			get { return GetValue(ModeProperty); }
			set { SetValue(ModeProperty, value); }
		}

		public List<RecentGameInfo> Entries
		{
			get { return GetValue(EntriesProperty); }
			set { SetValue(EntriesProperty, value); }
		}

		private int _colCount = 0;
		private int _rowCount = 0;
		private DispatcherTimer _timerInput = new DispatcherTimer();

		private int ElementsPerPage => _rowCount * _colCount;
		private int PageCount => (int)Math.Ceiling((double)Entries.Count / ElementsPerPage);

		static StateGrid()
		{
			BoundsProperty.Changed.AddClassHandler<StateGrid>((x, e) => x.InitGrid());
			EntriesProperty.Changed.AddClassHandler<StateGrid>((x, e) => {
				x.SelectedPage = 0;
				x.SelectedIndex = 0;
				x.InitGrid(true);
			});
			SelectedPageProperty.Changed.AddClassHandler<StateGrid>((x, e) => x.InitGrid(true));
			SelectedIndexProperty.Changed.AddClassHandler<StateGrid>((x, e) => x.UpdateSelectedEntry());

			IsVisibleProperty.Changed.AddClassHandler<StateGrid>((x, e) => {
				if(x.IsVisible) {
					x.FocusWhenUncovered();
				}
			});
		}

		private void UpdateSelectedEntry()
		{
			if(SelectedIndex < SelectedPage * ElementsPerPage || SelectedIndex >= (SelectedPage + 1) * ElementsPerPage) {
				//Change page
				SelectedPage = SelectedIndex / ElementsPerPage;
				return;
			} else {
				Grid grid = this.GetControl<Grid>("Grid");
				int startIndex = ElementsPerPage * SelectedPage;
				for(int i = 0; i < grid.Children.Count; i++) {
					if(grid.Children[i] is StateGridEntry entry) {
						entry.IsActiveEntry = startIndex + i == SelectedIndex;
					}
				}
			}
		}

		public StateGrid()
		{
			InitializeComponent();
			Focusable = true;
			//A bound `tiles` class (the slot sheet) can arrive after the first
			//layout. Classes also carries the pseudo-classes (:focus-within), so
			//rebuild only when the slot-tiles look itself flips - a rebuild on
			//every focus change would drop the focused tile and trap Tab.
			Classes.CollectionChanged += (_, _) => {
				if(_slotTiles != IsSlotTiles) {
					_slotTiles = IsSlotTiles;
					InitGrid(true);
				}
			};
			_timerInput.Interval = TimeSpan.FromMilliseconds(50);
			_timerInput.Tick += TimerInput_Tick;
		}

		private void InitializeComponent()
		{
			AvaloniaXamlLoader.Load(this);
		}

		protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
		{
			base.OnAttachedToVisualTree(e);
			_timerInput.Start();
			FocusWhenUncovered();
		}

		//ADR-0256 Decision 3: the grid asks for the focus through the one path,
		//which focuses it with a NavigationMethod (the ring the theme paints on
		//:focus-visible) and refuses while a Play surface is up over it. It keeps
		//asking on its own - it is a content-area screen, not a surface in the Esc
		//stack, and it also runs in Advanced, where no Play claim is ever open -
		//so this is the same focus it always took, minus the case where a sheet
		//over the game would have lost the keyboard to it.
		private void FocusWhenUncovered()
		{
			if(!PlayFocusOnOpen.SurfaceIsUp(this)) {
				PlayFocusOnOpen.Enter(this);
			}
		}

		protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
		{
			base.OnDetachedFromVisualTree(e);
			_timerInput.Stop();
		}

		//ADR-0256 Decision 2, the grid's exit: the pad's Back reaches a slot grid
		//opened by the Load/Save-state shortcuts, which never goes through the Esc
		//router. A grid with no close box (the Play home's row of tiles, W-P2) has
		//ShowClose false and nothing for Back to leave.
		public bool CanCloseFromPad => ShowClose;

		public void CloseFromPad() => RequestClose();

		private void OnCloseClick(object sender, RoutedEventArgs e) => RequestClose();

		private void RequestClose()
		{
			//#909: the grid is no longer opened from the pause overlay (W-P4's Save
			//states row is its own grid now), so the X closes the grid it belongs
			//to: the quick save/load shortcuts' and Advanced's own.
			if(DataContext is RecentGamesViewModel model) {
				if(model.NeedResume) {
					EmuApi.Resume();
				}
				model.Visible = false;
			}
		}

		private void OnPrevPageClick(object sender, RoutedEventArgs e)
		{
			int page = SelectedPage - 1;
			if(page < 0) {
				page = PageCount - 1;
			}
			SelectedPage = page;
		}

		private void OnNextPageClick(object sender, RoutedEventArgs e)
		{
			int page = SelectedPage + 1;
			if(page >= PageCount) {
				page = 0;
			}
			SelectedPage = page;
		}

		//ADR-0249: the `tiles` class lays the recent games out as the Player
		//home's row of tiles (W-P2) and tags each entry so the theme styles it.
		private const double TileColumnWidth = 198;
		private bool IsTiles => Classes.Contains("tiles") && Mode == GameScreenMode.RecentGames;
		//ADR-0249: the Save/Load state slots in the Player's slot sheet keep the
		//4 x 3 grid; each entry is a theme tile (`tiles slot`).
		private bool _slotTiles;
		private bool IsSlotTiles => Classes.Contains("tiles") && Mode != GameScreenMode.RecentGames;

		private void InitGrid(bool forceUpdate = false)
		{
			if(Entries == null) {
				return;
			}

			Grid grid = this.GetControl<Grid>("Grid");
			Size size = grid.Bounds.Size;

			int colCount = Math.Min(4, Math.Max(1, (int)(size.Width / 205)));
			int rowCount = Math.Min(3, Math.Max(1, (int)(size.Height / 200)));

			if(Entries.Count <= 1) {
				colCount = 1;
				rowCount = 1;
			} else if(Entries.Count <= 4) {
				colCount = Math.Min(2, colCount);
				rowCount = colCount;
			}

			if(Mode != GameScreenMode.RecentGames) {
				colCount = 4;
				rowCount = 3;
			} else if(IsTiles) {
				//ADR-0249 W-P2: one row of fixed-width tiles, as many as fit (max 5).
				colCount = Math.Min(5, Math.Max(1, (int)(size.Width / TileColumnWidth)));
				rowCount = 1;
			}

			bool layoutChanged = _colCount != colCount || _rowCount != rowCount;
			if(!forceUpdate && !layoutChanged) {
				//Grid is already the same size
				return;
			}

			if(layoutChanged) {
				SelectedPage = 0;
			}

			_colCount = colCount;
			_rowCount = rowCount;

			grid.Children.Clear();

			ColumnDefinitions columnDefinitions = new ColumnDefinitions();
			for(int i = 0; i < colCount; i++) {
				columnDefinitions.Add(IsTiles ? new ColumnDefinition(TileColumnWidth, GridUnitType.Pixel) : new ColumnDefinition(1, GridUnitType.Star));
			}
			grid.ColumnDefinitions = columnDefinitions;

			RowDefinitions rowDefinitions = new RowDefinitions();
			for(int i = 0; i < rowCount; i++) {
				rowDefinitions.Add(new RowDefinition(1, GridUnitType.Star));
			}
			grid.RowDefinitions = rowDefinitions;

			int elementsPerPage = ElementsPerPage;
			int startIndex = elementsPerPage * SelectedPage;

			ShowArrows = Entries.Count > elementsPerPage;
			ShowClose = Mode != GameScreenMode.RecentGames;

			List<StateGridEntry> entries = new();
			for(int row = 0; row < rowCount; row++) {
				for(int col = 0; col < colCount; col++) {
					int index = startIndex + row * colCount + col;

					if(index >= Entries.Count) {
						break;
					}

					StateGridEntry ctrl = new StateGridEntry();
					if(IsTiles) {
						ctrl.Classes.Add("tiles");
					} else if(IsSlotTiles) {
						ctrl.Classes.Add("tiles");
						ctrl.Classes.Add("slot");
					}

					ctrl.SetValue(Grid.ColumnProperty, col);
					ctrl.SetValue(Grid.RowProperty, row);
					ctrl.Entry = Entries[index];
					ctrl.IsActiveEntry = index == SelectedIndex;
					ctrl.Init();

					entries.Add(ctrl);
				}
			}
			grid.Children.AddRange(entries);
		}

		private bool _loadRequested = false;
		private HashSet<ushort> _pressedKeyCodes = new();

		private void TimerInput_Tick(object? sender, EventArgs e)
		{
			if(!IsEffectivelyVisible || !IsKeyboardFocusWithin || Entries == null || Entries.Count == 0) {
				_loadRequested = false;
				return;
			}

			List<ushort> keyCodes = InputApi.GetPressedKeys();

			//The player's own console mapping (port 1, plus GB/GBA/SMS), for the
			//keyboard's codes only - built on first use, so a pad-only session never
			//reads the config.
			List<KeyMapping>? mappings = null;
			List<KeyMapping> ConsoleMappings() => mappings ??= new List<ControllerConfig>() {
				ConfigManager.Config.Nes.Port1,
				ConfigManager.Config.Gameboy.Controller, ConfigManager.Config.Gba.Controller, ConfigManager.Config.Sms.Port1
			}.SelectMany((a) => new List<KeyMapping>() { a.Mapping1, a.Mapping2, a.Mapping3, a.Mapping4 }).ToList();

			foreach(ushort keyCode in keyCodes) {
				if(keyCode == 0 || !_pressedKeyCodes.Add(keyCode)) {
					continue;
				}

				//ADR-0256 Decision 4: a pad's code means what the pad's own preset
				//binds (PadNavControls), resolved for the device the code came from
				//(PadNaming reads the backend's own name - "Pad1 A", "Joy1 But2") -
				//never the rebindable console mapping. That is what lets a second pad
				//drive this grid at all, and what keeps a player who clears or moves
				//their console D-pad from losing grid navigation.
				//
				//Scoped to the Play door, which is the same gate the bridge asks
				//(PlayPadNavigation.InPlayDoor): this one control draws both doors'
				//grids - W-P2's tiles and the Save/Load screens in Play, Advanced's
				//game-selection and Save/Load screens in the classic GUI - and the
				//ADR is the Play GUI's. Ungated, the branch silently ate every pad
				//button in Advanced: the preset's Back has no GridAction, so the
				//`continue` below dropped it, and the bridge's own Back edge is
				//gated off there - while the pad buttons the console mapping does
				//bind to A/B/X/Y/Select/Start no longer reached the mapping that
				//loaded the entry, which is how Advanced navigated this screen
				//before ADR-0256 and how it still does.
				MainWindowViewModel? playDoor = MainWindowViewModel.Instance;
				if(PlayPadNavigation.InPlayDoor(playDoor?.IsPlayerMode == true, playDoor?.IsPlayWorkspace == true)
					&& PadNaming.Of(keyCode, InputApi.GetKeyName) is PadId pad
					&& PadNavControls.Resolve(pad.Family, pad.Device, InputApi.GetKeyCode) is PadNavMapping padNav) {
					switch(PlayPadNavigation.GridAction(keyCode, padNav)) {
						case PadNavAction.Left: MoveLeft(); break;
						case PadNavAction.Right: MoveRight(); break;
						case PadNavAction.Down: MoveDown(); break;
						case PadNavAction.Up: MoveUp(); break;
						//The preset's Confirm (the pad's A), not the console's A/B/X/Y/
						//Select/Start: the pad's B is the preset's Back and is the
						//bridge's (it leaves the grid), so reading the load off the
						//console mapping would collide with it (the B ambiguity).
						case PadNavAction.Confirm: _loadRequested = true; break;
					}
					continue;
				}

				//Keyboard: the player's own console mapping is theirs (Decision 4 is
				//about the pad), so the classic port-1 walk is unchanged.
				foreach(KeyMapping mapping in ConsoleMappings()) {
					if(mapping.Left == keyCode) {
						MoveLeft();
						break;
					} else if(mapping.Right == keyCode) {
						MoveRight();
						break;
					} else if(mapping.Down == keyCode) {
						MoveDown();
						break;
					} else if(mapping.Up == keyCode) {
						MoveUp();
						break;
					} else if(mapping.A == keyCode || mapping.B == keyCode || mapping.X == keyCode || mapping.Y == keyCode || mapping.Select == keyCode || mapping.Start == keyCode) {
						_loadRequested = true;
						break;
					}
				}
			}

			_pressedKeyCodes.Clear();
			_pressedKeyCodes.UnionWith(keyCodes);

			if(_loadRequested && keyCodes.Count == 0 && Entries.Count > 0) {
				//Load game/state once all buttons are released to avoid game processing pressed button
				RecentGameInfo entry = Entries[GridSelection.SlotOf(SelectedIndex, Entries.Count)];
				if(entry.IsEnabled() == true) {
					entry.Load();
				}
				_loadRequested = false;
			}
		}

		//#897: the arithmetic lives in the host-free GridSelection so it can be
		//tested without a host; these are the control's only callers of it. The
		//row count is handed over because "is there anything above me" is a fact
		//about the layout, and inferring it from the entry and column counts gets
		//the *paged* one-row grid wrong (see GridSelection.Next).
		private void MoveLeft() => SelectedIndex = GridSelection.Next(SelectedIndex, GridDirection.Left, Entries.Count, _colCount, _rowCount);

		private void MoveRight() => SelectedIndex = GridSelection.Next(SelectedIndex, GridDirection.Right, Entries.Count, _colCount, _rowCount);

		private void MoveDown() => SelectedIndex = GridSelection.Next(SelectedIndex, GridDirection.Down, Entries.Count, _colCount, _rowCount);

		private void MoveUp() => SelectedIndex = GridSelection.Next(SelectedIndex, GridDirection.Up, Entries.Count, _colCount, _rowCount);

		//#896: what the bridge asks before it lets the grid keep an Up press. A
		//grid with one row has nothing above it, which is the same fact
		//GridSelection.Next reads to leave a one-row selection where it is - the
		//bridge and the grid must never disagree about this, so both read the row
		//count and neither re-derives it.
		public bool MovesWithUpFromPad => _rowCount > 1;
	}
}
