using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Mesen.Logic;
using Xunit;

namespace Mesen.Tests.Play;

//W-P15 (#912): on macOS a pad that exposes only the micro (Siri Remote, the
//one-button pads) or the older basic GameController profile sends no key, so
//the unknown-pad pill never fires for it and it cannot be set up. The pad is a
//pad whatever profile it has, and its own profile's elements drive the same
//button bits every other pad on this host drives.
//
//The mapping itself is Objective-C++ the unit tests cannot compile, so it is
//written as data in the backend and read back off disk here - the idiom
//UI.HeadlessTests/PlayerControllerSheetTests already uses for the key tables.
//MacOSKeyManager's own `buttonNames` order is the independent source of truth
//the bits are checked against, and the pill rule is driven for real: a micro
//pad's first key raises ShowPill and its Start key opens the sheet.
public class MacOSPadProfileTests
{
	private const string KeyManager = "MacOSKeyManager.mm";
	private const string GameController = "MacOSGameController.mm";

	private static readonly string KeyManagerSource = ReadSource("MacOS", KeyManager);
	private static readonly string GameControllerSource = ReadSource("MacOS", GameController);

	private static string ReadSource(params string[] parts)
	{
		DirectoryInfo? dir = new(AppContext.BaseDirectory);
		while(dir != null && !File.Exists(Path.Combine(dir.FullName, "Mesen.sln"))) {
			dir = dir.Parent;
		}
		Assert.NotNull(dir);
		return File.ReadAllText(Path.Combine(dir!.FullName, Path.Combine(parts)));
	}

	//The pad's element -> the console button it is. Apple's element sets: a
	//micro gamepad has A, X, Menu and a direction pad; a basic one has all four
	//face buttons, both shoulders and the direction pad.
	private static readonly Dictionary<string, string> NamedElements = new() {
		["buttonA"] = "A",
		["buttonB"] = "B",
		["buttonX"] = "X",
		["buttonY"] = "Y",
		["leftShoulder"] = "L1",
		["rightShoulder"] = "R1",
		["buttonMenu"] = "Start",
	};

	private static readonly Dictionary<string, string[]> ProfileElements = new() {
		["kMicroGamepadKeys"] = new[] { "buttonA", "buttonX", "buttonMenu", "dpad" },
		["kBasicGamepadKeys"] = new[] { "buttonA", "buttonB", "buttonX", "buttonY", "leftShoulder", "rightShoulder", "dpad" },
	};

	private static string[] ButtonNames()
	{
		Match table = Regex.Match(KeyManagerSource, @"vector<string>\s+buttonNames\s*=\s*\{(.*?)\};", RegexOptions.Singleline);
		Assert.True(table.Success, $"{KeyManager} has no buttonNames table");
		string[] names = Regex.Matches(table.Groups[1].Value, "\"([^\"]*)\"").Select(m => m.Groups[1].Value).ToArray();
		Assert.True(names.Length >= 24, $"{KeyManager} names only {names.Length} buttons");
		return names;
	}

	//A profile's table, element and bit as written (the direction pad is a token,
	//not a number: it drives four bits).
	private static (string Element, string Bit)[] Table(string name)
	{
		Match table = Regex.Match(GameControllerSource, name + @"\[\]\s*=\s*\{(.*?)\n\};", RegexOptions.Singleline);
		Assert.True(table.Success, $"{GameController} has no {name} table");
		return Regex.Matches(table.Groups[1].Value, "\"([^\"]+)\"\\s*,\\s*([A-Za-z0-9_]+)")
			.Select(m => (m.Groups[1].Value, m.Groups[2].Value)).ToArray();
	}

	private static (string Token, int Bit) DpadToken()
	{
		Match sentinel = Regex.Match(GameControllerSource, @"static const int (\w+) = (-?\d+);");
		Assert.True(sentinel.Success, $"{GameController} has no direction-pad bit sentinel");
		return (sentinel.Groups[1].Value, int.Parse(sentinel.Groups[2].Value));
	}

