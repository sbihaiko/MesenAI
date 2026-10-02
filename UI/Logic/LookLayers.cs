using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mesen.Interop;

namespace Mesen.Logic;

//ADR-0246 (P.13, PRD Part B §13 W-P10): Settings › Look names the three
//things that change the picture by what they change, in the order they apply:
//  Art    - the pack (read-only here);
//  Pixels - the scale-filter family of VideoConfig.VideoFilter (CPU, captured);
//  Screen - a .slangp shader (GPU, display only) or the NTSC/LcdGrid console
//           filters (CPU, captured).
//Host-free (ADR-0123): VideoFilterType is not part of the UI.Tests
//dual-compile, so filters travel as their enum names; the ViewModel parses
//them. Note that Pixels and the NTSC/LcdGrid half of Screen share that one
//config field, so picking one of them replaces the other.

public enum LookFilterLayer { None, Pixels, Screen }

//Where a choice's result goes (ADR-0246 §2).
public enum LookCapture
{
	None,        //adds nothing to the picture
	Captured,    //◉ shows in screenshots and videos
	DisplayOnly, //◌ only on your display
	Mixed        //an NTSC/LcdGrid filter and a shader at once (set in Options)
}

public enum ShaderAvailability { Available, NotInBuild, NeedsMetalRenderer }

//The reason line a disabled or qualified control shows (simplicity rule 4).
public enum LookReason
{
	None,
	PackDrawsArt,
	ShadersNotInBuild,
	ShadersNeedMetalRenderer,
	NtscNesOnly,
	NtscNotAppliedUnderPack,
	NothingToCompare
}

public enum PixelsItemKind { Sharp, SmoothHq4x, SmoothXbrz4x, MoreInOptions, Current }

public enum ScreenItemKind { None, Ntsc, NamedLook, ShaderFile, ChooseFile, Current }

public sealed record NamedLookEntry(string Id, string Name, string PresetPath);

//Console is null when no game is loaded.
public sealed record LookInput(
	string VideoFilter,
	string ShaderFile,
	ConsoleType? Console,
	bool PackDrawsArt,
	string PackName,
	ShaderAvailability Shaders,
	IReadOnlyList<NamedLookEntry> NamedLooks,
	IReadOnlyList<string> RecentShaders);

//Filter: the VideoFilterType name the item stands for (the current value for
//Current; "" for MoreInOptions).
public sealed record PixelsItem(PixelsItemKind Kind, string Filter, LookCapture Capture);

//Label: the look's name, the shader file's name, or "" for the fixed items.
//Filter/ShaderFile: what the item stands for (both current values for Current).
public sealed record ScreenItem(ScreenItemKind Kind, string Label, string Filter, string ShaderFile, bool IsEnabled, LookReason Reason, LookCapture Capture);

public sealed record LookView(
	bool HasArt,
	string PackName,
	IReadOnlyList<PixelsItem> PixelsItems,
	PixelsItem PixelsSelected,
	bool PixelsEnabled,
	LookReason PixelsReason,
	LookCapture PixelsCapture,
	IReadOnlyList<ScreenItem> ScreenItems,
	ScreenItem ScreenSelected,
	LookReason ScreenNote,
	LookCapture ScreenCapture,
	bool CanAdjust,
	bool CanCompare,
	LookReason CompareReason);

public static class LookLayers
{
	public const string NoFilter = "None";
	public const string Hq4x = "HQ4x";
	public const string Xbrz4x = "xBRZ4x";
	public const string NtscBlargg = "NtscBlargg";
	public const string NtscBisqwit = "NtscBisqwit";
	public const string LcdGrid = "LcdGrid";

	//Recent shader files offered after the named looks; the rest stay in the
	//classic shader menu (Tools ⋯ › Options).
	public const int MaxRecentShaders = 3;

	public static LookFilterLayer LayerOf(string filter)
	{
		return filter switch {
			NoFilter or "" => LookFilterLayer.None,
			NtscBlargg or NtscBisqwit or LcdGrid => LookFilterLayer.Screen,
			_ => LookFilterLayer.Pixels
		};
	}

	//ADR-0246 §6: support is decided at startup (ConfigApi caches it), so on
	//macOS the software renderer reads "needs the Metal renderer" rather than
	//"not in this build", whatever the library answered.
	public static ShaderAvailability ResolveShaderAvailability(bool coreReportsShaderSupport, bool isMacOs, bool usesSoftwareRenderer)
	{
		if(isMacOs && usesSoftwareRenderer) {
			return ShaderAvailability.NeedsMetalRenderer;
		}
		return coreReportsShaderSupport ? ShaderAvailability.Available : ShaderAvailability.NotInBuild;
	}

