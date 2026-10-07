using System;
using Avalonia.Headless.XUnit;
using Mesen.Logic;
using Mesen.ViewModels;
using Xunit;

namespace Mesen.HeadlessTests;

//#1032 (ADR-0264 Decision 11): the folder browser is the sheet's OTHER surface
//when the Play home opens it - *Browse a file…* inside the library - but it is
//the sheet's ONLY surface for the Remaster workspace's *right game* chooser,
//whose picker says so through OpenMode = BrowseFile
//(RemasterWorkspaceViewModel.Recording.cs, NewRightGamePicker). B on the
//browser's roots has to answer for the surface that IS the sheet: inside the
//library it walks back to the grid, and as a picker of its own it dismisses -
//otherwise B turns the Remaster sheet (RemasterWorkspaceView.axaml) into the
//Play library grid, a surface the artist never asked for and cannot leave.
//
//View-model state only: the picker is built directly, both of its sources are
//stubbed and nothing reads a disk, so the case runs with or without the native
//core.
[NativeCoreFree("PlayerRomPickerViewModel's constructor only builds its AddKnownGameFolder delegate, and both sources are stubbed here")]
public class PlayerRomPickerModeTests
{
	private static PlayerRomPickerViewModel Picker(RomPickerMode mode)
	{
		return new PlayerRomPickerViewModel {
			OpenMode = mode,
			//No root, no hit and no scan may come from the machine this suite
			//happens to run on.
			VolumeSource = () => Array.Empty<string>(),
			WholeComputerFolder = null,
			SuggestionSource = _ => Array.Empty<RomPickerHit>(),
			LibraryFolderSource = () => Array.Empty<string>(),
			RunScanInline = true,
			RunLibraryScanInline = true
		};
	}

	//The right game chooser: Back on its roots is the dismiss, the way it was
	//before the Play sheet's library landed (ADR-0256 Decision 9).
	[AvaloniaFact]
	public void Back_on_the_right_game_pickers_roots_dismisses_it()
	{
		PlayerRomPickerViewModel picker = Picker(RomPickerMode.BrowseFile);
		picker.Open();
		Assert.True(picker.IsVisible, "the right game picker did not open");
		Assert.Equal(RomPickerMode.BrowseFile, picker.Mode);

		picker.Back();

		Assert.False(picker.IsVisible, "Back on the right game picker's roots left the sheet up");
		Assert.True(picker.Mode != RomPickerMode.Library,
			"Back on the right game picker's roots turned it into the Play library grid");
	}

	//...and the library's own browser keeps Decision 11's rule: the escape
	//hatch leads back to the grid, not away from the sheet.
	[AvaloniaFact]
	public void Back_on_the_librarys_browser_roots_returns_to_the_library()
	{
		PlayerRomPickerViewModel picker = Picker(RomPickerMode.Library);
		picker.Open();
		picker.BrowseFile();
		Assert.Equal(RomPickerMode.BrowseFile, picker.Mode);

		picker.Back();

		Assert.True(picker.IsVisible, "Back out of the library's own browser closed the sheet");
		Assert.Equal(RomPickerMode.Library, picker.Mode);
	}
}
