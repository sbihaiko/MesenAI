using System;
using System.Collections.Generic;
using System.Globalization;

namespace Mesen.Logic;

//G.5 (PRD Part B §8, ADR-0241, §13.5.2 W-P13): a game needs a BIOS file. The
//sheet replaces the FirmwareNotFound message box and its file-dialog loop; the
//Core's MissingFirmware notification and the copy into the Firmware folder are
//unchanged. MesenAI never points to a BIOS source.
public enum BiosKind
{
	Fds,
	GbaBios,
	SmsBootRom,
	GgBootRom,
	GameBoyBootRom,
	Other
}

public sealed record BiosSizeCheck(bool Accepted, long ActualSize, long ExpectedSize);

public static class PlayBiosPrompt
{
	//Rule 2: drop zone, Cancel, Choose File… = 3.
	public const int ControlCount = 3;

	//firmwareType: the FirmwareType enum name the Core sends (the enum itself
	//lives in the Avalonia-side interop file). One sentence per kind.
	public static BiosKind Classify(string firmwareType)
	{
		return firmwareType switch {
			"FDS" or "StudyBox" => BiosKind.Fds,
			"GameboyAdvance" => BiosKind.GbaBios,
			"SmsBootRom" => BiosKind.SmsBootRom,
			"GgBootRom" => BiosKind.GgBootRom,
			"Gameboy" or "GameboyColor" => BiosKind.GameBoyBootRom,
			_ => BiosKind.Other
		};
	}

	//"8 KB", "16 KB", "256 bytes", "2 MB": the sheet's grey hint and the
	//wrong-size line (W-X2: plain words, never a byte count in hex).
	public static string SizeText(long bytes)
	{
		if(bytes < 1024) {
			return bytes.ToString(CultureInfo.InvariantCulture) + " bytes";
		}
		if(bytes < 1024 * 1024) {
			return Trim(bytes / 1024.0) + " KB";
		}
		return Trim(bytes / (1024.0 * 1024.0)) + " MB";
	}

	private static string Trim(double value)
	{
		return Math.Round(value, 1).ToString("0.#", CultureInfo.InvariantCulture);
	}

	//knownSizes: every size the firmware type accepts (FirmwareFiles). A wrong
	//size is an inline line under the drop zone, not a new dialog; the first
	//known size is the one the line names.
	public static BiosSizeCheck CheckSize(long actualSize, IReadOnlyList<long> knownSizes)
	{
		if(knownSizes.Count == 0) {
			return new BiosSizeCheck(true, actualSize, actualSize);
		}
		foreach(long size in knownSizes) {
			if(size == actualSize) {
				return new BiosSizeCheck(true, actualSize, size);
			}
		}
		return new BiosSizeCheck(false, actualSize, knownSizes[0]);
	}
}

//W-S1's status line carries one extra clause from an edge flow: "Zelda needs
//the FDS BIOS" after a cancelled BIOS sheet (no game), "· waiting for one
//file" after a pack's missing dep (game running).
public static class PlayStatusNotice
{
	public const string Separator = " · ";

	public static string Compose(string sentence, string notice, bool gameLoaded)
	{
		if(string.IsNullOrEmpty(notice)) {
			return sentence;
		}
		return gameLoaded ? sentence + Separator + notice : notice;
	}
}