	public static LookView Build(LookInput input)
	{
		(List<PixelsItem> pixelsItems, PixelsItem pixelsSelected) = BuildPixels(input);
		(List<ScreenItem> screenItems, ScreenItem screenSelected) = BuildScreen(input);

		bool pixelsEnabled = !input.PackDrawsArt;
		bool hasShader = input.ShaderFile != "";
		bool shaderRuns = hasShader && input.Shaders == ShaderAvailability.Available;
		bool hasNtsc = IsNtsc(input.VideoFilter);

		LookReason screenNote = LookReason.None;
		if(hasShader && input.Shaders != ShaderAvailability.Available) {
			screenNote = ShaderReason(input.Shaders);
		} else if(hasNtsc && input.PackDrawsArt) {
			screenNote = LookReason.NtscNotAppliedUnderPack;
		}

		//Hold to Compare drops Pixels and Screen; Art stays (ADR-0246 §5). A
		//scale filter runs even over pack art (Options can set one), NTSC does not.
		bool pixelsRun = LayerOf(input.VideoFilter) == LookFilterLayer.Pixels;
		bool screenFilterRuns = input.VideoFilter == LcdGrid || (hasNtsc && !input.PackDrawsArt);
		bool canCompare = pixelsRun || screenFilterRuns || shaderRuns;

		return new LookView(
			HasArt: input.PackDrawsArt,
			PackName: input.PackDrawsArt ? input.PackName : "",
			PixelsItems: pixelsItems,
			PixelsSelected: pixelsSelected,
			PixelsEnabled: pixelsEnabled,
			PixelsReason: pixelsEnabled ? LookReason.None : LookReason.PackDrawsArt,
			PixelsCapture: pixelsSelected.Capture,
			ScreenItems: screenItems,
			ScreenSelected: screenSelected,
			ScreenNote: screenNote,
			ScreenCapture: screenSelected.Capture,
			CanAdjust: shaderRuns,
			CanCompare: canCompare,
			CompareReason: canCompare ? LookReason.None : LookReason.NothingToCompare);
	}

	//What picking `item` in Pixels writes: (VideoFilter, ShaderFile). Look never
	//writes while Pixels is disabled (pack art), and re-picking the current item
	//or "More in Options…" writes nothing (restore-not-clobber, PRD §6.1).
	public static (string VideoFilter, string ShaderFile) ApplyPixels(LookInput input, PixelsItem item)
	{
		(string, string) unchanged = (input.VideoFilter, input.ShaderFile);
		if(input.PackDrawsArt) {
			return unchanged;
		}
		return item.Kind switch {
			PixelsItemKind.Sharp => LayerOf(input.VideoFilter) == LookFilterLayer.Pixels ? (NoFilter, input.ShaderFile) : unchanged,
			PixelsItemKind.SmoothHq4x => (Hq4x, input.ShaderFile),
			PixelsItemKind.SmoothXbrz4x => (Xbrz4x, input.ShaderFile),
			_ => unchanged
		};
	}

	//What picking `item` in Screen writes. Screen is one effect: NTSC clears the
	//shader, a shader clears an NTSC/LcdGrid filter, None clears both. A Pixels
	//filter is another layer and is kept by a shader or None. Disabled and
	//current items write nothing.
	public static (string VideoFilter, string ShaderFile) ApplyScreen(LookInput input, ScreenItem item)
	{
		(string, string) unchanged = (input.VideoFilter, input.ShaderFile);
		if(!item.IsEnabled) {
			return unchanged;
		}
		return item.Kind switch {
			ScreenItemKind.None => (WithoutScreenFilter(input.VideoFilter), ""),
			ScreenItemKind.Ntsc => (NtscBlargg, ""),
			ScreenItemKind.NamedLook or ScreenItemKind.ShaderFile => ApplyShaderFile(input, item.ShaderFile),
			_ => unchanged
		};
	}

	//*Choose a shader file…* returns here with the picked path.
	public static (string VideoFilter, string ShaderFile) ApplyShaderFile(LookInput input, string shaderFile)
	{
		if(input.Shaders != ShaderAvailability.Available || shaderFile == "") {
			return (input.VideoFilter, input.ShaderFile);
		}
		return (WithoutScreenFilter(input.VideoFilter), shaderFile);
	}

