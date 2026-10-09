namespace Mesen.Logic;

//#939 (PRD Part B §13.5.2 W-P16): W-P6 shows a pack's pending file as its
//orange line with Add the File…, which opens W-P16's add flow. The line
//follows the W-P16 notice: it outlives Play Without It (the pack stays
//partial, and this is where the file can still be added) and goes once the
//file is in place or the game is gone (PackDepNoticeState.Clear).
public static class PackDetailPendingFile
{
	public static bool Shows(PackDepNoticeState notice) => notice.HasPending;

	//Rule 9: the sheet opens on the control that can act.
	public static string FirstControl(bool hasPendingFile, bool canChange)
	{
		if(hasPendingFile) {
			return "PackDetailAddFileButton";
		}
		return canChange ? "PackDetailChangeButton" : "PackDetailDoneButton";
	}
}
