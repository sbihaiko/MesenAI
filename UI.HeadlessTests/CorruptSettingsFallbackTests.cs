using System;
using System.IO;
using Mesen.Config;
using Mesen.Logic;
using Xunit;

namespace Mesen.HeadlessTests;

//#678: a settings.json that exists but cannot be read (truncated JSON, or the
//literal `null`) used to fall back to Configuration.CreateConfig(), i.e. the
//new-install defaults - an Advanced user with a corrupt file came back in
//Player with "record while I play" off. The rule (SettingsLoadDefaults) is
//host-free in UI.Tests; this checks the wiring in Configuration.Deserialize,
//which only this project can reach.
public class CorruptSettingsFallbackTests : IDisposable
{
	private readonly string _folder = Path.Combine(Path.GetTempPath(), "mesen-678-" + Guid.NewGuid().ToString("N"));

	public CorruptSettingsFallbackTests()
	{
		Directory.CreateDirectory(_folder);
	}

	public void Dispose()
	{
		try {
			Directory.Delete(_folder, true);
		} catch(IOException) {
		}
	}

	private Configuration Load(string contents)
	{
		string file = Path.Combine(_folder, "settings.json");
		File.WriteAllText(file, contents);
		Configuration config = Configuration.Deserialize(file);
		//~Configuration saves to ConfigManager.ConfigFile; a test config must not.
		GC.SuppressFinalize(config);
		return config;
	}

	[Theory]
	[InlineData("{\"Preferences\": {\"UiMode\": \"Adv")]
	[InlineData("null")]
	public void An_unreadable_existing_file_gets_the_upgrade_defaults(string contents)
	{
		Configuration config = Load(contents);

		Assert.Equal(UiMode.Advanced, config.Preferences.UiMode);
		Assert.False(config.Preferences.ClassicMenuNoticeShown);
		Assert.True(config.EnhancementPacks.BootstrapEnhancementFolder);
		//The key mappings and fonts were lost with the file: they still get
		//their first-run initialization.
		Assert.Equal((int)ConfigUpgradeHint.FirstRun, config.ConfigUpgrade);
	}

	[Theory]
	[InlineData("{\"Preferences\": {\"UiMode\": \"Adv")]
	[InlineData("null")]
	public void An_unreadable_existing_file_is_backed_up_before_it_is_overwritten(string contents)
	{
		Load(contents);

		string[] backups = Directory.GetFiles(_folder, "settings.*.bak");
		Assert.Single(backups);
		Assert.Equal(contents, File.ReadAllText(backups[0]));
	}
}
