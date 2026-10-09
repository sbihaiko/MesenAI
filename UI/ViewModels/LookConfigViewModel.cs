using CommunityToolkit.Mvvm.ComponentModel;
using Mesen.Config;
using Mesen.Interop;
using Mesen.Localization;
using Mesen.Logic;
using Mesen.Utilities;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Mesen.ViewModels;

//One row of the Pixels or Screen popup.
public sealed class LookChoice
{
	public string Label { get; init; } = "";
	public bool IsEnabled { get; init; } = true;
	public PixelsItem? Pixels { get; init; }
	public ScreenItem? Screen { get; init; }
}

//ADR-0246 (P.13): Settings › Look. Every rule lives in Logic/LookLayers; this
//class reads the inputs (config, loaded game, pack, shader support, bundled
//looks), shows LookLayers.Build's view and writes what LookLayers.Apply*
//returns. Nothing here decides a rule.
public partial class LookConfigViewModel : DisposableViewModel
{
	public VideoConfig Config { get; }

	[ObservableProperty] public partial bool HasArt { get; private set; }
	[ObservableProperty] public partial string ArtText { get; private set; } = "";

	[ObservableProperty] public partial List<LookChoice> PixelsChoices { get; private set; } = new();
	[ObservableProperty] public partial LookChoice? SelectedPixels { get; set; }
	[ObservableProperty] public partial bool PixelsEnabled { get; private set; }
	[ObservableProperty] public partial string PixelsReason { get; private set; } = "";
	[ObservableProperty] public partial string PixelsMark { get; private set; } = "";

	[ObservableProperty] public partial List<LookChoice> ScreenChoices { get; private set; } = new();
	[ObservableProperty] public partial LookChoice? SelectedScreen { get; set; }
	[ObservableProperty] public partial string ScreenNote { get; private set; } = "";
	[ObservableProperty] public partial string ScreenMark { get; private set; } = "";

	[ObservableProperty] public partial bool CanAdjust { get; private set; }
	[ObservableProperty] public partial bool CanCompare { get; private set; }
	[ObservableProperty] public partial string CompareReason { get; private set; } = "";
	//#1149 (review of #1163): the other line this tab can carry. The footer keeps
	//the note's line at the taller of the two, so toggling Pixels or Screen -
	//which swaps the hint and the reason - cannot move the footer and the rows
	//above it. Empty where the tab has neither to say, and then nothing is kept.
	[ObservableProperty] public partial string CompareNoteReserve { get; private set; } = "";
	[ObservableProperty] public partial bool IsComparing { get; private set; }

	//Set by ConfigViewModel: "More in Options…" switches to the Video tab.
	public Action<ConfigWindowTab>? OpenTab { get; set; }
	//Set by the view: the file dialog behind "Choose a shader file…".
	public Func<System.Threading.Tasks.Task<string?>>? PickShaderFile { get; set; }
	//Whether the core draws a pack's art now (ADR-0246 §3); headless tests swap it.
	public Func<bool> DrawingPackArt { get; set; } = EmuApi.IsDrawingPackArt;

	private LookInput _input;
	private bool _refreshing;

	public LookConfigViewModel()
	{
		Config = ConfigManager.Config.Video;
		_input = ReadInput();
		Refresh();

		if(Avalonia.Controls.Design.IsDesignMode) {
			return;
		}

		AddDisposable(Config.ObserveProp(nameof(VideoConfig.VideoFilter), Refresh));
		AddDisposable(Config.ObserveProp(nameof(VideoConfig.ShaderFile), Refresh));
		AddDisposable(MainWindowViewModel.Instance.ObserveProp(nameof(MainWindowViewModel.RomInfo), Refresh));
		AddDisposable(MainWindowViewModel.Instance.ObserveProp(nameof(MainWindowViewModel.CurrentPackName), Refresh));
		//#663: a recording stops and restarts the pack's art with the same game and pack.
		PackArtSwitch.Switched += Refresh;
		AddDisposable(new DisposableObserver(() => PackArtSwitch.Switched -= Refresh));
	}

