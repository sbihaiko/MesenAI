using Mesen.Logic;
using Xunit;

namespace Mesen.Tests.Config
{
	//#678: an unreadable settings.json is an existing install, so the defaults
	//Configuration falls back to are the upgrade path's, not a fresh unzip's.
	public class SettingsLoadDefaultsTests
	{
		[Fact]
		public void No_settings_file_gets_the_new_install_defaults()
		{
			MissingKeyDefaults d = SettingsLoadDefaults.For(settingsFileExists: false);
			Assert.Equal(UiMode.Player, d.UiMode);
			Assert.True(d.ClassicMenuNoticeShown);
			Assert.Equal(BootstrapRecordingDefault.ForNewInstall, d.BootstrapEnhancementFolder);
		}

		[Fact]
		public void An_existing_settings_file_gets_the_upgrade_defaults_even_when_unreadable()
		{
			MissingKeyDefaults d = SettingsLoadDefaults.For(settingsFileExists: true);
			Assert.Equal(UiMode.Advanced, d.UiMode);
			Assert.False(d.ClassicMenuNoticeShown);
			Assert.Equal(BootstrapRecordingDefault.ForExistingSettingsWithoutKey, d.BootstrapEnhancementFolder);
		}
	}
}
