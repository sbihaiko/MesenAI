using System;
using System.Diagnostics;
using System.Text;

namespace Mesen.Logic
{
	//Test-facing (ADR-0125, H6): the two pieces of the Linux file-association
	//writer that can be decided without launching a process, and the two that
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
			return ExecArgument(executablePath) + " " + argument;
		}

		//The Desktop Entry Specification, "The Exec key": quoting is enclosing the
		//argument in double quotes and preceding the double quote, backtick, dollar
		//sign and backslash with an additional backslash - and that escaping
		//backslash is itself subject to the general escape rule for string values,
		//which is applied first. So a literal "$" is written "\\$" and a literal
		//"\" is written as four successive backslashes.
		//
		//The percentage character is a separate rule, not the general escape rule
		//above - that one covers only "\s", "\n", "\t", "\r" and "\\". It is
		//exec-variables.html, the Exec key's field-code paragraph, which says a
		//literal percentage "must be escaped as %%", and that field codes are
		//expanded only after quoting has been undone. "%" is not one of the four
		//characters quoting escapes, so nothing unquotes it and a literal "%" is
		//simply doubled - no backslash is added. Without this,
		//"/home/50%off/Mesen" writes "...50%off...", whose "%o" the loader reads as
		//a field code; a command line with an unlisted field code is invalid and
		//must not be processed, so double-clicking a ROM does nothing (#870).
		public static string ExecArgument(string value)
		{
			//Throwing rather than returning an entry the loader rejects: the
			//caller is what can report the path and skip writing the file (#877).
			if(!CanWriteExecValue(value, out string reason)) {
				throw new ArgumentException(reason, nameof(value));
			}
			StringBuilder sb = new(value.Length + 2);
			sb.Append('"');
			foreach(char c in value) {
				if(c == '\\') {
					sb.Append('\\', 4);
				} else if(c == '"' || c == '`' || c == '$') {
					sb.Append('\\', 2);
					sb.Append(c);
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

		//The Exec key cannot represent every path, and refusing is the only
		//honest answer for the two it cannot (#877). Quoting does not help
		//either of them:
		//
		//  - an "=" in the executable's path. The specification's Exec key
		//    states it outright: "The name or path of the executable program may
		//    not contain the equal sign (=)". No amount of quoting removes it,
		//    so the entry is invalid however it is written.
		//  - a control character, the line breaks above all. A .desktop file is
		//    line-based, so a raw newline inside the quoted argument ends the
		//    Exec= line and corrupts every key after it. The general escape rule
		//    ("\n", "\t", "\r") belongs to string *values* and is not part of
		//    the Exec quoting rules, so it cannot encode one here.
		//
		//A tab and every other character below the printable range are refused
		//with them: the file is text and the value is one line, and a path
		//holding a control character is not one a user wrote on purpose. The
		//caller is what reports the reason and skips writing the desktop entry.
		public static bool CanWriteExecValue(string executablePath, out string reason)
		{
			if(executablePath.Contains('=')) {
				reason = "the executable path contains '=', which the Exec key forbids";
				return false;
			}
			foreach(char c in executablePath) {
				if(c < 0x20 || c == 0x7f) {
					reason = "the executable path contains a control character (U+" + ((int)c).ToString("X4") + "), which a desktop entry cannot carry";
					return false;
				}
			}
			reason = "";
			return true;
		}
	}
}
