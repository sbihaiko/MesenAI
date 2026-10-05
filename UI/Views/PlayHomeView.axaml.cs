using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Mesen.Utilities;
using Mesen.ViewModels;

namespace Mesen.Views
{
	//G.2 (W-P1/W-P2): thin code-behind - Open a ROM… opens the in-app ROM picker
	//(#845, ADR-0256 Decision 9), Continue resumes the most recent game exactly
	//like clicking its tile.
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

		//#845 (ADR-0256 Decision 9): the home's own action opens the in-app
		//picker instead of EmuApi.ExecuteShortcut(OpenFile). That reached
		//ShortcutHandler -> FileDialogHelper -> Avalonia's StorageProvider - a
		//native dialog that owns the screen once it opens, so the focus engine
		//(and the pad with it) could not drive the choice, and a machine with a
		//pad and nothing else could not load a game. Every other file choice in
		//the app, and Advanced's own Open, still go there.
		private void OnOpenRom(object? sender, RoutedEventArgs e)
		{
			if(Window() is MainWindowViewModel model) {
				model.OpenRomPicker();
			}
		}

		//The window this home belongs to. The home's own DataContext is the
		//recent-games list, so the sheet - one per window - is reached through
		//the window's; a home drawn outside a MainWindow (a render, a preview)
		//has no picker to open and says so by doing nothing.
		private MainWindowViewModel? Window()
		{
			return TopLevel.GetTopLevel(this)?.DataContext as MainWindowViewModel;
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