	public void Refresh()
	{
		if(_refreshing) {
			return;
		}
		_refreshing = true;
		try {
			_input = ReadInput();
			LookView view = LookLayers.Build(_input);

			HasArt = view.HasArt;
			ArtText = !view.HasArt ? ResourceHelper.GetMessage("LookArtNone")
				: view.PackName != "" ? view.PackName : ResourceHelper.GetMessage("LookArtUnnamedPack");

			PixelsChoices = view.PixelsItems.Select(p => new LookChoice { Label = PixelsLabel(p), IsEnabled = view.PixelsEnabled, Pixels = p }).ToList();
			SelectedPixels = PixelsChoices.First(c => c.Pixels == view.PixelsSelected);
			PixelsEnabled = view.PixelsEnabled;
			PixelsReason = ReasonText(view.PixelsReason);
			PixelsMark = MarkText(view.PixelsCapture);

			ScreenChoices = view.ScreenItems.Select(s => new LookChoice { Label = ScreenLabel(s), IsEnabled = s.IsEnabled, Screen = s }).ToList();
			SelectedScreen = ScreenChoices.First(c => c.Screen == view.ScreenSelected);
			ScreenNote = ReasonText(view.ScreenNote);
			ScreenMark = MarkText(view.ScreenCapture);

			CanAdjust = view.CanAdjust;
			CanCompare = view.CanCompare;
			//The tab's two lines: the hint while Pixels or Screen has something to
			//compare, and the reason it gives while neither has (LookLayers.Build's
			//CompareReason). The footer keeps the note's line at the taller.
			string hint = ResourceHelper.GetMessage("LookCompareHint");
			string nothing = ReasonText(LookReason.NothingToCompare);
			CompareReason = view.CanCompare ? hint : nothing;
			CompareNoteReserve = view.CanCompare ? nothing : hint;
		} finally {
			_refreshing = false;
		}
	}

	partial void OnSelectedPixelsChanged(LookChoice? value)
	{
		if(_refreshing || value?.Pixels == null) {
			return;
		}
		if(value.Pixels.Kind == PixelsItemKind.MoreInOptions) {
			//After the selection change: Refresh replaces the list the ComboBox
			//is still committing, which threw in Avalonia's SelectionModel.
			Avalonia.Threading.Dispatcher.UIThread.Post(() => {
				Refresh();
				OpenTab?.Invoke(ConfigWindowTab.Video);
			});
			return;
		}
		Write(LookLayers.ApplyPixels(_input, value.Pixels));
	}

	partial void OnSelectedScreenChanged(LookChoice? value)
	{
		if(_refreshing || value?.Screen == null) {
			return;
		}
		if(value.Screen.Kind == ScreenItemKind.ChooseFile) {
			Refresh();
			if(value.Screen.IsEnabled) {
				ChooseShaderFile();
			}
			return;
		}
		if(value.Screen.Kind == ScreenItemKind.ShaderFile && value.Screen.IsEnabled) {
			ConfigManager.Config.RecentFiles.AddRecentShader(value.Screen.ShaderFile);
		}
		Write(LookLayers.ApplyScreen(_input, value.Screen));
	}

	private async void ChooseShaderFile()
	{
		if(PickShaderFile == null) {
			return;
		}
		string? file = await PickShaderFile();
		if(file == null) {
			return;
		}
		ConfigManager.Config.RecentFiles.AddRecentShader(file);
		Write(LookLayers.ApplyShaderFile(ReadInput(), file));
	}

	//Hold to Compare (ADR-0246 §5): Pixels and Screen drop while held, Art
	//stays. The renderer bypasses the shader chain rather than rebuilding it
	//(the measured swap stutters), and the paused frame is redrawn.
	public void SetCompare(bool compare)
	{
		if(compare && !CanCompare) {
			return;
		}
		if(IsComparing == compare) {
			return;
		}
		IsComparing = compare;
		EmuApi.SetLookCompare(compare);
	}

	private void Write((string VideoFilter, string ShaderFile) next)
	{
		VideoFilterType filter = Enum.TryParse(next.VideoFilter, out VideoFilterType parsed) ? parsed : Config.VideoFilter;
		bool changed = filter != Config.VideoFilter || next.ShaderFile != Config.ShaderFile;
		_refreshing = true;
		try {
			Config.VideoFilter = filter;
			Config.ShaderFile = next.ShaderFile;
		} finally {
			_refreshing = false;
		}
		if(changed) {
			Config.ApplyConfig();
			EmuApi.RedrawPausedFrame();
		}
		Refresh();
	}

