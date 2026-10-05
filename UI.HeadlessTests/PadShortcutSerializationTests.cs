using System.Text.Json;
using Mesen.Config;
using Mesen.Config.Shortcuts;
using Xunit;

namespace Mesen.HeadlessTests;

//ADR-0255 slice 4: ShortcutKeyInfo grew a slot, and the file that records it is
//every user's settings.json. Configuration.Serialize only writes when the
//serialized text differs from what was loaded, so a slot that adds a key to
//every document - even an empty one - rewrites the file of every install on
//upgrade. This asserts the two halves of that: a document written before the
//slot existed loads with it null and is written back byte for byte, and a
//document that uses it carries only what the player set.
//
//It lives here, not in UI.Tests, because the app's own source-generated
//serializer (MesenSerializerContext) is the thing under test and UI.Tests is
//host-free (ADR-0123). Nothing here touches the native core: no InputApi, so
//PadShortcutBinding.IsAxis is out of reach on purpose - PadAxisAction covers the
//name classification host-free in UI.Tests/Input/PadAxisActionTests.
public class PadShortcutSerializationTests
{
	private static Configuration ConfigWith(params ShortcutKeyInfo[] shortcuts)
	{
		Configuration config = Configuration.CreateConfig();
		foreach(ShortcutKeyInfo shortcut in shortcuts) {
			config.Preferences.ShortcutKeys.Add(shortcut);
		}
		return config;
	}

	private static ShortcutKeyInfo KeyOnly(EmulatorShortcut shortcut, ushort key1)
	{
		return new ShortcutKeyInfo { Shortcut = shortcut, KeyCombination = new KeyCombination() { Key1 = key1 } };
	}

	private static string Serialize(Configuration config)
	{
		return JsonSerializer.Serialize(config, typeof(Configuration), MesenSerializerContext.Default);
	}

	private static Configuration Deserialize(string json)
	{
		Configuration? loaded = (Configuration?)JsonSerializer.Deserialize(json, typeof(Configuration), MesenSerializerContext.Default);
		Assert.NotNull(loaded);
		return loaded!;
	}

	[Fact]
	public void A_document_written_before_the_slot_existed_loads_and_writes_back_identically()
	{
		Configuration before = ConfigWith(KeyOnly(EmulatorShortcut.Rewind, 8), KeyOnly(EmulatorShortcut.Pause, 19));
		string json = Serialize(before);

		//No shortcut carries a pad binding, so the slot puts no key in the
		//document at all - which is what makes the round trip below byte-stable
		//for an install that never uses it.
		Assert.DoesNotContain("PadBinding", json);

		Configuration loaded = Deserialize(json);
		Assert.All(loaded.Preferences.ShortcutKeys, sk => Assert.Null(sk.PadBinding));
		Assert.Equal(json, Serialize(loaded));
	}

	[Fact]
	public void A_shortcut_with_a_button_and_no_key_round_trips()
	{
		Configuration config = ConfigWith(new ShortcutKeyInfo {
			Shortcut = EmulatorShortcut.SaveState,
			PadBinding = new PadShortcutBinding() { KeyCode = 0x1006 }
		});
		string json = Serialize(config);

		using(JsonDocument doc = JsonDocument.Parse(json)) {
			JsonElement slot = doc.RootElement.GetProperty("Preferences").GetProperty("ShortcutKeys")[0].GetProperty("PadBinding");
			Assert.Equal(0x1006, slot.GetProperty("KeyCode").GetInt32());
			//A button has no use for a threshold, so the player's setting is not
			//written as a null that means nothing.
			Assert.False(slot.TryGetProperty("ThresholdPercent", out _));
		}

		Configuration loaded = Deserialize(json);
		ShortcutKeyInfo info = loaded.Preferences.ShortcutKeys[0];
		Assert.NotNull(info.PadBinding);
		Assert.False(info.PadBinding!.IsEmpty);
		Assert.Equal(0x1006, info.PadBinding.KeyCode);
		//Both key slots stay empty: this is the "a button and no key" case.
		Assert.True(info.KeyCombination.IsEmpty);
		Assert.True(info.KeyCombination2.IsEmpty);
		Assert.Null(info.PadBinding.ThresholdPercent);
		Assert.Equal(PadAxisActionDefault, info.PadBinding.EffectiveThresholdPercent);
		Assert.Equal(json, Serialize(loaded));
	}

	//Mirrors PadAxisAction.DefaultThresholdPercent: a plain const here so this
	//project's test does not silently follow a changed default - if the default
	//moves, the pad's behaviour at the default deadzone moves with it and this
	//assertion is where that is noticed.
	private const int PadAxisActionDefault = 40;

	[Fact]
	public void A_player_set_threshold_is_written_and_survives_the_round_trip()
	{
		Configuration config = ConfigWith(new ShortcutKeyInfo {
			Shortcut = EmulatorShortcut.Rewind,
			PadBinding = new PadShortcutBinding() { KeyCode = 0x100F, ThresholdPercent = 80 }
		});
		string json = Serialize(config);

		using(JsonDocument doc = JsonDocument.Parse(json)) {
			JsonElement slot = doc.RootElement.GetProperty("Preferences").GetProperty("ShortcutKeys")[0].GetProperty("PadBinding");
			Assert.Equal(80, slot.GetProperty("ThresholdPercent").GetInt32());
		}

		Configuration loaded = Deserialize(json);
		Assert.Equal(80, loaded.Preferences.ShortcutKeys[0].PadBinding!.EffectiveThresholdPercent);
		Assert.Equal(json, Serialize(loaded));
	}

	[Fact]
	public void An_out_of_range_threshold_resolves_clamped()
	{
		//A hand-edited settings.json is not trusted: 0 would read as held at rest
		//and past 100% could never be reached (PadAxisAction.ClampPercent).
		Configuration config = ConfigWith(new ShortcutKeyInfo {
			Shortcut = EmulatorShortcut.Rewind,
			PadBinding = new PadShortcutBinding() { KeyCode = 0x100F, ThresholdPercent = 0 }
		});
		string json = Serialize(config);

		Configuration loaded = Deserialize(json);
		Assert.Equal(1, loaded.Preferences.ShortcutKeys[0].PadBinding!.EffectiveThresholdPercent);
		//Clamped on read, not on write: the file keeps what it said, so a
		//downgrade-and-upgrade cycle cannot rewrite the player's document.
		Assert.Equal(0, loaded.Preferences.ShortcutKeys[0].PadBinding!.ThresholdPercent);
		Assert.Equal(json, Serialize(loaded));
	}
}
