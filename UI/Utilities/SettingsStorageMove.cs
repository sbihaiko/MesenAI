using System;
using System.IO;

namespace Mesen.Utilities
{
	//ADR-0256 Decision 8's storage write, on its own so the part that can fail is
	//testable without touching the real user folders (ConfigManager.CreateConfig
	//writes to the process's own default folders, which a test must never do).
	//
	//The move has two halves and the first is not undoable by retrying: pointing
	//ConfigManager.HomeFolder at the target writes the settings there, and the
	//file left behind in the folder being left is what stops that folder winning
	//the *next* launch (ConfigManager.HomeFolder prefers a settings.json next to
	//the executable and otherwise takes a documents folder that holds one). If
	//the second half throws - a locked settings.backup.json, a folder that
	//became read-only between the probe and the move - both folders hold a
	//settings.json, the old one wins on the next start, and the player's choice
	//has silently reverted while the surface said it failed.
	//
	//So every file the move touches is read into memory first, and Rollback puts
	//all three back - including one the target already had before the attempt.
	//The caller restores the in-memory folder beside it (ConfigManager.
	//RestoreHomeFolder): the file state alone is not what the process was using.
	public sealed class SettingsStorageMove
	{
		private const string SettingsFile = "settings.json";
		private const string BackupFile = "settings.backup.json";

		private readonly string _leavingSettings;
		private readonly string _leavingBackup;
		private readonly FileSnapshot _target;
		private readonly FileSnapshot _leaving;
		private readonly FileSnapshot _backup;

		private SettingsStorageMove(string targetFolder, string leavingFolder)
		{
			_leavingSettings = Path.Combine(leavingFolder, SettingsFile);
			_leavingBackup = Path.Combine(leavingFolder, BackupFile);
			_target = FileSnapshot.Of(Path.Combine(targetFolder, SettingsFile));
			_leaving = FileSnapshot.Of(_leavingSettings);
			_backup = FileSnapshot.Of(_leavingBackup);
		}

		//Snapshots the three files the move touches. Call it before the folder
		//switch; a move that finishes has nothing to undo and the object is
		//simply dropped.
		public static SettingsStorageMove Begin(string targetFolder, string leavingFolder)
		{
			return new SettingsStorageMove(targetFolder, leavingFolder);
		}

		//What the folder being left must not keep: a settings.json of its own.
		//The name is the one the Advanced door's Change Folder window already uses
		//(SelectStorageFolderViewModel.MigrateData), so a player who looks finds
		//their old file under a name that says what it is.
		public void MoveOldSettingsAside()
		{
			if(!File.Exists(_leavingSettings)) {
				return;
			}
			if(File.Exists(_leavingBackup)) {
				File.Delete(_leavingBackup);
			}
			File.Move(_leavingSettings, _leavingBackup);
		}

		//Every file as it was before the attempt. Safe after a partial failure:
		//each snapshot knows whether its file existed, and one that could not be
		//read at all is left untouched rather than deleted.
		public void Rollback()
		{
			_target.Restore();
			_leaving.Restore();
			_backup.Restore();
		}

		//One file's bytes, or the fact that it was not there.
		private readonly struct FileSnapshot
		{
			private readonly string _path;
			private readonly byte[]? _bytes;
			//False when the file could not be read: restoring then means doing
			//nothing, never deleting a file this move never managed to look at.
			private readonly bool _known;

			private FileSnapshot(string path, byte[]? bytes, bool known)
			{
				_path = path;
				_bytes = bytes;
				_known = known;
			}

			public static FileSnapshot Of(string path)
			{
				try {
					return new FileSnapshot(path, File.Exists(path) ? File.ReadAllBytes(path) : null, known: true);
				} catch {
					return new FileSnapshot(path, null, known: false);
				}
			}

			public void Restore()
			{
				if(!_known) {
					return;
				}
				try {
					if(_bytes is null) {
						if(File.Exists(_path)) {
							File.Delete(_path);
						}
						return;
					}
					File.WriteAllBytes(_path, _bytes);
				} catch {
					//The rollback is the best effort of a path that has already
					//failed; the caller's own message is what the player sees.
				}
			}
		}
	}
}
