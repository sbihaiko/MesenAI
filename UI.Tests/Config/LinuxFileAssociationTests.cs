using System;
using System.Diagnostics;
using Mesen.Logic;
using Xunit;

namespace Mesen.Tests.Config;

//#862: FileAssociationHelper handed a folder to the two update-* helpers as a
//joined command line, and wrote the Exec= key of mesen.desktop unquoted. Both
//break on a path with a space: the child process gets several argv entries and
//none of them is the folder (the failure is swallowed and only logged), and
//"Exec=/home/John Doe/Mesen %f" is an invalid desktop entry, so double-clicking
//a ROM does nothing. Neither failure is visible.
//
//No process is launched and no Linux is needed here: the two decisions are pure
//string work, so they live in UI/Logic/LinuxFileAssociation.cs and are read back
//directly. That placement is the point, not an accident - UI/Logic is what this
//project dual-compiles, so a rule assertion belongs here and not in
//UI.HeadlessTests, whose scope is wiring only (UI.HeadlessTests/AGENTS.md).
//Native-free, so no [Collection] is needed (#432).
public class LinuxFileAssociationTests
{
	//The data dir ($XDG_DATA_HOME, or $HOME/.local/share under a user whose home
	//has a space) and the folder an AppImage is unpacked into can both hold one.
	private const string SpacedFolder = "/home/John Doe/.local/share/mime";
	private const string SpacedExecutable = "/home/John Doe/Applications/Mesen";

	//ProcessStartInfo.ArgumentList is the argv the child is exec'd with: one
	//path, one entry, spaces and all. The joined-string form (ProcessStartInfo.Arguments,
	//or the Process.Start(name, string) overload the bug used) leaves this empty
	//and lets .NET re-split it on the spaces.
	[Fact]
	public void A_folder_with_a_space_reaches_the_update_helper_as_one_argument()
	{
		ProcessStartInfo info = LinuxFileAssociation.DatabaseUpdateStartInfo("update-mime-database", SpacedFolder);

		Assert.Equal("update-mime-database", info.FileName);
		Assert.Equal(new[] { SpacedFolder }, info.ArgumentList);
	}

	//The Desktop Entry Specification's Exec quoting, not the shell's: an argument
	//with a space is enclosed in double quotes. A bare path makes the entry
	//invalid.
	[Fact]
	public void A_path_with_a_space_is_quoted_in_the_exec_value()
	{
		Assert.Equal("\"/home/John Doe/Applications/Mesen\" %f", LinuxFileAssociation.ExecValue(SpacedExecutable, "%f"));
	}

	//The spec's double escaping: the quoting backslash added before ", ` and $
	//is itself subject to the general string escape rule, so a literal "$" is
	//written "\\$".
	[Fact]
	public void The_reserved_characters_are_escaped_per_the_desktop_entry_spec()
	{
		Assert.Equal("\"/tmp/we\\\\\"ird/\\\\`bin/\\\\$x/Mesen\"", LinuxFileAssociation.ExecArgument("/tmp/we\"ird/`bin/$x/Mesen"));
	}

	//The spec: "to unambiguously represent a literal backslash character in a
	//quoted argument in a desktop entry file requires the use of four successive
	//backslash characters".
	[Fact]
	public void A_literal_backslash_is_written_as_four_backslashes()
	{
		Assert.Equal("\"/tmp/a\\\\\\\\b\"", LinuxFileAssociation.ExecArgument("/tmp/a\\b"));
	}

	//A path without a space is still valid when quoted, and the quoting is applied
	//uniformly so there is no branch to get wrong.
	[Fact]
	public void A_path_without_a_space_is_still_quoted()
	{
		Assert.Equal("\"/usr/bin/Mesen\" %f", LinuxFileAssociation.ExecValue("/usr/bin/Mesen", "%f"));
	}