	private LookInput ReadInput()
	{
		RomInfo rom = MainWindowViewModel.Instance.RomInfo;
		bool gameLoaded = rom.Format != RomFormat.Unknown;
		bool packArt = gameLoaded && !Avalonia.Controls.Design.IsDesignMode && DrawingPackArt();
		ShaderAvailability shaders = Avalonia.Controls.Design.IsDesignMode ? ShaderAvailability.Available : ConfigApi.GetShaderAvailability();
		return new LookInput(
			VideoFilter: Config.VideoFilter.ToString(),
			ShaderFile: Config.ShaderFile ?? "",
			Console: gameLoaded ? rom.ConsoleType : null,
			PackDrawsArt: packArt,
			PackName: MainWindowViewModel.Instance.CurrentPackName,
			Shaders: shaders,
			NamedLooks: LoadNamedLooks(),
			RecentShaders: ConfigManager.Config.RecentFiles.Shaders.Where(File.Exists).ToList());
	}

	//The bundled looks whose preset was extracted (Dependencies.zip → <home>/Shaders/Looks).
	private static IReadOnlyList<NamedLookEntry> LoadNamedLooks()
	{
		string folder = Path.Combine(ConfigManager.ShaderFolder, NamedLookManifest.FolderName);
		string manifest = Path.Combine(folder, NamedLookManifest.FileName);
		if(!File.Exists(manifest)) {
			return Array.Empty<NamedLookEntry>();
		}
		try {
			IReadOnlyList<NamedLook> looks = NamedLookManifest.Parse(File.ReadAllText(manifest));
			return NamedLookManifest.Entries(looks, folder).Where(e => File.Exists(e.PresetPath)).ToList();
		} catch(IOException) {
			return Array.Empty<NamedLookEntry>();
		}
	}

	private static string PixelsLabel(PixelsItem item)
	{
		return item.Kind switch {
			PixelsItemKind.Sharp => ResourceHelper.GetMessage("LookPixelsSharp"),
			PixelsItemKind.SmoothHq4x => ResourceHelper.GetMessage("LookPixelsHq4x"),
			PixelsItemKind.SmoothXbrz4x => ResourceHelper.GetMessage("LookPixelsXbrz4x"),
			PixelsItemKind.MoreInOptions => ResourceHelper.GetMessage("LookPixelsMore"),
			_ => ResourceHelper.GetMessage("LookCurrentFromOptions", FilterText(item.Filter))
		};
	}

	private static string ScreenLabel(ScreenItem item)
	{
		string label = item.Kind switch {
			ScreenItemKind.None => ResourceHelper.GetMessage("LookScreenNone"),
			ScreenItemKind.Ntsc => ResourceHelper.GetMessage("LookScreenNtsc"),
			ScreenItemKind.ChooseFile => ResourceHelper.GetMessage("LookScreenChooseFile"),
			ScreenItemKind.Current => ResourceHelper.GetMessage("LookCurrentFromOptions", CurrentScreenText(item)),
			_ => item.Label
		};
		string reason = !item.IsEnabled || item.Kind == ScreenItemKind.Ntsc ? ReasonText(item.Reason) : "";
		return reason == "" ? label : label + " — " + reason;
	}

	private static string CurrentScreenText(ScreenItem item)
	{
		List<string> parts = new();
		if(LookLayers.LayerOf(item.Filter) == LookFilterLayer.Screen) {
			parts.Add(FilterText(item.Filter));
		}
		if(item.ShaderFile != "") {
			parts.Add(item.Label);
		}
		return string.Join(" + ", parts);
	}

	private static string FilterText(string filter)
	{
		return Enum.TryParse(filter, out VideoFilterType parsed) ? ResourceHelper.GetEnumText(parsed) : filter;
	}

	private static string MarkText(LookCapture capture)
	{
		return capture switch {
			LookCapture.Captured => ResourceHelper.GetMessage("LookMarkCaptured"),
			LookCapture.DisplayOnly => ResourceHelper.GetMessage("LookMarkDisplayOnly"),
			LookCapture.Mixed => ResourceHelper.GetMessage("LookMarkMixed"),
			_ => ResourceHelper.GetMessage("LookMarkNone")
		};
	}

	private static string ReasonText(LookReason reason)
	{
		return reason == LookReason.None ? "" : ResourceHelper.GetMessage("LookReason" + reason);
	}

	//A held compare never outlives the window.
	protected override void DisposeView()
	{
		SetCompare(false);
	}
}
