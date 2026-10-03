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

		public SelectRomWindow() : this(false)
		{
		}

		//playerLook (ADR-0249, UI/Logic/PlayerDialog): the W-P5 sheet shape
		//instead of the classic dialog - same view-model, keys and double-click.
		public SelectRomWindow(bool playerLook)
		{
			InitializeComponent();

			if(playerLook) {
				Border root = this.GetControl<Border>("PlayerSelectRomRoot");
				root.Classes.Add("player");
				root.IsVisible = true;
				this.GetControl<DockPanel>("ClassicSelectRomRoot").IsVisible = false;
			}
			_searchBox = this.GetControl<TextBox>(playerLook ? "PlayerSelectRomSearch" : "Search");
			_listBox = this.GetControl<ListBox>(playerLook ? "PlayerSelectRomList" : "ListBox");
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

		public static async Task<ResourcePath?> Show(string file)
		{
			List<ArchiveRomEntry> entries = ArchiveHelper.GetArchiveRomList(file);
			if(entries.Count == 0) {
				return file;
			} else if(entries.Count == 1) {
				return new ResourcePath() { Path = file, InnerFile = entries[0].Filename, InnerFileIndex = entries[0].IsUtf8 ? 0 : 1 };
			}

			SelectRomViewModel model = new(entries) { SelectedEntry = entries[0] };
			Window? parent = ApplicationHelper.GetMainWindow();
			if(parent == null) {
				return null;
			}
			bool playerLook = PlayerDialogScope.UsesPlayerLook(parent);
			SelectRomWindow wnd = new SelectRomWindow(playerLook) { DataContext = model };
			if(playerLook) {
				wnd.GetControl<TextBlock>("PlayerSelectRomTitle").Text = ResourceHelper.GetMessage("SelectRomPlayerTitle", System.IO.Path.GetFileName(file));
			}

			wnd.WindowStartupLocation = WindowStartupLocation.CenterOwner;
			await wnd.ShowDialog(parent);

			if(model.Cancelled || model.SelectedEntry == null) {
				return null;
			}

			int innerFileIndex = 0;
			if(!model.SelectedEntry.IsUtf8) {
				innerFileIndex = entries.IndexOf(model.SelectedEntry) + 1;
			}

			return new ResourcePath() { Path = file, InnerFile = model.SelectedEntry.Filename, InnerFileIndex = innerFileIndex };
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
			if(string.IsNullOrWhiteSpace(value)) {
				FilteredEntries = _entries;
			} else {
				FilteredEntries = _entries.Where(e => e.Filename.Contains(value, StringComparison.OrdinalIgnoreCase));
			}

			SelectedEntry = FilteredEntries.FirstOrDefault();
		}
	}
}
