using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Mesen.GUI.Utilities;
using Mesen.Interop;
using Mesen.Localization;
using Mesen.Logic;
using Mesen.Utilities;
using Mesen.ViewModels;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Mesen.Windows
{
	public partial class SelectRomWindow : MesenWindow
	{
		private ListBox _listBox;
		private TextBox _searchBox;

		public SelectRomWindow()
		{
			InitializeComponent();
			_searchBox = this.GetControl<TextBox>("Search");
			_listBox = this.GetControl<ListBox>("ListBox");
		}

		private void InitializeComponent()
		{
			AvaloniaXamlLoader.Load(this);
		}

		protected override void OnOpened(EventArgs e)
		{
			base.OnOpened(e);

			//Post this to allow focus to work properly when drag and dropping file
			Dispatcher.UIThread.Post(() => {
				Activate();
				_searchBox.Focus();
			});
		}

		protected override void OnKeyDown(KeyEventArgs e)
		{
			if(DataContext is SelectRomViewModel model) {
				if(e.Key == Key.Down || e.Key == Key.Up) {
					if(_searchBox.IsKeyboardFocusWithin) {
						if(model.FilteredEntries.Count() > 1) {
							model.SelectedEntry = model.FilteredEntries.ElementAt(1);
							_listBox.ContainerFromIndex(1)?.Focus();
						} else {
							model.SelectedEntry = model.FilteredEntries.ElementAt(0);
							_listBox.ContainerFromIndex(1)?.Focus();
						}
					}
				} else if(e.Key == Key.Enter && model.SelectedEntry != null) {
					model.Cancelled = false;
					Close();
				} else if(e.Key == Key.Escape) {
					Close();
				}
			}
			base.OnKeyDown(e);
		}

		//The archive's game to open. ADR-0249 (user decision 2026-10-03): in
		//Player mode the list is a sheet inside the main window
		//(PlaySelectRomSheetViewModel); Advanced keeps this window.
		public static async Task<ResourcePath?> Show(string file)
		{
			List<ArchiveRomEntry> entries = ArchiveHelper.GetArchiveRomList(file);
			switch(ArchiveRomPick.Route(entries.Count)) {
				case ArchiveRomRoute.WholeFile: return file;
				case ArchiveRomRoute.OnlyGame: return InnerFile(file, entries[0], 0);
			}

			Window? parent = ApplicationHelper.GetMainWindow();
			if(parent == null) {
				return null;
			}
			if(ArchiveRomPick.UsesSheet(PlayerDialogScope.PlayerMode, parent is MainWindow) && parent.DataContext is MainWindowViewModel main) {
				PlaySelectRomRow? row = await main.SelectRomSheet.Request(System.IO.Path.GetFileName(file), entries);
				if(row == null) {
					return null;
				}
				return InnerFile(file, row.Entry, row.Position);
			}

			SelectRomViewModel model = new(entries) { SelectedEntry = entries[0] };
			SelectRomWindow wnd = new SelectRomWindow() { DataContext = model };
			wnd.WindowStartupLocation = WindowStartupLocation.CenterOwner;
			await wnd.ShowDialog(parent);

			if(model.Cancelled || model.SelectedEntry == null) {
				return null;
			}
			return InnerFile(file, model.SelectedEntry, entries.IndexOf(model.SelectedEntry));
		}

		private static ResourcePath InnerFile(string file, ArchiveRomEntry entry, int position)
		{
			return new ResourcePath() { Path = file, InnerFile = entry.Filename, InnerFileIndex = ArchiveRomPick.InnerFileIndex(entry.IsUtf8, position) };
		}

		private void OnOkClick(object sender, RoutedEventArgs e)
		{
			if(DataContext is SelectRomViewModel model && model.SelectedEntry != null) {
				model.Cancelled = false;
				Close();
			}
		}

		private void OnCancelClick(object sender, RoutedEventArgs e)
		{
			Close();
		}

		bool _isDoubleTap = false;
		private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
		{
			if(_isDoubleTap) {
				if(DataContext is SelectRomViewModel model && model.SelectedEntry != null) {
					model.Cancelled = false;
					Close();
				}
				_isDoubleTap = false;
			}
		}

		private void OnDoubleTapped(object sender, TappedEventArgs e)
		{
			_isDoubleTap = true;
		}

		protected override void OnClosed(EventArgs e)
		{
			((SelectRomViewModel?)DataContext)?.Dispose();
			base.OnClosed(e);
		}
	}

	public partial class SelectRomViewModel : DisposableViewModel
	{
		private readonly List<ArchiveRomEntry> _entries;
		[ObservableProperty] public partial IEnumerable<ArchiveRomEntry> FilteredEntries { get; set; }
		[ObservableProperty] public partial string SearchString { get; set; } = "";
		[ObservableProperty] public partial ArchiveRomEntry? SelectedEntry { get; set; }
		[ObservableProperty] public partial bool Cancelled { get; set; } = true;

		public SelectRomViewModel(List<ArchiveRomEntry> entries)
		{
			_entries = entries;
			FilteredEntries = entries;
			SelectedEntry = FilteredEntries.FirstOrDefault();
		}

		partial void OnSearchStringChanged(string value)
		{
			FilteredEntries = _entries.Where(e => ArchiveRomPick.Matches(e.Filename, value));

			SelectedEntry = FilteredEntries.FirstOrDefault();
		}
	}
}