	[Fact]
	public void A_micro_or_basic_pads_elements_drive_the_console_button_the_key_manager_names()
	{
		string[] names = ButtonNames();
		(string Token, int Bit) dpad = DpadToken();

		foreach((string table, string[] elements) in ProfileElements) {
			(string Element, string Bit)[] keys = Table(table);
			Assert.Equal(elements, keys.Select(k => k.Element).ToArray());

			foreach((string element, string bit) in keys) {
				if(element == "dpad") {
					//A direction pad is four keys, not one: the four the table that
					//names every pad's Up/Down/Left/Right puts at 8..11.
					Assert.Equal(dpad.Token, bit);
					Assert.Equal(new[] { "Up", "Down", "Left", "Right" }, names[8..12]);
					continue;
				}
				Assert.True(NamedElements.ContainsKey(element), $"{element} is not an element of that profile");
				Assert.Equal(NamedElements[element], names[int.Parse(bit)]);
			}
		}

		//The tables are what the handlers land the elements on - a table nothing
		//reads would pass the checks above and map nothing.
		Assert.Contains("HandleProfileElement(input, element, kMicroGamepadKeys", GameControllerSource);
		Assert.Contains("HandleProfileElement(input, element, kBasicGamepadKeys", GameControllerSource);
	}

	[Fact]
	public void A_micro_pad_is_kept_and_its_own_keys_raise_the_unknown_pad_pill()
	{
		//AddController asks the backend instead of dropping every pad that has no
		//extended gamepad, so the pad gets a slot and can send keys at all.
		Match add = Regex.Match(KeyManagerSource, @"MacOSKeyManager::AddController\(void\* cont\)\s*\{(.*?)\n\}", RegexOptions.Singleline);
		Assert.True(add.Success, $"{KeyManager} has no AddController");
		Assert.Contains("if(!MacOSGameController::Supports(controller))", add.Groups[1].Value);
		Assert.Contains("_controllers.push_back", add.Groups[1].Value);

		//Supports answers for all three profiles a pad can expose, and only those.
		Match supports = Regex.Match(GameControllerSource, @"MacOSGameController::Supports\(GCController\* controller\)\s*\{(.*?)\n\}", RegexOptions.Singleline);
		Assert.True(supports.Success, $"{GameController} has no Supports");
		foreach(string profile in new[] { "extendedGamepad", "microGamepad", "[controller gamepad]" }) {
			Assert.Contains(profile, supports.Groups[1].Value);
		}

		//A micro pad's keys land in the base gamepad family the pill rule reads,
		//whose names are the key manager's own: buttonA is Pad1 A and buttonMenu
		//is Pad1 Start.
		string[] names = ButtonNames();
		(string Token, int Bit) dpad = DpadToken();
		Dictionary<string, ushort> keys = Table("kMicroGamepadKeys")
			.Where(k => k.Element != "dpad")
			.ToDictionary(k => k.Element, k => (ushort)(ControllerDevices.BaseGamepadIndex + int.Parse(k.Bit)));
		Assert.Equal(0, ControllerDevices.DeviceOf(keys["buttonA"]));
		Assert.Equal(0, ControllerDevices.DeviceOf(keys["buttonMenu"]));
		Assert.Equal("A", names[keys["buttonA"] - ControllerDevices.BaseGamepadIndex]);
		Assert.Equal("Start", names[keys["buttonMenu"] - ControllerDevices.BaseGamepadIndex]);

		UnknownControllerDetector detector = new();
		Func<ushort, bool> isStart = key => names[key - ControllerDevices.BaseGamepadIndex] == "Start";

		//Its first press raises the pill for its device, with nothing mapped to it.
		Assert.Equal(DetectorEvent.ShowPill,
			detector.OnPressed(new[] { keys["buttonA"] }, Array.Empty<ushort>(), isStart, TimeSpan.Zero));
		Assert.Equal(0, detector.PillDevice);

		//Start on that pad - buttonMenu, the bit the table gives that name - is
		//what opens the sheet that writes the pad's mapping.
		Assert.Equal(DetectorEvent.OpenSheet,
			detector.OnPressed(new[] { keys["buttonA"], keys["buttonMenu"] }, Array.Empty<ushort>(), isStart, TimeSpan.FromSeconds(1)));
		Assert.Equal(0, detector.SheetDevice);
	}

	[Fact]
	public void An_extended_pad_still_drives_the_sixteen_bits_it_always_drove()
	{
		//A pad that exposes all three profiles takes the extended branch, and its
		//handler is untouched: one element per bit, up to the sticks. The four
		//direction bits are the one group it lands through the direction-pad
		//handler, as they always were.
		Match block = Regex.Match(GameControllerSource,
			@"if\(_extended != nil\) \{(.*?)\} else if\(_micro != nil\)", RegexOptions.Singleline);
		Assert.True(block.Success, $"{GameController} no longer branches on the extended profile first");
		foreach(int bit in Enumerable.Range(0, 16).Where(b => b is < 8 or > 11)) {
			Assert.Contains($"_buttonState[{bit}] =", block.Groups[1].Value);
		}
		Assert.Contains("HandleDpad(", block.Groups[1].Value);
		Assert.Contains("HandleThumbstick(", block.Groups[1].Value);
	}
}
