namespace Mesen.Logic
{
	//Which collection a cover came from. The cover priority is a decision
	//(downloaded box art above a downloaded title screen, above the player's own
	//screenshot from the Recent list), so the caller has to be able to tell the
	//two downloads apart rather than guess from the file.
	public enum BoxArtCoverKind
	{
		Boxart = 0,
		Title
	}

	//A cover that is on the player's disk right now. There is exactly one way to
	//get one - BoxArtCache.GetCover - and it never hands back a path it could not
	//write, so a caller that receives this can hand the file straight to the
	//image loader.
	public sealed record BoxArtCover(string FilePath, BoxArtCoverKind Kind);
}
