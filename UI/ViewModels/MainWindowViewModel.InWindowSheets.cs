using CommunityToolkit.Mvvm.ComponentModel;
using Mesen.Config;
using System.IO;

namespace Mesen.ViewModels
{
	//ADR-0249 (user decision 2026-10-03, "Virar sheets"): the last two classic
	//windows a Player flow opened are sheets inside this window - an archive's
	//game list (W-P5 sheet shape) and Look's Adjust… (the shader's parameters,
	//over W-P10). Advanced keeps SelectRomWindow and ShaderConfigWindow.
	public partial class MainWindowViewModel
	{
		public PlaySelectRomSheetViewModel SelectRomSheet { get; } = new();

		//ADR-0250 Decision 3: a task door's About, Command Line, Check for
		//Updates, video recorder settings and barcode (PlayerToolSheetView).
		public PlayerToolSheetViewModel ToolSheet { get; } = new();

		//Look's Adjust…: the shader's parameters, previewed live as the classic
		//window did (ShaderConfigViewModel's allowPreview). Shown over the
		//Settings sheet, which stays open beneath and comes back on close.
		[ObservableProperty, NotifyPropertyChangedFor(nameof(IsShaderSheetVisible), nameof(ShaderSheetTitle))]
		public partial ShaderConfigViewModel? ShaderSheet { get; private set; }

		public bool IsShaderSheetVisible => ShaderSheet != null;

		public string ShaderSheetTitle => ShaderSheet == null ? "" : Path.GetFileName(ShaderSheet.Config.ShaderFile);

		public void OpenShaderSheet(ShaderConfigViewModel shader)
		{
			CloseShaderSheet(false);
			ShaderSheet = shader;
		}

		//OK saves the parameters to the shader's file; Cancel and Esc do not.
		//Either way the video config is applied again (ShaderConfigWindow.OnClosed),
		//which drops a preview that was not saved.
		public void CloseShaderSheet(bool save)
		{
			ShaderConfigViewModel? shader = ShaderSheet;
			if(shader == null) {
				return;
			}
			ShaderSheet = null;
			if(save) {
				shader.Save();
			}
			shader.Dispose();
			ConfigManager.Config.Video.ApplyConfig();
		}

		//Esc on one of these sheets closes it: the archive opens nothing, the
		//shader's parameters return to Settings unsaved. True when Esc was taken.
		private bool HandleInWindowSheetEsc()
		{
			if(SelectRomSheet.IsVisible) {
				SelectRomSheet.Cancel();
				return true;
			}
			if(IsShaderSheetVisible) {
				CloseShaderSheet(false);
				return true;
			}
			if(ToolSheet.IsVisible) {
				ToolSheet.Close();
				return true;
			}
			return false;
		}
	}
}
