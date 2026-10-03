using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Mesen.Config;
using Mesen.Interop;
using Mesen.Utilities;
using Mesen.ViewModels;
using Mesen.Windows;
using System.IO;

namespace Mesen.Views
{
	public class LookConfigView : UserControl
	{
		public LookConfigView()
		{
			InitializeComponent();

			//Hold to Compare: pressed/released, not clicked. Button marks the
			//pointer events handled, so listen to handled ones too.
			Button compare = this.GetControl<Button>("btnLookHoldToCompare");
			compare.AddHandler(PointerPressedEvent, (s, e) => SetCompare(true), RoutingStrategies.Tunnel | RoutingStrategies.Bubble, handledEventsToo: true);
			compare.AddHandler(PointerReleasedEvent, (s, e) => SetCompare(false), RoutingStrategies.Tunnel | RoutingStrategies.Bubble, handledEventsToo: true);
			compare.AddHandler(PointerCaptureLostEvent, (s, e) => SetCompare(false), RoutingStrategies.Direct | RoutingStrategies.Bubble, handledEventsToo: true);
			compare.AddHandler(KeyDownEvent, (s, e) => { if(e.Key == Key.Space) { SetCompare(true); } }, RoutingStrategies.Tunnel | RoutingStrategies.Bubble, handledEventsToo: true);
			compare.AddHandler(KeyUpEvent, (s, e) => { if(e.Key == Key.Space) { SetCompare(false); } }, RoutingStrategies.Tunnel | RoutingStrategies.Bubble, handledEventsToo: true);
			compare.LostFocus += (s, e) => SetCompare(false);
		}

		private void InitializeComponent()
		{
			AvaloniaXamlLoader.Load(this);
		}

		protected override void OnDataContextChanged(System.EventArgs e)
		{
			base.OnDataContextChanged(e);
			if(DataContext is LookConfigViewModel model) {
				model.PickShaderFile = () => FileDialogHelper.OpenFile(ConfigManager.ShaderFolder, TopLevel.GetTopLevel(this) as Window, FileDialogHelper.ShaderExt);
			}
		}

		protected override void OnDetachedFromVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
		{
			SetCompare(false);
			base.OnDetachedFromVisualTree(e);
		}

		private void SetCompare(bool compare)
		{
			(DataContext as LookConfigViewModel)?.SetCompare(compare);
		}

		//ADR-0249 (W-P10 › W-P6): in Player mode (Look on the main window's
		//Settings sheet) the Art row swaps that sheet for the pack detail sheet,
		//as the pause overlay's Pack row opens it. The classic Options window
		//keeps Enhancement Packs.
		private void OnPackDetails(object? sender, RoutedEventArgs e)
		{
			if(PlayerDialogScope.UsesPlayerLook(this) && TopLevel.GetTopLevel(this) is MainWindow main) {
				main.OpenPackDetailFromSettings();
				return;
			}
			ApplicationHelper.GetOrCreateUniqueWindow(this, () => new EnhancementPacksWindow());
		}

		//The shader's own parameters (ShaderConfigWindow), in the Player look
		//when Look is the Player sheet's (ADR-0249).
		private void OnAdjust(object? sender, RoutedEventArgs e)
		{
			string shader = ConfigManager.Config.Video.ShaderFile;
			if(!File.Exists(shader)) {
				return;
			}
			new ShaderConfigWindow(PlayerDialogScope.UsesPlayerLook(this)) {
				DataContext = new ShaderConfigViewModel(true, shader)
			}.ShowCenteredDialog((Control)this);
		}
	}
}