	private static (List<PixelsItem>, PixelsItem) BuildPixels(LookInput input)
	{
		List<PixelsItem> items = new() {
			new(PixelsItemKind.Sharp, NoFilter, LookCapture.Captured),
			new(PixelsItemKind.SmoothHq4x, Hq4x, LookCapture.Captured),
			new(PixelsItemKind.SmoothXbrz4x, Xbrz4x, LookCapture.Captured),
			new(PixelsItemKind.MoreInOptions, "", LookCapture.Captured)
		};

		PixelsItem selected;
		if(LayerOf(input.VideoFilter) != LookFilterLayer.Pixels) {
			selected = items[0];
		} else if(input.VideoFilter == Hq4x) {
			selected = items[1];
		} else if(input.VideoFilter == Xbrz4x) {
			selected = items[2];
		} else {
			selected = new(PixelsItemKind.Current, input.VideoFilter, LookCapture.Captured);
			items.Insert(0, selected);
		}
		return (items, selected);
	}

	private static (List<ScreenItem>, ScreenItem) BuildScreen(LookInput input)
	{
		bool shadersOk = input.Shaders == ShaderAvailability.Available;
		LookReason shaderReason = shadersOk ? LookReason.None : ShaderReason(input.Shaders);
		bool ntscAllowed = input.Console == null || input.Console == ConsoleType.Nes;
		LookReason ntscReason = !ntscAllowed ? LookReason.NtscNesOnly : input.PackDrawsArt ? LookReason.NtscNotAppliedUnderPack : LookReason.None;

		List<ScreenItem> items = new() {
			new(ScreenItemKind.None, "", NoFilter, "", true, LookReason.None, LookCapture.None),
			new(ScreenItemKind.Ntsc, "", NtscBlargg, "", ntscAllowed, ntscReason, LookCapture.Captured)
		};
		foreach(NamedLookEntry look in input.NamedLooks) {
			items.Add(new(ScreenItemKind.NamedLook, look.Name, "", look.PresetPath, shadersOk, shaderReason, LookCapture.DisplayOnly));
		}

		bool isNamed(string path) => input.NamedLooks.Any(l => SamePath(l.PresetPath, path));
		List<string> files = new();
		if(input.ShaderFile != "" && !isNamed(input.ShaderFile)) {
			files.Add(input.ShaderFile);
		}
		foreach(string recent in input.RecentShaders) {
			if(files.Count >= MaxRecentShaders) {
				break;
			}
			if(recent != "" && !isNamed(recent) && !files.Any(f => SamePath(f, recent))) {
				files.Add(recent);
			}
		}
		foreach(string file in files) {
			items.Add(new(ScreenItemKind.ShaderFile, Path.GetFileNameWithoutExtension(file), "", file, shadersOk, shaderReason, LookCapture.DisplayOnly));
		}
		items.Add(new(ScreenItemKind.ChooseFile, "", "", "", shadersOk, shaderReason, LookCapture.DisplayOnly));

		ScreenItem? selected = null;
		bool hasShader = input.ShaderFile != "";
		bool hasScreenFilter = LayerOf(input.VideoFilter) == LookFilterLayer.Screen;
		bool ntscItemMatches = input.VideoFilter == NtscBlargg && ntscAllowed;
		if(hasShader && !hasScreenFilter) {
			selected = items.First(i => (i.Kind == ScreenItemKind.NamedLook || i.Kind == ScreenItemKind.ShaderFile) && SamePath(i.ShaderFile, input.ShaderFile));
		} else if(!hasShader && ntscItemMatches) {
			selected = items[1];
		} else if(!hasShader && !hasScreenFilter) {
			selected = items[0];
		}

		if(selected == null) {
			//Set in Options and not one of Look's choices: shown as the current
			//item, never rewritten (restore-not-clobber).
			LookCapture capture = hasShader ? LookCapture.Mixed : LookCapture.Captured;
			string label = hasShader ? Path.GetFileNameWithoutExtension(input.ShaderFile) : "";
			LookReason reason = IsNtsc(input.VideoFilter) && input.PackDrawsArt ? LookReason.NtscNotAppliedUnderPack : LookReason.None;
			selected = new(ScreenItemKind.Current, label, input.VideoFilter, input.ShaderFile, true, reason, capture);
			items.Insert(0, selected);
		}
		return (items, selected);
	}

	private static string WithoutScreenFilter(string filter) => LayerOf(filter) == LookFilterLayer.Screen ? NoFilter : filter;

	private static bool IsNtsc(string filter) => filter == NtscBlargg || filter == NtscBisqwit;

	private static LookReason ShaderReason(ShaderAvailability availability)
	{
		return availability == ShaderAvailability.NeedsMetalRenderer ? LookReason.ShadersNeedMetalRenderer : LookReason.ShadersNotInBuild;
	}

	private static bool SamePath(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
