using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace Mesen.Logic
{
	//Test-facing (ADR-0125, H6): the pieces of the Linux file-association
	//writer that can be decided without launching a process, and the ones that
	//break when a path contains a space (#862).
	//
	//1. The argv of the two update-* helpers. A path handed to
	//   Process.Start(name, string) as a joined command line is split on its
	//   spaces, so the helper runs with several argv entries and none of them is
	//   the folder - and the failure is swallowed. ProcessStartInfo.ArgumentList
	//   keeps one path as one entry, so the command is built here, never joined.
	//2. The Exec= value of the .desktop file. Its quoting is the Desktop Entry
	//   Specification's, not the shell's: the executable path is enclosed in
	//   double quotes (allowed for any argument, required once it holds a
	//   reserved character) and the four characters the spec escapes inside
	//   them (", `, $, \) are backslash-escaped - with the double escaping the
	//   spec's general string rule implies. A bare path with a space makes the
	//   whole desktop entry invalid, so double-clicking a ROM does nothing. The
	//   same general rule doubles a literal "%" (#870): left single it is read
	//   as a field code and invalidates the entry just as surely.
	//3. The two inputs the Exec key cannot carry at all (#877): an "=" in the
	//   executable's path, which the spec forbids outright, and a control
	//   character with no escape of its own. Both are refused rather than
	//   written into an entry every loader rejects.
	//4. Which keys an update reconciles in an entry that already exists, and
	//   which it leaves alone (#882). CreateLinuxShortcutFile runs only when the
	//   file is absent, so an entry an older build wrote kept its invalid Exec=
	//   forever - the quoting rules above could not reach a user who had already
	//   run Mesen. ReconcileDesktopEntry closes that: Exec= and MimeType= are
	//   reconciled, every other key is carried through.
	public static class LinuxFileAssociation
	{
		public static ProcessStartInfo DatabaseUpdateStartInfo(string command, string folder)
		{
			ProcessStartInfo info = new(command) { UseShellExecute = false };
			info.ArgumentList.Add(folder);
			return info;
		}

		//The argument is the desktop entry's field code ("%f"), not a literal
		//value, so it is concatenated as-is: it stays outside the quotes and is
		//never escaped. A field code is only a field code outside quotes, and the
		//general "%%" escape is for literal percentages only - doubling the
		//argument would turn the real %f into literal text and stop the ROM from
		//being opened (#870).
		public static string ExecValue(string executablePath, string argument)
		{
			//The "=" prohibition is about the executable's path, not about
			//arguments in general - "The name or path of the executable program
			//may not contain the equal sign (=)" - so the check lives here and
			//ExecArgument stays a plain quoter that any argument can use (#877).
			if(!CanWriteExecutablePath(executablePath, out string reason)) {
				throw new ArgumentException(reason, nameof(executablePath));
			}
			return ExecArgument(executablePath) + " " + argument;
		}

		//The Desktop Entry Specification, "The Exec key": quoting is enclosing the
		//argument in double quotes and preceding the double quote, backtick, dollar
		//sign and backslash with an additional backslash - and that escaping
		//backslash is itself subject to the general escape rule for string values,
		//which is applied first. So a literal "$" is written "\\$" and a literal
		//"\" is written as four successive backslashes.
		//
		//The percentage character and the three whitespace controls are a separate
		//rule, not the general escape rule above - that one covers only "\s", "\n",
		//"\t", "\r" and "\\". It is exec-variables.html, the Exec key's field-code
		//paragraph, which says a literal percentage "must be escaped as %%", and
		//that field codes are expanded only after quoting has been undone. "%" is
		//not one of the four characters quoting escapes, so nothing unquotes it and
		//a literal "%" is simply doubled - no backslash is added. Without this,
		//"/home/50%off/Mesen" writes "...50%off...", whose "%o" the loader reads as
		//a field code; a command line with an unlisted field code is invalid and
		//must not be processed, so double-clicking a ROM does nothing (#870).
		//
		//Tab, newline and carriage return are RESERVED characters that the value
		//type nevertheless carries, through its own escapes: a `string` value "may
		//contain all ASCII characters except for control characters", and the five
		//escapes it defines are \s, \n, \t, \r and \\. They cannot be written raw -
		//the file is line-based, and a raw newline would end the Exec= line - so
		//each is emitted as its two-character escape and reaches the argument as
		//itself once the general rule has run (#877).
		public static string ExecArgument(string value)
		{
			StringBuilder sb = new(value.Length + 2);
			sb.Append('"');
			foreach(char c in value) {
				if(c == '\\') {
					sb.Append('\\', 4);
				} else if(c == '"' || c == '`' || c == '$') {
					sb.Append('\\', 2);
					sb.Append(c);
				} else if(c == '\t') {
					sb.Append("\\t");
				} else if(c == '\n') {
					sb.Append("\\n");
				} else if(c == '\r') {
					sb.Append("\\r");
				} else if(c == '%') {
					//General string escape rule, applied before the quoting rule:
					//a literal "%" becomes "%%" (and is unescaped back to "%"
					//before field codes are expanded, so it is never a field code).
					sb.Append("%%");
				} else {
					sb.Append(c);
				}
			}
			sb.Append('"');
			return sb.ToString();
		}

		//Whether the Exec key can carry this executable path at all (#877). Two
		//inputs it cannot:
		//
		//  - an "=". The specification is explicit: "The name or path of the
		//    executable program may not contain the equal sign (=)". No amount of
		//    quoting removes it, so the entry is invalid however it is written.
		//  - a control character with no escape. The value type defines exactly
		//    five escapes (\s, \n, \t, \r, \\), so tab, newline and carriage
		//    return are representable - as the escapes ExecArgument writes - and
		//    everything else below 0x20, DEL included, has no legal form at all.
		//
		//A path holding one of those is not one a user wrote on purpose, so the
		//caller reports the reason and writes no desktop entry, rather than one
		//the loader rejects in silence.
		public static bool CanWriteExecutablePath(string executablePath, out string reason)
		{
			if(executablePath.Contains('=')) {
				reason = "the executable path contains '=', which the Exec key forbids";
				return false;
			}
			foreach(char c in executablePath) {
				bool carriesAnEscape = c == '\t' || c == '\n' || c == '\r';
				if((c < 0x20 && !carriesAnEscape) || c == 0x7f) {
					reason = "the executable path contains a control character (U+" + ((int)c).ToString("X4") + ") that a desktop entry has no escape for";
					return false;
				}
			}
			reason = "";
			return true;
		}

		//Reconcile the keys this writer owns in a desktop entry that already
		//exists. CreateLinuxShortcutFile is only reached when the file is absent,
		//so an entry written by an older build kept whatever Exec= it had (#882) -
		//which is how the quoting fix of #877 could pass a user by entirely: the
		//file was never rewritten.
		//
		//An update reconciles the two keys this writer owns - Exec= and MimeType= -
		//and carries every other key through untouched. Name=, Comment=, Icon= and
		//anything the user added are preserved: the file is ours to maintain, not
		//ours to overwrite. Both keys are replaced where they stand rather than
		//appended, so a second Exec= never appears for a loader to choose between;
		//a key the entry lacks is appended - inside [Desktop Entry], which is the
		//group those keys belong to.
		//
		//The scoping is the correction of a real clobber: a .desktop file may hold
		//more than one group, and a `[Desktop Action ...]` group carries its OWN
		//Exec= for a different command line. Reconciling "every line that starts
		//with Exec=" turns that action into a second copy of the application
		//command and drops its arguments - a key the user added, which this writer
		//therefore does not own.
		//
		//The #877 refusal applies here too: a path the Exec key cannot carry is no
		//reason to replace a working entry with one no loader accepts. The function
		//returns null and the caller leaves the file exactly as it stands. A null
		//`executablePath` is a different case and not a refusal: the caller could
		//not read the running executable, so the Exec key is out of reach while
		//MimeType is still ours to reconcile.
		public static string? ReconcileDesktopEntry(string content, string? executablePath, IReadOnlyList<string>? mimeTypes, out string reason)
		{
			reason = "";
			bool reconcileExec = executablePath != null;
			if(reconcileExec && !CanWriteExecutablePath(executablePath!, out reason)) {
				return null;
			}

			string execValue = reconcileExec ? ExecValue(executablePath!, "%f") : "";
			string mimeTypeValue = MimeTypeValue(mimeTypes);

			List<string> lines = new(content.Split(Environment.NewLine));
			int groupStart = FindDesktopEntryGroup(lines);
			if(groupStart < 0) {
				//A file with no [Desktop Entry] group is not loadable as it stands,
				//and appending the keys at the end would file them under whichever
				//group is last. Give them the group they belong to.
				if(lines.Count != 1 || lines[0].Length != 0) {
					lines.Add("");
				}
				lines.Add(DesktopEntryGroup);
				groupStart = lines.Count - 1;
			}
			int groupEnd = FindGroupEnd(lines, groupStart);

			bool execReplaced = false;
			bool mimeTypeReplaced = false;
			for(int i = groupStart + 1; i < groupEnd; i++) {
				if(reconcileExec && IsKey(lines[i], "Exec")) {
					lines[i] = "Exec=" + execValue;
					execReplaced = true;
				} else if(IsKey(lines[i], "MimeType")) {
					lines[i] = "MimeType=" + mimeTypeValue;
					mimeTypeReplaced = true;
				}
			}

			int insertAt = groupEnd;
			if(reconcileExec && !execReplaced) {
				lines.Insert(insertAt, "Exec=" + execValue);
				insertAt++;
			}
			if(!mimeTypeReplaced) {
				lines.Insert(insertAt, "MimeType=" + mimeTypeValue);
			}

			return string.Join(Environment.NewLine, lines);
		}

		private const string DesktopEntryGroup = "[Desktop Entry]";

		//The group the application's own keys live in. Matched exactly: group
		//names are case-sensitive, so a `[desktop entry]` is a different (and
		//invalid) group rather than this one.
		private static int FindDesktopEntryGroup(List<string> lines)
		{
			for(int i = 0; i < lines.Count; i++) {
				if(lines[i].Trim() == DesktopEntryGroup) {
					return i;
				}
			}
			return -1;
		}

		//The line the group ends at: the next header, or the end of the file.
		private static int FindGroupEnd(List<string> lines, int groupStart)
		{
			for(int i = groupStart + 1; i < lines.Count; i++) {
				if(lines[i].TrimStart().StartsWith("[", StringComparison.Ordinal)) {
					return i;
				}
			}
			return lines.Count;
		}

		//`key=`, tolerating whitespace before the `=`. That whitespace is not in
		//the desktop entry grammar, but a line carrying it is a line whose key the
		//loader will not read - appending a second Exec= beside it would leave two
		//for the loader to choose between, so it is recognised and rewritten.
		private static bool IsKey(string line, string key)
		{
			string trimmed = line.Trim();
			if(!trimmed.StartsWith(key, StringComparison.Ordinal)) {
				return false;
			}
			return trimmed.Substring(key.Length).TrimStart().StartsWith("=", StringComparison.Ordinal);
		}

		private static string MimeTypeValue(IReadOnlyList<string>? mimeTypes)
		{
			if(mimeTypes == null || mimeTypes.Count == 0) {
				return "";
			}
			StringBuilder sb = new();
			for(int i = 0; i < mimeTypes.Count; i++) {
				if(i > 0) {
					sb.Append(';');
				}
				sb.Append("application/").Append(mimeTypes[i]);
			}
			return sb.ToString();
		}

		//The mesen.desktop text, or null when the Exec key cannot carry the path -
		//with the reason for the caller to report. Keeping the decision here is
		//what makes it testable: the writer's choice between "write a broken entry"
		//and "write nothing" is then a return value, not a branch behind a
		//Process.GetCurrentProcess() call only a real Linux run can reach (#877).
		public static string? BuildDesktopEntry(string executablePath, IReadOnlyList<string>? mimeTypes, out string reason)
		{
			if(!CanWriteExecutablePath(executablePath, out reason)) {
				return null;
			}

			StringBuilder sb = new();
			sb.Append("[Desktop Entry]").Append(Environment.NewLine);
			sb.Append("Type=Application").Append(Environment.NewLine);
			sb.Append("Name=Mesen").Append(Environment.NewLine);
			sb.Append("Comment=Emulator").Append(Environment.NewLine);
			sb.Append("Keywords=game;emulator;emu").Append(Environment.NewLine);
			sb.Append("Categories=GNOME;GTK;Game;Emulator;").Append(Environment.NewLine);
			sb.Append("Exec=").Append(ExecValue(executablePath, "%f")).Append(Environment.NewLine);
			sb.Append("NoDisplay=false").Append(Environment.NewLine);
			sb.Append("StartupNotify=true").Append(Environment.NewLine);
			sb.Append("Icon=MesenIcon").Append(Environment.NewLine);

			if(mimeTypes != null) {
				sb.Append("MimeType=");
				for(int i = 0; i < mimeTypes.Count; i++) {
					if(i > 0) {
						sb.Append(';');
					}
					sb.Append("application/").Append(mimeTypes[i]);
				}
				sb.Append(Environment.NewLine);
			}

			reason = "";
			return sb.ToString();
		}
	}
}
