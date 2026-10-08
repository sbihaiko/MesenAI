namespace Mesen.Logic
{
	//#1067: whether the ROM picker sheet walks its grid to ask about visible tiles.
	//Only an open sheet that is showing the library has tiles on screen; a closed
	//sheet, or one showing the folder browser, must not pay the per-tile walk.
	//Host-free (UI/Logic firewall, ADR-0123).
	public static class RomPickerAskRule
	{
		public static bool ShouldWalkGrid(bool sheetOpen, bool showingLibrary) => sheetOpen && showingLibrary;
	}
}
