namespace Mesen.Logic;

//ADR-0243 Q3 (F12.20): Play no longer records a ROM by itself. The setting
//EnhancementPackConfig.BootstrapEnhancementFolder ("record while I play")
//stays, off for a new install; an install that had it on keeps it on and is
//told once, on the upgrade, that recording is now Remaster's Record.
public static class BootstrapRecordingDefault
{
	//Configuration.CreateConfig (no settings.json: a fresh unzip) writes this.
	public const bool ForNewInstall = false;

	//The property initializer: what an existing settings.json without the key
	//deserializes to. That install was recording on every load, and keeps it.
	public const bool ForExistingSettingsWithoutKey = true;

	//upgradingFromBefore: the settings were written by a build older than this
	//change (ConfigUpgrade below ConfigUpgradeHint.RecordingOnDemand) and are
	//not a first run. The stored value is never changed - the notice is the
	//whole migration.
	public static bool UpgradeNoticeDue(bool upgradingFromBefore, bool enabled)
	{
		return upgradingFromBefore && enabled;
	}
}
