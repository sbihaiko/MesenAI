using System;
using Mesen.Interop;

namespace Mesen.Logic
{
	//What the player's own word for a console is, for the rows that name the
	//console they are talking about (the Enhancements sheet's disabled Overclock
	//row, #1081). The name itself is a string the player reads, so it lives in
	//the locale files like every other one and this only names the id - the
	//caller resolves it, the way the picker's rows resolve RomConsoleKinds'
	//`RomConsole` + member ids.
	//
	//The row read the core enum instead, and `ResourceHelper.GetEnumText` answers
	//an id the locale file does not carry with the raw `[[Sms]]` placeholder -
	//which is what the player saw. So the console set is named here, exhaustively:
	//a console added to ConsoleType (InteropEnums.cs) has to be given a name in
	//this switch before it compiles, and ConsoleTypeNamesTests enumerates the enum
	//so that name must also reach resources.en.xml.
	//
	//Host-free (BCL + the dual-compiled Mesen.Interop.ConsoleType, ADR-0123).
	public static class ConsoleTypeNames
	{
		//Today's product consoles plus the ones the core can still load: a name is
		//needed for every member, not only for the consoles a panel can show, so
		//the table never has a hole for the next console to fall through.
		public static string MessageId(ConsoleType consoleType)
		{
			return consoleType switch {
				ConsoleType.Snes => "ConsoleTypeSnes",
				ConsoleType.Gameboy => "ConsoleTypeGameboy",
				ConsoleType.Nes => "ConsoleTypeNes",
				ConsoleType.PcEngine => "ConsoleTypePcEngine",
				ConsoleType.Sms => "ConsoleTypeSms",
				ConsoleType.Gba => "ConsoleTypeGba",
				ConsoleType.Ws => "ConsoleTypeWs",
				//A console this table does not name is a new member of
				//ConsoleType, and naming it is the whole point of the table: the
				//row must not fall back to the enum (that is the bug), and it must
				//not print a blank either. ConsoleTypeNamesTests walks the enum, so
				//this line is reached in the test run that adds one.
				_ => throw new ArgumentOutOfRangeException(nameof(consoleType), consoleType,
					"No name for this console: add it to ConsoleTypeNames and resources.en.xml")
			};
		}
	}
}
