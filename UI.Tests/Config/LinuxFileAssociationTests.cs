using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text.RegularExpressions;
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

	//#877: the Exec key cannot carry every path, and the writer used to emit one
	//anyway. An "=" is refused by the specification outright - "The name or path
	//of the executable program may not contain the equal sign (=)" - and no
	//quoting removes it. The rule is a property of the PATH, not of arguments in
	//general, so ExecValue is what refuses.
	[Theory]
	[InlineData("/home/u/apps/foo=1/Mesen")]
	[InlineData("/home/u/a\u0001b/Mesen")]
	[InlineData("/home/u/a\u007fb/Mesen")]
	public void A_path_the_exec_key_cannot_carry_is_refused(string path)
	{
		Assert.Throws<ArgumentException>(() => LinuxFileAssociation.ExecValue(path, "%f"));
	}

	//Tab, newline and carriage return are RESERVED characters the value type
	//carries through its own escapes ("a string value may contain all ASCII
	//characters except for control characters" and the five escapes are \s, \n,
	//\t, \r, \\), so such a path IS writable: raw it would end the line-based
	//Exec= line, but the escape reaches the argument as the character itself once
	//the general rule - applied before the quoting rule - has run. Refusing these
	//would be over-rejection (#877).
	[Theory]
	[InlineData("/home/u/a\tb/Mesen", "\"/home/u/a\\tb/Mesen\"")]
	[InlineData("/home/u/a\nb/Mesen", "\"/home/u/a\\nb/Mesen\"")]
	[InlineData("/home/u/a\rb/Mesen", "\"/home/u/a\\rb/Mesen\"")]
	public void A_whitespace_control_in_the_path_is_escaped_not_refused(string path, string expected)
	{
		Assert.True(LinuxFileAssociation.CanWriteExecutablePath(path, out string reason));
		Assert.Empty(reason);
		Assert.Equal(expected, LinuxFileAssociation.ExecArgument(path));
	}

	//The escape above must not be confused with a literal backslash followed by
	//the same letter: a backslash is written as four, so "\t" in the path arrives
	//as a backslash and a "t", never as a tab.
	[Fact]
	public void A_literal_backslash_before_a_t_is_not_a_tab()
	{
		Assert.Equal("\"/home/u/a\\\\\\\\tb/Mesen\"", LinuxFileAssociation.ExecArgument("/home/u/a\\tb/Mesen"));
	}

	//The rule is a property of the path alone, so the caller can ask before it
	//starts building the entry. The reason is what reaches the log: the
	//alternative - a desktop entry the environment silently rejects - is exactly
	//the failure #877 is about.
	[Theory]
	[InlineData("/home/u/apps/foo=1/Mesen", "=")]
	[InlineData("/home/u/a\u0001b/Mesen", "control character")]
	public void An_unrepresentable_path_reports_why(string path, string expectedInReason)
	{
		Assert.False(LinuxFileAssociation.CanWriteExecutablePath(path, out string reason));
		Assert.Contains(expectedInReason, reason);
	}

	//The complement, and the one that keeps this fix from being a regression:
	//everything #862 and #870 taught the quoting to handle is still writable.
	//Only what has no encoding at all is refused.
	[Theory]
	[InlineData(SpacedExecutable)]
	[InlineData("/home/50%off/Mesen")]
	[InlineData("/tmp/a\"b/`c/$d\\e/Mesen")]
	[InlineData("/usr/bin/Mesen")]
	public void A_path_the_exec_key_can_carry_is_not_refused(string path)
	{
		Assert.True(LinuxFileAssociation.CanWriteExecutablePath(path, out string reason));
		Assert.Empty(reason);
	}

	//The "=" prohibition is about the executable's path; an ordinary argument may
	//hold one, and the quoter must not refuse it.
	[Fact]
	public void An_argument_may_contain_an_equal_sign()
	{
		Assert.Equal("\"x=y\"", LinuxFileAssociation.ExecArgument("x=y"));
	}

	//#877, the half the writer owns: BuildDesktopEntry turns "write a broken
	//entry or write nothing" into a return value, so it is assertable without a
	//Linux run - the branch used to sit behind Process.GetCurrentProcess()
	//.MainModule, which no test can reach.
	[Fact]
	public void An_unrepresentable_path_yields_no_desktop_entry()
	{
		Assert.Null(LinuxFileAssociation.BuildDesktopEntry("/home/u/apps/foo=1/Mesen", null, out string reason));
		Assert.Contains("=", reason);
	}

	[Fact]
	public void A_representable_path_yields_a_full_desktop_entry()
	{
		string? content = LinuxFileAssociation.BuildDesktopEntry(SpacedExecutable, new List<string> { "x-mesen-nes" }, out string reason);

		Assert.Empty(reason);
		Assert.NotNull(content);
		Assert.Contains("[Desktop Entry]", content);
		Assert.Contains("Exec=\"/home/John Doe/Applications/Mesen\" %f", content);
		Assert.Contains("MimeType=application/x-mesen-nes", content);
	}

	//#882: CreateLinuxShortcutFile is only reached when mesen.desktop is absent,
	//and the update path rewrote MimeType= and nothing else. So an entry written
	//before #877 kept its invalid Exec= for good: the quoting fix could not reach
	//a user who had ever run Mesen, and the ROM double-click stayed dead. The same
	//stale Exec= survives the executable moving - the AppImage case, where the
	//unpacked folder differs between runs - and then the shortcut launches nothing.
	//
	//The rule the tests below pin: an update reconciles the keys this writer owns,
	//Exec= and MimeType=, and carries every other key through untouched. The file
	//is ours to maintain, not ours to overwrite.
	private static string Entry(params string[] lines)
	{
		return string.Join(Environment.NewLine, lines);
	}

	[Fact]
	public void An_update_repairs_an_exec_key_an_older_build_wrote()
	{
		string stale = Entry("[Desktop Entry]", "Type=Application", "Name=Mesen",
			"Exec=/home/John Doe/Mesen %f", "MimeType=application/x-mesen-nes;");

		string? fixedUp = LinuxFileAssociation.ReconcileDesktopEntry(stale, SpacedExecutable, new List<string> { "x-mesen-nes" }, out string reason);

		Assert.Empty(reason);
		Assert.NotNull(fixedUp);
		Assert.Contains("Exec=\"/home/John Doe/Applications/Mesen\" %f", fixedUp);
		Assert.DoesNotContain("Exec=/home/John Doe/Mesen %f", fixedUp);
	}

	[Fact]
	public void An_update_repoints_an_exec_key_at_the_executable_that_is_running()
	{
		string stale = Entry("[Desktop Entry]", "Exec=\"/opt/old/Mesen\" %f");

		string? fixedUp = LinuxFileAssociation.ReconcileDesktopEntry(stale, "/opt/new/Mesen", null, out string reason);

		Assert.Empty(reason);
		Assert.NotNull(fixedUp);
		Assert.Contains("Exec=\"/opt/new/Mesen\" %f", fixedUp);
		Assert.DoesNotContain("/opt/old/Mesen", fixedUp);
	}

	//The complement: a user who renamed the entry, or added a key of their own,
	//must not have that edit thrown away by an update that only had Exec= and
	//MimeType= to reconcile.
	[Fact]
	public void An_update_keeps_the_keys_the_writer_does_not_own()
	{
		string entry = Entry("[Desktop Entry]", "Exec=\"/opt/old/Mesen\" %f",
			"Name=My Own Name", "Comment=I typed this", "X-Custom=keep me",
			"MimeType=application/x-mesen-nes;");

		string? fixedUp = LinuxFileAssociation.ReconcileDesktopEntry(entry, "/opt/new/Mesen", null, out string reason);

		Assert.NotNull(fixedUp);
		Assert.Contains("Name=My Own Name", fixedUp);
		Assert.Contains("Comment=I typed this", fixedUp);
		Assert.Contains("X-Custom=keep me", fixedUp);
	}

	//A second Exec= line would leave the loader to pick one, so the key is
	//replaced where it stands rather than appended.
	[Fact]
	public void An_update_replaces_an_exec_key_in_place_rather_than_adding_a_second()
	{
		string entry = Entry("[Desktop Entry]", "Exec=\"/opt/old/Mesen\" %f", "MimeType=application/x-mesen-nes;");

		string? fixedUp = LinuxFileAssociation.ReconcileDesktopEntry(entry, "/opt/new/Mesen", null, out string reason);

		Assert.NotNull(fixedUp);
		Assert.Single(Regex.Matches(fixedUp!, "^Exec=", RegexOptions.Multiline));
		Assert.Equal("Exec=\"/opt/new/Mesen\" %f", fixedUp!.Split(Environment.NewLine)[1]);
	}

	[Fact]
	public void An_update_adds_an_exec_key_when_the_entry_has_none()
	{
		string entry = Entry("[Desktop Entry]", "Type=Application", "Name=Mesen");

		string? fixedUp = LinuxFileAssociation.ReconcileDesktopEntry(entry, "/opt/new/Mesen", null, out string reason);

		Assert.NotNull(fixedUp);
		Assert.Contains("Exec=\"/opt/new/Mesen\" %f", fixedUp);
	}

	[Fact]
	public void An_update_still_reconciles_the_mime_types()
	{
		string entry = Entry("[Desktop Entry]", "Exec=\"/opt/old/Mesen\" %f", "MimeType=application/x-mesen-nes;");

		string? fixedUp = LinuxFileAssociation.ReconcileDesktopEntry(entry, "/opt/new/Mesen", new List<string> { "x-mesen-nes", "x-mesen-gb" }, out string reason);

		Assert.NotNull(fixedUp);
		Assert.Contains("MimeType=application/x-mesen-nes;application/x-mesen-gb", fixedUp);
	}

	//The refusal of #877 applies to the update as well: a path the Exec key cannot
	//carry is no reason to replace a working entry with an invalid one. Returning
	//null leaves the file as it stands, and reports why.
	[Fact]
	public void An_update_leaves_the_entry_alone_when_the_exec_key_cannot_carry_the_path()
	{
		string entry = Entry("[Desktop Entry]", "Exec=\"/opt/old/Mesen\" %f", "MimeType=application/x-mesen-nes;");

		Assert.Null(LinuxFileAssociation.ReconcileDesktopEntry(entry, "/home/u/apps/foo=1/Mesen", null, out string reason));
		Assert.Contains("=", reason);
	}

	//A .desktop file may hold more than one group, and a `[Desktop Action ...]`
	//group carries its own Exec= for a different command line. This writer never
	//writes one, so it is not the writer's to rewrite - reconciling "every line
	//that starts with Exec=" turns the action into a second copy of the
	//application command and silently drops whatever arguments it had.
	private static string EntryWithAnAction()
	{
		return Entry("[Desktop Entry]", "Exec=\"/opt/old/Mesen\" %f",
			"MimeType=application/x-mesen-nes;", "",
			"[Desktop Action NewWindow]", "Name=New Window",
			"Exec=\"/opt/old/Mesen\" %f --new-window");
	}

	[Fact]
	public void An_update_leaves_an_action_groups_exec_key_alone()
	{
		string? fixedUp = LinuxFileAssociation.ReconcileDesktopEntry(EntryWithAnAction(), "/opt/new/Mesen", null, out string reason);

		Assert.NotNull(fixedUp);
		Assert.Contains("Exec=\"/opt/old/Mesen\" %f --new-window", fixedUp);
		Assert.Equal("Exec=\"/opt/new/Mesen\" %f", fixedUp!.Split(Environment.NewLine)[1]);
	}

	//A key the entry lacks belongs to the application group, not to whichever
	//group happens to be last: appended at the end of the file it lands inside
	//the action, and the loader reads a MimeType the application group does not
	//have.
	[Fact]
	public void An_update_adds_a_missing_key_inside_the_desktop_entry_group()
	{
		string entry = Entry("[Desktop Entry]", "Exec=\"/opt/old/Mesen\" %f", "",
			"[Desktop Action NewWindow]", "Name=New Window",
			"Exec=\"/opt/old/Mesen\" %f --new-window");

		string? fixedUp = LinuxFileAssociation.ReconcileDesktopEntry(entry, "/opt/new/Mesen",
			new List<string> { "x-mesen-nes" }, out string reason);

		Assert.NotNull(fixedUp);
		string[] lines = fixedUp!.Split(Environment.NewLine);
		int mimeType = Array.IndexOf(lines, "MimeType=application/x-mesen-nes");
		int action = Array.IndexOf(lines, "[Desktop Action NewWindow]");
		Assert.True(action >= 0, "the action group should still be there");
		Assert.True(mimeType >= 0 && mimeType < action, $"the missing MimeType should be added to [Desktop Entry], got:\n{fixedUp}");
	}

	//Process.GetCurrentProcess().MainModule is null where the running executable
	//cannot be read. The path this replaced refreshed MimeType without ever asking
	//for the executable, so an unknown path must not stop the update: only the
	//Exec key is out of reach, MimeType is still ours.
	[Fact]
	public void An_update_still_reconciles_the_mime_types_without_an_executable_path()
	{
		string entry = Entry("[Desktop Entry]", "Exec=\"/opt/old/Mesen\" %f", "MimeType=application/x-mesen-nes;");

		string? fixedUp = LinuxFileAssociation.ReconcileDesktopEntry(entry, null, new List<string> { "x-mesen-gb" }, out string reason);

		Assert.NotNull(fixedUp);
		Assert.Contains("MimeType=application/x-mesen-gb", fixedUp);
		Assert.Contains("Exec=\"/opt/old/Mesen\" %f", fixedUp);
	}

	//A file with no [Desktop Entry] group at all is not loadable as it stands.
	//The header has to go BEFORE the keys that were already there: put after
	//them, they stay outside any group - the loader rejects the file, and the
	//stale Exec= the update was meant to repair survives outside the group.
	[Fact]
	public void An_update_gives_a_headerless_entry_the_group_its_keys_belong_to()
	{
		string entry = Entry("Exec=\"/opt/old/Mesen\" %f", "Type=Application");

		string? fixedUp = LinuxFileAssociation.ReconcileDesktopEntry(entry, "/opt/new/Mesen", null, out string reason);

		Assert.NotNull(fixedUp);
		Assert.Equal("[Desktop Entry]", fixedUp!.Split(Environment.NewLine)[0]);
		Assert.Contains("Exec=\"/opt/new/Mesen\" %f", fixedUp);
		Assert.Single(Regex.Matches(fixedUp, "^Exec=", RegexOptions.Multiline));
	}

	//The desktop spec forbids two groups with the same name, so such a file is
	//invalid - but GLib merges same-named groups with the last key winning, so
	//reconciling only the first one leaves a stale Exec= that the loader picks.
	[Fact]
	public void An_update_reconciles_every_desktop_entry_group()
	{
		string entry = Entry("[Desktop Entry]", "Exec=\"/opt/old/Mesen\" %f", "",
			"[Desktop Entry]", "Exec=\"/opt/stale/Mesen\" %f", "Name=Second");

		string? fixedUp = LinuxFileAssociation.ReconcileDesktopEntry(entry, "/opt/new/Mesen", null, out string reason);

		Assert.NotNull(fixedUp);
		Assert.DoesNotContain("/opt/old/Mesen", fixedUp);
		Assert.DoesNotContain("/opt/stale/Mesen", fixedUp);
		Assert.Equal(2, Regex.Matches(fixedUp!, "^Exec=", RegexOptions.Multiline).Count);
	}

	//Whitespace around the `=` is not in the desktop entry grammar, but a line
	//like this is not a reason to append a second Exec= for the loader to choose
	//between - the key is recognised and rewritten in place.
	[Fact]
	public void An_update_repairs_an_exec_key_written_with_spaces_around_the_equals()
	{
		string entry = Entry("[Desktop Entry]", "Exec =\"/opt/old/Mesen\" %f", "MimeType=application/x-mesen-nes;");

		string? fixedUp = LinuxFileAssociation.ReconcileDesktopEntry(entry, "/opt/new/Mesen", null, out string reason);

		Assert.NotNull(fixedUp);
		Assert.Equal("Exec=\"/opt/new/Mesen\" %f", fixedUp!.Split(Environment.NewLine)[1]);
		Assert.Single(Regex.Matches(fixedUp, "^Exec", RegexOptions.Multiline));
	}
}
