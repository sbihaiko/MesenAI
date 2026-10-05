using System;
using System.IO;
using Mesen.Config;
using Mesen.Utilities;
using Mesen.ViewModels;
using Xunit;

namespace Mesen.HeadlessTests;

//CodeRabbit's finding on #846 (2026-10-05). `PlayerSystemSettingsViewModel`'s
//storage write does two things that are not equally undoable: it points
//`ConfigManager.HomeFolder` at the chosen folder and saves the settings there,
//and then it moves the old folder's `settings.json` aside - because that file is
//what would make the folder being left win the *next* launch. If the move
//throws (a locked `settings.backup.json`, a folder that became read-only between
//the probe and the move), the old code left both folders holding a
//settings.json: the surface said "could not be written", and the next start
//silently brought the old folder back.
//
//The real write cannot be called from a test - the switch inside it writes to
//the process's own default folders - so the view-model has two seams (the three
//folder functions and `SwitchConfigFolder`) and this drives the real
//`WriteStorage` through them, against folders the case made.
public class SystemStorageWriteTests : IDisposable
{
	private readonly string _folder = Path.Combine(Path.GetTempPath(), "mesen-846-" + Guid.NewGuid().ToString("N"));

	public SystemStorageWriteTests()
	{
		Directory.CreateDirectory(Documents);
		Directory.CreateDirectory(Portable);
	}

	public void Dispose()
	{
		try {
			Directory.Delete(_folder, true);
		} catch {
			//Nothing to remove when a case failed before it built its tree.
		}
	}

	private string Documents => Path.Combine(_folder, "documents");
	private string Portable => Path.Combine(_folder, "portable");
	private string DocumentSettings => Path.Combine(Documents, "settings.json");
	private string PortableSettings => Path.Combine(Portable, "settings.json");
	private string PortableBackup => Path.Combine(Portable, "settings.backup.json");

	//A storage surface whose switch writes into the temp folders instead of the
	//process's own.
	private sealed class TestSystem : PlayerSystemSettingsViewModel
	{
		private readonly string _documentSettings;

		public TestSystem(string home, string documents, string portable)
			: base(() => home, () => documents, () => portable)
		{
			_documentSettings = Path.Combine(documents, "settings.json");
		}

		//What ConfigManager.CreateConfig does, without the real folders: the file
		//lands in the chosen folder.
		protected override void SwitchConfigFolder(bool storeInUserProfile)
		{
			File.WriteAllText(storeInUserProfile ? _documentSettings : Path.Combine(PortableFolder, "settings.json"), "written by the switch");
		}

		protected override void WriteKeyboard(DefaultKeyMappingType flags)
		{
		}

		protected override void RestartMesen()
		{
		}

		protected override void CloseMainWindow()
		{
		}

		public void Move(bool storeInUserProfile, string leaving) => WriteStorage(storeInUserProfile, leaving);
	}

	private TestSystem System(string home) => new(home, Documents, Portable);

	//The happy path: the settings land in the chosen folder and the file that
	//would win the next launch is set aside under the name Change Folder uses.
	[Fact]
	public void The_choice_is_written_and_the_folder_being_left_is_cleared()
	{
		File.WriteAllText(PortableSettings, "the folder being left");

		System(Documents).Move(storeInUserProfile: true, leaving: Portable);

		Assert.Equal("written by the switch", File.ReadAllText(DocumentSettings));
		Assert.False(File.Exists(PortableSettings), "the folder being left still has a settings.json, so it wins the next launch");
		Assert.Equal("the folder being left", File.ReadAllText(PortableBackup));
	}

	//The finding. `settings.backup.json` is a directory, so the move throws - the
	//same shape as a locked file - and the state has to be exactly what it was:
	//the old folder keeps its settings.json, and a settings.json the switch had
	//already written into the target is gone again.
	[Fact]
	public void A_move_that_fails_leaves_the_state_as_it_was()
	{
		File.WriteAllText(PortableSettings, "the folder being left");
		Directory.CreateDirectory(PortableBackup);

		Assert.ThrowsAny<Exception>(() => System(Documents).Move(storeInUserProfile: true, leaving: Portable));

		Assert.Equal("the folder being left", File.ReadAllText(PortableSettings));
		Assert.False(File.Exists(DocumentSettings), "the failed move left a settings.json behind in the folder it could not switch to");
		Assert.True(Directory.Exists(PortableBackup), "the rollback removed something it had not written");
	}

	//A settings file the target already had is put back, not deleted: the attempt
	//is undone, which is not the same thing as the target ending up empty.
	[Fact]
	public void A_target_that_already_had_a_settings_file_keeps_it()
	{
		File.WriteAllText(PortableSettings, "the folder being left");
		File.WriteAllText(DocumentSettings, "the target's own file, from an earlier attempt");
		Directory.CreateDirectory(PortableBackup);

		Assert.ThrowsAny<Exception>(() => System(Documents).Move(storeInUserProfile: true, leaving: Portable));

		Assert.Equal("the target's own file, from an earlier attempt", File.ReadAllText(DocumentSettings));
		Assert.Equal("the folder being left", File.ReadAllText(PortableSettings));
	}
}
