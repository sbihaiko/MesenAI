using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Mesen.Config;
using Mesen.Interop;
using Xunit;

namespace Mesen.HeadlessTests;

//Issue #954, ADR-0253 W.7 on a real GBA cartridge. W.7 shipped with its
//on-screen result "not evaluated" because no GBA ROM was at hand; this class
//loads SyntheticGbaRom into the real core with the Widescreen switch on and
//reads the frame back through FrameCaptureApi - the capture Play's pause
//surfaces use, after the video filter, so it holds the extended frame exactly
//as the screen gets it.
//
//What it pins, per ADR-0249, is structure and color class, never exact pixel
//values: the frame's size, and whether a column reads as the map's hidden
//columns (green), the shown ones (red), black or white. The color math itself
//is covered host-free by the TestGbaReveal* cases in core_unit_tests.
//
//The frame is 284x160: 22 columns each side of the 240-px picture. With a
//256-wide map and scroll 0, ColumnHasContent gives each side 16 columns of the
//map's hidden columns 30-31 and 6 columns that would wrap back onto what the
//window already shows (left: frame x 0-5, right: frame x 278-283), which fall
//back to black.
//
//Skips when the native core is not built (ADR-0150 §3), which is every CI run.
[Collection(NativeCoreCollection.Name)]
public class GbaWidescreenRevealTests : IDisposable
{
	private const int Extra = 22;
	private const int Standard = 240;
	private const int Extended = Standard + 2 * Extra;
	private const int Height = 160;
	//The columns of each side that fall on the map's own wrap.
	private const int Wrapped = 6;

	private static readonly FieldInfo HomeFolderField = typeof(ConfigManager).GetField("_homeFolder", BindingFlags.NonPublic | BindingFlags.Static)!;

	private readonly string _folder = Path.Combine(Path.GetTempPath(), "mesen-954-" + Guid.NewGuid().ToString("N"));
	private readonly object? _previousHome;
	private VideoAspectRatio _previousAspect;
	private VideoFilterType _previousFilter;
	private bool _previousSkipBoot;
	private bool _configTouched;

	private enum Tone { Hidden, Shown, Black, White, Other }

	public GbaWidescreenRevealTests()
	{
		Directory.CreateDirectory(Path.Combine(_folder, "home"));
		//Keep the config, the (absent) BIOS lookup and anything the core writes
		//out of the user's real home.
		_previousHome = HomeFolderField.GetValue(null);
		HomeFolderField.SetValue(null, Path.Combine(_folder, "home"));
	}

	public void Dispose()
	{
		if(NativeCore.IsAvailable) {
			EmuApi.Stop();
			if(_configTouched) {
				ConfigManager.Config.Video.AspectRatio = _previousAspect;
				ConfigManager.Config.Video.VideoFilter = _previousFilter;
				ConfigManager.Config.Gba.SkipBootScreen = _previousSkipBoot;
				ConfigManager.Config.Video.ApplyConfig();
				ConfigManager.Config.Gba.ApplyConfig();
			}
		}
		HomeFolderField.SetValue(null, _previousHome);
		try {
			Directory.Delete(_folder, true);
		} catch(IOException) {
		} catch(UnauthorizedAccessException) {
		}
	}

	[AvaloniaFact]
	public void A_text_bg_reveals_the_map_columns_the_window_does_not_show()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		Tone[,] frame = RunScene(GbaScene.TextBg, widescreen: true, out int width, out int height);

