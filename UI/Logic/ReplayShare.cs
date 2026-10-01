using System;
using System.IO;
using System.Text;

namespace Mesen.Logic;

//ADR-0205 sections 2 and 6 (slice R.1): the host-free half of the single
//Record-and-share action. The recording itself is the Core's
//(MovieRecordAndShare); what lives here is what the UI decides around it - the
//file it writes and the pre-filled issue URL it opens in the author's own
//browser. There is no upload and no credential: GitHub exposes no public API
//for attaching a non-image file, so the author drags the file into the form.
public static class ReplayShare
{
	public const string Repository = "sbihaiko/MesenAI";
	public const string FormTemplate = "replay.yml";
	public const string FormLabel = "replay";
	public const string TitlePrefix = "[Replay] ";

	//Browsers and GitHub accept long query strings but not unbounded ones; the
	//notes are the only variable part and the untruncated Description is
	//already in the .mmo's MovieInfo.txt.
	public const int MaxUrlLength = 4000;

	//Pre-filled issue URL. The title carries only the prefix: the workflow
	//rewrites the whole title from the file (section 5), so a typed title is
	//not evidence and pre-filling one would only invite editing it.
	public static string BuildIssueUrl(string notes)
	{
		string head = "https://github.com/" + Repository + "/issues/new"
			+ "?template=" + Uri.EscapeDataString(FormTemplate)
			+ "&title=" + Uri.EscapeDataString(TitlePrefix)
			+ "&labels=" + Uri.EscapeDataString(FormLabel);

		StringBuilder escaped = new StringBuilder();
		//By rune: escaping a surrogate half alone would percent-encode U+FFFD.
		foreach(Rune c in (notes ?? "").EnumerateRunes()) {
			string piece = Uri.EscapeDataString(c.ToString());
			if(head.Length + "&notes=".Length + escaped.Length + piece.Length > MaxUrlLength) {
				break;
			}
			escaped.Append(piece);
		}
		return escaped.Length == 0 ? head : head + "&notes=" + escaped;
	}

	//The .mmo the action writes. The extension is cosmetic to the Core (it
	//detects the format by content), and a rejected .mmo may be re-uploaded as
	//.zip without recompression (section 1).
	public static string FileName(string romName, DateTime now)
	{
		StringBuilder name = new StringBuilder();
		foreach(char c in romName ?? "") {
			name.Append(Array.IndexOf(Path.GetInvalidFileNameChars(), c) >= 0 || c == ':' || c == '?' || c == '\\' ? '_' : c);
		}
		string baseName = name.ToString().Trim();
		if(baseName.Length == 0) {
			baseName = "replay";
		}
		return baseName + " " + now.ToString("yyyy-MM-dd HH.mm.ss") + ".mmo";
	}
}
