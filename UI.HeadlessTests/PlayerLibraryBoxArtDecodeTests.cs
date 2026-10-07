using System;
using System.IO;
using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Mesen.Logic;
using Mesen.ViewModels;
using Xunit;

namespace Mesen.HeadlessTests;

//#1039 review findings 1 and 2 on PR #1057.
public class PlayerLibraryBoxArtDecodeTests : IDisposable
{
	private readonly string _folder = Path.Combine(Path.GetTempPath(), "mesen-1039-decode-" + Guid.NewGuid().ToString("N"));

	public PlayerLibraryBoxArtDecodeTests()
	{
		Directory.CreateDirectory(_folder);
	}

	public void Dispose()
	{
		try {
			Directory.Delete(_folder, true);
		} catch {
		}
	}

	//Finding 2: a cover is up to 4 MiB and often ~1000x1400, drawn at 104x139. What
	//the tile keeps is the small decode, not the source's resolution.
	[AvaloniaFact]
	public void A_large_cover_is_decoded_down_to_about_twice_the_tile_width()
	{
		string path = Path.Combine(_folder, "big.png");
		using(WriteableBitmap source = new(new PixelSize(1000, 1400), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul)) {
			source.Save(path);
		}

		using Bitmap? decoded = BoxArtBitmap.Decode(path);

		Assert.NotNull(decoded);
		Assert.InRange(decoded!.PixelSize.Width, 1, 2 * 104);
	}

	[AvaloniaFact]
	public void A_file_the_decoder_refuses_decodes_to_nothing()
	{
		string path = Path.Combine(_folder, "bad.png");
		File.WriteAllText(path, "not an image");

		Assert.Null(BoxArtBitmap.Decode(path));
	}

	//Finding 1: the picker's canonical-titles pass and the box-art chain hash
	//through ONE RomHashCache, so a dump is read once however many surfaces ask.
	[AvaloniaFact]
	public void The_picker_and_the_box_art_session_share_one_rom_hash_cache()
	{
		MainWindowViewModel model = new();

		Assert.Same(MainWindowViewModel.BoxArtHashes, model.RomPicker.RomHashCache);
	}
}
