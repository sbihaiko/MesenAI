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
		//The general escape rule also covers the percentage character, which
		//exec-variables.html states must be escaped as "%%". Because that rule runs
		//before the quoting rule and "%" is not one of the four characters quoting
		//escapes, a literal "%" is simply doubled - no backslash is added. Without
		//this, "/home/50%off/Mesen" writes "...50%off...", whose "%o" the loader
		//reads as a field code; a command line with an unlisted field code is
		//invalid and must not be processed, so double-clicking a ROM does nothing
		//(#870).
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
	}
}