		Assert.Equal(Extended, width);
		Assert.Equal(Height, height);
		foreach(int y in new[] { 0, Height / 2, Height - 1 }) {
			AssertRun(frame, y, 0, Wrapped, Tone.Black, "left side, map wrap");
			AssertRun(frame, y, Wrapped, Extra, Tone.Hidden, "left side, map columns 30-31");
			AssertRun(frame, y, Extra, Extra + Standard, Tone.Shown, "the 240-px picture");
			AssertRun(frame, y, Extra + Standard, Extended - Wrapped, Tone.Hidden, "right side, map columns 30-31");
			AssertRun(frame, y, Extended - Wrapped, Extended, Tone.Black, "right side, map wrap");
		}
	}

	[AvaloniaFact]
	public void An_affine_bg_on_the_row_turns_both_sides_black()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		Tone[,] frame = RunScene(GbaScene.AffineBg, widescreen: true, out int width, out _);

		Assert.Equal(Extended, width);
		AssertSidesAre(frame, Tone.Black);
	}

	[AvaloniaFact]
	public void A_bitmap_mode_turns_both_sides_black()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		Tone[,] frame = RunScene(GbaScene.Bitmap, widescreen: true, out int width, out _);

		Assert.Equal(Extended, width);
		AssertSidesAre(frame, Tone.Black);
	}

	[AvaloniaFact]
	public void A_forced_blank_row_stays_white_beside_the_picture()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		Tone[,] frame = RunScene(GbaScene.ForcedBlank, widescreen: true, out int width, out _);

		Assert.Equal(Extended, width);
		foreach(int y in new[] { 0, Height / 2, Height - 1 }) {
			AssertRun(frame, y, 0, Extended, Tone.White, "forced-blank row");
		}
	}

	[AvaloniaFact]
	public void Turning_the_switch_off_returns_the_standard_width()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		RunScene(GbaScene.TextBg, widescreen: true, out int wide, out _);
		Assert.Equal(Extended, wide);

		Tone[,] frame = Recapture(widescreen: false, out int width, out int height);

		Assert.Equal(Standard, width);
		Assert.Equal(Height, height);
		AssertRun(frame, Height / 2, 0, Standard, Tone.Shown, "the 240-px picture");
	}

	//Issue #963: Directory.Delete throws UnauthorizedAccessException, not
	//IOException, when a folder it must empty refuses writes (as when the core
	//still holds it), and teardown must not turn a passing case red. Host-only:
	//it never starts the core, so it runs with or without the native library.
	[AvaloniaFact]
	public void Teardown_tolerates_a_folder_it_is_not_allowed_to_delete()
	{
		Assert.SkipWhen(OperatingSystem.IsWindows(), "Unix file modes lock the folder");
		GbaWidescreenRevealTests inner = new();
		string locked = Path.Combine(inner._folder, "locked");
		Directory.CreateDirectory(locked);
		File.WriteAllBytes(Path.Combine(locked, "held.gba"), new byte[] { 0 });
		File.SetUnixFileMode(locked, UnixFileMode.UserRead | UnixFileMode.UserExecute);
		try {
			Exception? thrown = Record.Exception(inner.Dispose);
			Assert.Null(thrown);
		} finally {
			File.SetUnixFileMode(locked, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
			Directory.Delete(inner._folder, true);
		}
	}

	private Tone[,] RunScene(GbaScene scene, bool widescreen, out int width, out int height)
	{
		string rom = Path.Combine(_folder, "synthetic-" + scene + ".gba");
		File.WriteAllBytes(rom, SyntheticGbaRom.Build(scene));

		EmuApi.InitDll();
		EmuApi.InitializeEmu(ConfigManager.HomeFolder, IntPtr.Zero, IntPtr.Zero, true, true, true, true);

		_previousAspect = ConfigManager.Config.Video.AspectRatio;
		_previousFilter = ConfigManager.Config.Video.VideoFilter;
		_previousSkipBoot = ConfigManager.Config.Gba.SkipBootScreen;
		_configTouched = true;
		//No BIOS is ever committed: the cartridge starts straight at 0x08000000.
		ConfigManager.Config.Gba.SkipBootScreen = true;
		ConfigManager.Config.Gba.ApplyConfig();
		//The NTSC filters refuse an extended frame (W.6); the default one keeps it.
		ConfigManager.Config.Video.VideoFilter = VideoFilterType.None;

		Assert.True(EmuApi.LoadRom(rom, string.Empty), $"the core refused to load {rom}");
		Assert.True(WaitFor(() => EmuApi.IsRunning()), "the game never started");
		Assert.Equal(ConsoleType.Gba, EmuApi.GetRomInfo().ConsoleType);

		return Recapture(widescreen, out width, out height);
	}

	//Applies the switch, lets the console run a handful of frames under it (the
	//PPU latches it once per frame), parks it and reads the shown frame.
	private static Tone[,] Recapture(bool widescreen, out int width, out int height)
	{
		ConfigManager.Config.Video.AspectRatio = widescreen ? VideoAspectRatio.Widescreen : VideoAspectRatio.Auto;
		ConfigManager.Config.Video.ApplyConfig();

		EmuApi.Resume();
		uint start = FrameCaptureApi.HeadlessGetFrameCount();
		Assert.True(WaitFor(() => FrameCaptureApi.HeadlessGetFrameCount() >= start + 10), "the console stopped producing frames");
		EmuApi.Pause();
		Assert.True(WaitFor(() => EmuApi.IsPaused()), "the console never paused");
		//The cartridge draws the same picture every frame, so whichever of the
		//last few frames the capture reads, its structure is the same.

		using WriteableBitmap? bitmap = FrameCaptureApi.CaptureFrame();
		Assert.NotNull(bitmap);
		width = bitmap.PixelSize.Width;
		height = bitmap.PixelSize.Height;
		return Classify(bitmap);
	}

	private static Tone[,] Classify(WriteableBitmap bitmap)
	{
		int width = bitmap.PixelSize.Width;
		int height = bitmap.PixelSize.Height;
		Tone[,] tones = new Tone[height, width];
		using ILockedFramebuffer source = bitmap.Lock();
		byte[] row = new byte[width * 4];
		for(int y = 0; y < height; y++) {
			Marshal.Copy(source.Address + y * source.RowBytes, row, 0, row.Length);
			for(int x = 0; x < width; x++) {
				tones[y, x] = ToneOf(row[x * 4 + 2], row[x * 4 + 1], row[x * 4]);
			}
		}
		return tones;
	}

	//Bands wide enough for any color correction the default GBA filter applies:
	//the class, not the value, is what the cartridge decides.
	private static Tone ToneOf(byte r, byte g, byte b)
	{
		if(r < 48 && g < 48 && b < 48) {
			return Tone.Black;
		}
		if(r > 200 && g > 200 && b > 200) {
			return Tone.White;
		}
		if(g > 96 && g > 2 * r && g > 2 * b) {
			return Tone.Hidden;
		}
		if(r > 96 && r > 2 * g && r > 2 * b) {
			return Tone.Shown;
		}
		return Tone.Other;
	}

	private static void AssertSidesAre(Tone[,] frame, Tone expected)
	{
		foreach(int y in new[] { 0, Height / 2, Height - 1 }) {
			AssertRun(frame, y, 0, Extra, expected, "left side");
			AssertRun(frame, y, Extra + Standard, Extended, expected, "right side");
		}
	}

	private static void AssertRun(Tone[,] frame, int y, int from, int to, Tone expected, string what)
	{
		for(int x = from; x < to; x++) {
			Assert.True(frame[y, x] == expected, $"{what}: pixel ({x}, {y}) is {frame[y, x]}, expected {expected}");
		}
	}

	private static bool WaitFor(Func<bool> condition)
	{
		Stopwatch clock = Stopwatch.StartNew();
		while(clock.ElapsedMilliseconds < 5000) {
			if(condition()) {
				return true;
			}
			Thread.Sleep(20);
		}
		return condition();
	}
}
