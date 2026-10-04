using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Mesen.Config.Shortcuts;
using Mesen.Interop;
using Mesen.Utilities;
using Mesen.ViewModels;

namespace Mesen.Views
{
	//G.2 (W-P1/W-P2): thin code-behind - Open a ROM… reuses the existing
	//Open-ROM shortcut/dialog (ShortcutHandler.OpenFile), Continue resumes the
	//most recent game exactly like clicking its tile.
	public class PlayHomeView : UserControl
	{
		public PlayHomeView()
		{
			InitializeComponent();
		}

		private void InitializeComponent()
		{
			AvaloniaXamlLoader.Load(this);
		}

		private RecentGamesViewModel? Model => DataContext as RecentGamesViewModel;

		//ADR-0256 Decision 3: the home keeps its own triggers - it is the one that
		//knows it has appeared - and gives up the decision. It used to pick its
		//own first control and carry its own "has another surface already taken
		//the focus" test (#625), which was the same question the pause overlay,
		//the sheets and the slot grid were each answering for themselves.
		//PlayFocusOnOpen answers it once, for all of them, so a surface that is up
		//wins however the two posts interleave.
		protected override void OnDataContextChanged(System.EventArgs e)
		{
			base.OnDataContextChanged(e);
			PlayFocusOnOpen.ContentChanged(this);
		}

		protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
		{
			base.OnAttachedToVisualTree(e);
			PlayFocusOnOpen.ContentChanged(this);
		}

		private void OnOpenRom(object? sender, RoutedEventArgs e)
		{
			EmuApi.ExecuteShortcut(new ExecuteShortcutParams() { Shortcut = EmulatorShortcut.OpenFile });
		}

		//G.5 (W-P14): the alert's next step is the same Open a ROM… dialog.
		private void OnOpenAnother(object? sender, RoutedEventArgs e)
		{
			Model?.OnOpenStarted();
			OnOpenRom(sender, e);
		}

		private void OnContinue(object? sender, RoutedEventArgs e)
		{
			if(Model?.GameEntries.Count > 0) {
				Model.GameEntries[0].Load();
			}
		}
	}
}
