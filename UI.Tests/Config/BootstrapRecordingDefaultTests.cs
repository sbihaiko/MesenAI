using Mesen.Logic;
using Xunit;

namespace Mesen.Tests.Config
{
	//ADR-0243 Q3 (F12.20): Play no longer records by itself. The setting
	//EnhancementPackConfig.BootstrapEnhancementFolder stays, off for a new
	//install; an install that had it on keeps it on and is told so once.
	public class BootstrapRecordingDefaultTests
	{
		[Fact]
		public void NewInstall_RecordsOnlyOnDemand()
		{
			//Configuration.CreateConfig (no settings.json) writes this value.
			Assert.False(BootstrapRecordingDefault.ForNewInstall);
		}

		[Fact]
		public void ExistingSettingsWithoutTheKey_KeepTheOldBehaviour()
		{
			//The property initializer is what an existing settings.json without
			//the key deserializes to; such an install was recording on load.
			Assert.True(BootstrapRecordingDefault.ForExistingSettingsWithoutKey);
		}

		[Theory]
		[InlineData(true, true, true)]
		[InlineData(true, false, false)]
		[InlineData(false, true, false)]
		[InlineData(false, false, false)]
		public void UpgradeNotice_OnlyForAnUpgradeThatKeptItOn(bool upgradingFromBefore, bool enabled, bool expected)
		{
			Assert.Equal(expected, BootstrapRecordingDefault.UpgradeNoticeDue(upgradingFromBefore, enabled));
		}
	}
}
