using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Mesen.Logic;
using Mesen.ViewModels;
using System;

namespace Mesen.Windows
{
	//G.5 (PRD Part B §13.5.2 W-P12): shown once, before the main window, when
	//there is no settings file - the storage choice decides where that file
	//(and the core library) lives, so it runs before MainWindow can exist.
	public class SetupWizardWindow : MesenWindow
	{
		private readonly SetupWizardViewModel _model;
		private bool _confirmed;

		public SetupWizardWindow() : this(new SetupWizardViewModel())
		{
		}

		//The headless tests pass a model whose Confirm writes nothing.
		public SetupWizardWindow(SetupWizardViewModel model)
		{
			_model = model;
			DataContext = _model;
			InitializeComponent();
		}

		private void InitializeComponent()
		{
			AvaloniaXamlLoader.Load(this);
		}

		protected override void OnOpened(EventArgs e)
		{
			base.OnOpened(e);
			Dispatcher.UIThread.Post(() => this.GetControl<Button>("FirstRunStartPlaying").Focus());
		}

		protected override void OnKeyDown(KeyEventArgs e)
		{
			//Esc keeps the choice and continues, like the close button.
			if(e.Key == Key.Escape) {
				e.Handled = true;
				Close();
				return;
			}
			base.OnKeyDown(e);
		}

		protected override void OnClosing(WindowClosingEventArgs e)
		{
			base.OnClosing(e);
			if(_confirmed) {
				return;
			}
			//#661: quitting the app or shutting the OS down writes nothing and
			//is never cancelled; the sheet shows again next launch.
			bool shuttingDown = e.CloseReason is WindowCloseReason.ApplicationShutdown or WindowCloseReason.OSShutdown;
			if(!PlayFirstRun.ConfirmsOnClose(shuttingDown)) {
				return;
			}
			//The app cannot run without a storage choice: closing applies the
			//one on screen. A folder that cannot be written keeps the sheet
			//open with its sentence (W-X2).
			_confirmed = _model.Confirm();
			e.Cancel = !_confirmed;
		}

		private void BtnStart_OnClick(object? sender, RoutedEventArgs e)
		{
			Close();
		}
	}
}
