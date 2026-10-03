using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Mesen.Logic;
using Mesen.ViewModels;
using System.Collections;

namespace Mesen.Views
{
	//G.1 (PRD Part B §13.5.1, W-S1-W-S3): code-behind of the shell bar. The
	//workspace rules are host-free (UI/Logic/WorkspaceShell.cs); this only
	//routes clicks to MainWindowViewModel.SelectWorkspace and keeps the door's
	//Tools ⋯ current when it opens.
	public class WorkspaceShellBar : UserControl
	{
		public Menu ToolsMenu { get; }

		public WorkspaceShellBar()
		{
			InitializeComponent();
			ToolsMenu = this.GetControl<Menu>("ToolsMenu");
		}

		//Leading room for the macOS traffic lights (ShellTitleBar.LeadingInset);
		//the profile button keeps its own 12 px margin after it.
		public void SetLeadingInset(double inset)
		{
			Button profile = this.GetControl<Button>("ProfileButton");
			profile.Margin = new Thickness(12 + inset, 0, 0, 0);
		}

		private void InitializeComponent()
		{
			AvaloniaXamlLoader.Load(this);
		}

		private void OnPickWorkspace(object? sender, RoutedEventArgs e)
		{
			if(sender is Button button && button.Tag is Workspace workspace && DataContext is MainWindowViewModel model) {
				model.SelectWorkspace(workspace);
			}
			this.GetControl<Button>("ProfileButton").Flyout?.Hide();
		}

		//When Tools ⋯ opens: Play's console items follow the loaded game
		//(ADR-0250), and Share's netplay "Select controller" submenu is rebuilt,
		//as MainMenuView.MnuTools_Opened does for the classic bar.
		private void MnuTools_Opened(object? sender, RoutedEventArgs e)
		{
			if(DataContext is not MainWindowViewModel model || e.Source is not MenuItem item || item.Name != "ToolsMenuButton") {
				return;
			}
			bool rebuilt = model.MainMenu.RefreshDoorMenu();
			if(model.MainMenu.UpdateNetplayMenu() && !rebuilt) {
				IEnumerable? items = item.ItemsSource;
				item.ItemsSource = null;
				item.ItemsSource = items;
			}
		}
	}
}
