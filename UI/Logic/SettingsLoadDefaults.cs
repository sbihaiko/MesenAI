namespace Mesen.Logic;

//#678: the one-shot defaults Configuration.CreateConfig applies to the keys a
//new Configuration would otherwise take from its property initializers. Which
//set applies depends only on whether a settings.json was there at startup -
//not on whether it could be read: a corrupt or `null` file still belongs to an
//existing install, so it gets the upgrade-path values (Advanced, the classic-menu
//toast still owed, "record while I play" kept on), never the fresh-unzip ones.
public sealed record MissingKeyDefaults(UiMode UiMode, bool ClassicMenuNoticeShown, bool BootstrapEnhancementFolder);

public static class SettingsLoadDefaults
{
	public static MissingKeyDefaults For(bool settingsFileExists)
	{
		return new MissingKeyDefaults(
			UiModeDefaultRule.ForMissingKey(settingsFileExists),
			ClassicMenuNotice.ShownForMissingKey(settingsFileExists),
			settingsFileExists ? BootstrapRecordingDefault.ForExistingSettingsWithoutKey : BootstrapRecordingDefault.ForNewInstall
		);
	}
}