	//#870: the general escape rule for string values also covers the percentage
	//character ("Literal percentage characters must be escaped as %%"), and it is
	//applied to the whole Exec value before the quoting rule. So a literal "%" in
	//the executable path is doubled. Without it, "/home/50%off/Mesen" writes
	//"...50%off...", whose "%o" the loader reads as a field code - and a command
	//line with an unlisted field code is invalid, so double-clicking a ROM does
	//nothing.
	[Fact]
	public void A_path_with_a_literal_percent_is_doubled_in_the_exec_value()
	{
		Assert.Equal("\"/home/50%%off/Mesen\" %f", LinuxFileAssociation.ExecValue("/home/50%off/Mesen", "%f"));
	}

	//The "%" doubling is the general rule, not a replacement for the quoting rule:
	//a reserved character next to a literal "%" still gets its own escape, and the
	//two compose in the same argument.
	[Fact]
	public void A_percent_next_to_other_reserved_characters_keeps_both_escapes()
	{
		Assert.Equal("\"/tmp/100%%/a\\\\\"b/\\\\$c/Mesen\"", LinuxFileAssociation.ExecArgument("/tmp/100%/a\"b/$c/Mesen"));
	}

	//Requirement of #870: the field code argument ("%f") is a field code, not a
	//literal percentage, so it stays unquoted and unescaped - it must still read
	//"%f" in the Exec value.
	[Fact]
	public void The_real_field_code_is_not_escaped()
	{
		Assert.Equal("\"/usr/bin/Mesen\" %f", LinuxFileAssociation.ExecValue("/usr/bin/Mesen", "%f"));
	}

	//A literal "%f" in the path is a literal percentage followed by "f": the
	//general rule writes it "%%f" inside the quotes, while the real field code
	//stays "%f" outside them. Escaping the path is exactly what keeps the two
	//apart - the quoted path can never be mistaken for the field code, and the
	//field code is never doubled into literal text.
	[Fact]
	public void A_literal_percent_f_in_the_path_is_not_the_field_code()
	{
		Assert.Equal("\"/home/%%f/Mesen\" %f", LinuxFileAssociation.ExecValue("/home/%f/Mesen", "%f"));
	}

	//#877: the Exec key cannot represent every path, and the writer used to emit
	//one anyway. An "=" in the executable's path is refused by the specification
	//outright - "The name or path of the executable program may not contain the
	//equal sign (=)" - so no quoting makes the entry valid. Refusing is the fix,
	//and it belongs where the quoting happens: the value throws instead of
	//returning an entry the loader rejects.
	[Theory]
	[InlineData("/home/u/apps/foo=1/Mesen")]
	[InlineData("/home/u/a\nb/Mesen")]
	[InlineData("/home/u/a\rb/Mesen")]
	[InlineData("/home/u/a\tb/Mesen")]
	public void A_path_the_exec_key_cannot_represent_is_refused(string path)
	{
		Assert.Throws<ArgumentException>(() => LinuxFileAssociation.ExecValue(path, "%f"));
		Assert.Throws<ArgumentException>(() => LinuxFileAssociation.ExecArgument(path));
	}

	//The rule is a property of the path alone, so the caller can ask before it
	//starts building the entry. The reason is what reaches the log: the
	//alternative - a desktop entry the environment silently rejects - is
	//exactly the failure #877 is about.
	[Theory]
	[InlineData("/home/u/apps/foo=1/Mesen", "=")]
	[InlineData("/home/u/a\nb/Mesen", "control character")]
	[InlineData("/home/u/a\tb/Mesen", "control character")]
	public void An_unrepresentable_path_reports_why(string path, string expectedInReason)
	{
		Assert.False(LinuxFileAssociation.CanWriteExecValue(path, out string reason));
		Assert.Contains(expectedInReason, reason);
	}

	//The complement, and the one that keeps this fix from being a regression:
	//everything #862 and #870 taught the quoting to handle is still
	//representable. Only what has no encoding at all is refused.
	[Theory]
	[InlineData(SpacedExecutable)]
	[InlineData("/home/50%off/Mesen")]
	[InlineData("/tmp/a\"b/`c/$d\\e/Mesen")]
	[InlineData("/usr/bin/Mesen")]
	public void A_path_the_exec_key_can_represent_is_not_refused(string path)
	{
		Assert.True(LinuxFileAssociation.CanWriteExecValue(path, out string reason));
		Assert.Empty(reason);
	}
}
