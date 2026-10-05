using Mesen.Config;
using Xunit;

namespace Mesen.HeadlessTests;

//ADR-0255, the keyboard case: "se nenhum controle estiver conectado o teclado
//deve estar configurado de maneira padrao e permitir o jogo".
//
//A fresh install already satisfies that - PlayFirstRun.Mappings always turns on
//a pad preset and one keyboard preset - but the presets are only ever applied
//on first run (Configuration.InitializeDefaults). A settings.json that already
//carries DefaultKeyMappings = 0 loads straight through, and the guard that
//refuses to *write* None lives in ConfigManager.ResetSettings, which only runs
//for callers that reset. So the player ends up with no pad preset and no
//keyboard preset: nothing plays, and the menus that would fix it are reachable
//only by mouse.
//
//What is asserted here is the guard's decision - which configs it writes and
//which it leaves alone. What is deliberately NOT asserted is that the resulting
//mappings hold real key codes. They cannot, in a headless host: the presets bind
//through InputApi.GetKeyCode, which is KeyManager::GetKeyCode behind a null
//check, and the IKeyManager is only registered by InitializeEmu when it was
//given both a window and a renderer handle - which a headless test never has (it
//is also why this project's other native tests pass `noInput: true`). Every
//preset applied here binds zeros, and asserting on that would assert the
//harness, not the rule. The ordering that makes it real in the app is the
//comment at the call site in MainWindow: "InitializeDefaults must be after
//InitializeEmu, otherwise keybindings will be empty".
[Collection(NativeCoreCollection.Name)]
public class KeyboardPresetRecoveryTests
{
	private static Configuration EmptyConfig()
		=> new() { DefaultKeyMappings = DefaultKeyMappingType.None };

	[Fact]
	public void A_config_with_no_preset_and_nothing_bound_is_given_one()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		Configuration cfg = EmptyConfig();

		Assert.True(cfg.RestoreKeyboardPresetIfNothingIsBound());

		Assert.NotEqual(DefaultKeyMappingType.None, cfg.DefaultKeyMappings);
		Assert.True(cfg.DefaultKeyMappings.HasFlag(DefaultKeyMappingType.ArrowKeys));
		//The pad presets come back too: None meant *no* preset, so restoring only
		//the keyboard would leave a player who does have a pad worse off than a
		//fresh install.
		Assert.True(cfg.DefaultKeyMappings.HasFlag(DefaultKeyMappingType.Xbox));
	}

	//The guard must not fire over a config the player built by hand: None with
	//keys in it is a config someone assembled deliberately, and re-applying a
	//preset would overwrite their bindings - the same bug in reverse.
	[Fact]
	public void A_config_whose_keys_were_bound_by_hand_is_left_alone()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		Configuration cfg = EmptyConfig();
		cfg.Nes.Port1.Mapping1.A = 0x1234;

		Assert.False(cfg.RestoreKeyboardPresetIfNothingIsBound());

		Assert.Equal(DefaultKeyMappingType.None, cfg.DefaultKeyMappings);
		Assert.Equal(0x1234, cfg.Nes.Port1.Mapping1.A);
	}

	//A config that does name a preset is untouched: the guard is about None, not
	//about "has no keys yet".
	[Fact]
	public void A_config_that_names_a_preset_keeps_it()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		Configuration cfg = new() { DefaultKeyMappings = DefaultKeyMappingType.WasdKeys };

		Assert.False(cfg.RestoreKeyboardPresetIfNothingIsBound());

		Assert.Equal(DefaultKeyMappingType.WasdKeys, cfg.DefaultKeyMappings);
	}

	//UpgradeConfig runs on every load, so a second pass must find nothing to do.
	//The return value is the contract: true means "this call is what changed the
	//config", and a guard that reported true twice would be re-applying a preset
	//over the one it had just set.
	[Fact]
	public void The_guard_reports_that_it_wrote_only_once()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		Configuration cfg = EmptyConfig();

		Assert.True(cfg.RestoreKeyboardPresetIfNothingIsBound());
		Assert.False(cfg.RestoreKeyboardPresetIfNothingIsBound());
	}
}
