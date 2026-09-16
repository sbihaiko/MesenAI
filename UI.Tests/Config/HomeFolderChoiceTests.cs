using System.Collections.Generic;
using Mesen.Logic;
using Xunit;

namespace Mesen.Tests.Config
{
	//ADR-0201: the product renamed its runtime surface to MesenAI, and the data
	//folder it reads settings, HD packs, saves and recordings from was MesenCE
	//(and Mesen2 before that). The rule is adopt, never move - the point being
	//that a rename must not relocate a user's data. HomeFolderChoice is the
	//pure core of that rule; ConfigManager.HomeFolder owns the filesystem IO
	//and passes in the candidates plus a hasSettings predicate.
	public class HomeFolderChoiceTests
	{
		private const string New = "/data/MesenAI";
		private const string MesenCe = "/data/MesenCE";
		private const string Mesen2 = "/data/Mesen2";

		private static string Resolve(params string[] foldersWithSettings)
		{
			HashSet<string> existing = new(foldersWithSettings);
			return HomeFolderChoice.Resolve(New, new string?[] { MesenCe, Mesen2 }, existing.Contains);
		}

		[Fact]
		public void FreshInstall_TakesTheNewFolder()
		{
			Assert.Equal(New, Resolve());
		}

		[Fact]
		public void ExistingMesenCeInstall_KeepsItsFolder()
		{
			//The whole point: an install that already runs out of MesenCE is not
			//moved, so its packs, saves and recordings stay where they are.
			Assert.Equal(MesenCe, Resolve(MesenCe));
		}

		[Fact]
		public void ExistingMesen2Install_StillKeepsItsFolder()
		{
			//The chain this ADR extends rather than replaces: Mesen2 remains the
			//last resort when neither newer folder has settings.
			Assert.Equal(Mesen2, Resolve(Mesen2));
		}

		[Fact]
		public void NewestLegacyWins_WhenBothLegacyFoldersExist()
		{
			Assert.Equal(MesenCe, Resolve(MesenCe, Mesen2));
		}

		[Fact]
		public void NewFolderWinsOverEveryLegacyFolder()
		{
			//Once an install has actually been created under the new name, it is
			//the one that runs - a stale MesenCE folder beside it must not win.
			Assert.Equal(New, Resolve(New, MesenCe, Mesen2));
		}

		[Fact]
		public void NullLegacyCandidate_IsSkipped()
		{
			//The candidates a caller builds from ExistingFolder() are null when
			//that folder has no settings.json; a null must not be dereferenced.
			Assert.Equal(New, HomeFolderChoice.Resolve(New, new string?[] { null, null }, _ => false));
		}

		[Fact]
		public void SettingsInANewerFolder_DoesNotRescueAnOlderOne()
		{
			//Order matters: Mesen2 having settings must not beat MesenCE having
			//them, because MesenCE is the more recent generation.
			Assert.Equal(MesenCe, Resolve(MesenCe));
			Assert.NotEqual(Mesen2, Resolve(MesenCe, Mesen2));
		}
	}
}
