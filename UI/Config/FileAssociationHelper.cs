using Avalonia.Platform;
using Avalonia.Threading;
using Mesen.Interop;
using Mesen.Logic;
using Mesen.Utilities;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;

namespace Mesen.Config
{
	class FileAssociationHelper
	{
		static private string CreateMimeType(string mimeType, string extension, string description, List<string> mimeTypes, bool addType)
		{
			string baseFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "mime", "packages");
			if(!Directory.Exists(baseFolder)) {
				Directory.CreateDirectory(baseFolder);
			}
			string filename = Path.Combine(baseFolder, mimeType + ".xml");

			if(addType) {
				FileHelper.WriteAllText(filename,
					"<?xml version=\"1.0\" encoding=\"utf-8\"?>" + Environment.NewLine +
					"<mime-info xmlns=\"http://www.freedesktop.org/standards/shared-mime-info\">" + Environment.NewLine +
					"\t<mime-type type=\"application/" + mimeType + "\">" + Environment.NewLine +
					"\t\t<glob-deleteall/>" + Environment.NewLine +
					"\t\t<glob pattern=\"*." + extension + "\"/>" + Environment.NewLine +
					"\t\t<comment>" + description + "</comment>" + Environment.NewLine +
					"\t\t<icon>MesenIcon</icon>" + Environment.NewLine +
					"\t</mime-type>" + Environment.NewLine +
					"</mime-info>" + Environment.NewLine);

				mimeTypes.Add(mimeType);
			} else if(File.Exists(filename)) {
				try {
					File.Delete(filename);
				} catch { }
			}
			return mimeType;
		}

		static public void UpdateLinuxFileAssociations()
		{
			PreferencesConfig cfg = ConfigManager.Config.Preferences;

			string baseFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
			string desktopFolder = Path.Combine(baseFolder, "applications");
			string mimeFolder = Path.Combine(baseFolder, "mime");
			string iconFolder = Path.Combine(baseFolder, "icons");
			if(!Directory.Exists(mimeFolder)) {
				Directory.CreateDirectory(mimeFolder);
			}
			if(!Directory.Exists(iconFolder)) {
				Directory.CreateDirectory(iconFolder);
			}
			if(!Directory.Exists(desktopFolder)) {
				Directory.CreateDirectory(desktopFolder);
			}

			List<string> mimeTypes = new List<string>();
			CreateMimeType("x-mesen-nes", "nes", "NES ROM", mimeTypes, cfg.AssociateNesRomFiles);
			CreateMimeType("x-mesen-fds", "fds", "FDS ROM", mimeTypes, cfg.AssociateNesRomFiles);
			CreateMimeType("x-mesen-qd", "qd", "FDS ROM (QD format)", mimeTypes, cfg.AssociateNesRomFiles);
			CreateMimeType("x-mesen-studybox", "studybox", "Studybox ROM (Famicom)", mimeTypes, cfg.AssociateNesRomFiles);
			CreateMimeType("x-mesen-unif", "unf", "NES ROM", mimeTypes, cfg.AssociateNesRomFiles);

			CreateMimeType("x-mesen-nsf", "nsf", "Nintendo Sound File", mimeTypes, cfg.AssociateNesMusicFiles);
			CreateMimeType("x-mesen-nsfe", "nsfe", "Nintendo Sound File (extended)", mimeTypes, cfg.AssociateNesMusicFiles);

			CreateMimeType("x-mesen-gb", "gb", "Game Boy ROM", mimeTypes, cfg.AssociateGbRomFiles);
			CreateMimeType("x-mesen-gbx", "gbx", "Game Boy ROM", mimeTypes, cfg.AssociateGbRomFiles);
			CreateMimeType("x-mesen-gbc", "gbc", "Game Boy Color ROM", mimeTypes, cfg.AssociateGbRomFiles);
			CreateMimeType("x-mesen-gbs", "gbs", "Game Boy Sound File", mimeTypes, cfg.AssociateGbMusicFiles);
			CreateMimeType("x-mesen-gba", "gba", "Game Boy Advance ROM", mimeTypes, cfg.AssociateGbaRomFiles);

			CreateMimeType("x-mesen-sms", "sms", "Master System ROM", mimeTypes, cfg.AssociateSmsRomFiles);
			CreateMimeType("x-mesen-gg", "gg", "Game Gear ROM", mimeTypes, cfg.AssociateGameGearRomFiles);
			CreateMimeType("x-mesen-sg", "sg", "SG-1000 ROM", mimeTypes, cfg.AssociateSgRomFiles);

			//Icon used for shortcuts
			using(FileStream file = File.Open(Path.Combine(iconFolder, "MesenIcon.png"), FileMode.OpenOrCreate, FileAccess.Write)) {
				AssetLoader.Open(new Uri("avares://Mesen/Assets/MesenIcon.png")).CopyTo(file);
			}

			string desktopFile = Path.Combine(desktopFolder, "mesen.desktop");
			if(!File.Exists(desktopFile)) {
				CreateLinuxShortcutFile(desktopFile, mimeTypes);
			} else {
				UpdateLinuxShortcutFile(desktopFile, mimeTypes);
			}

			//Update databases. The folder goes through ArgumentList, never a joined
			//command line: a path with a space would otherwise arrive as several
			//argv entries and the helper would never see it (#862).
			try {
				Process.Start(LinuxFileAssociation.DatabaseUpdateStartInfo("update-mime-database", mimeFolder))?.WaitForExit();
				Process.Start(LinuxFileAssociation.DatabaseUpdateStartInfo("update-desktop-database", desktopFolder));
			} catch {
				try {
					EmuApi.WriteLogEntry("An error occurred while updating file associations");
				} catch { }
			}
		}

		//#882: the line work moved into LinuxFileAssociation.ReconcileDesktopEntry
		//so the decision is testable without a Linux run - the same reason
		//BuildDesktopEntry is there (#877). What is reconciled, and what is left
		//alone, is now a return value instead of a branch behind
		//Process.GetCurrentProcess().MainModule.
		private static void UpdateLinuxShortcutFile(string desktopFile, List<string> mimeTypes)
		{
			string? content = FileHelper.ReadAllText(desktopFile);
			if(content == null) {
				return;
			}

			ProcessModule? mainModule = Process.GetCurrentProcess().MainModule;
			if(mainModule == null) {
				return;
			}

			string? updated = LinuxFileAssociation.ReconcileDesktopEntry(content, mainModule.FileName, mimeTypes, out _);
			if(updated != null) {
				FileHelper.WriteAllText(desktopFile, updated, new UTF8Encoding(false));
			}
		}

		static public void CreateLinuxShortcutFile(string filename, List<string>? mimeTypes = null)
		{
			ProcessModule? mainModule = Process.GetCurrentProcess().MainModule;
			if(mainModule == null) {
				return;
			}

			string? content = LinuxFileAssociation.BuildDesktopEntry(mainModule.FileName, mimeTypes, out string reason);
			if(content == null) {
				//#877: the Exec key cannot carry this path - an "=" in it, or a
				//control character with no escape - so there is no valid desktop
				//entry to write. Writing one anyway produces a file the desktop
				//environment rejects, and the failure is silent, so report why.
				try {
					EmuApi.WriteLogEntry("[FileAssociation] not writing " + filename + ": " + reason);
				} catch { }
				return;
			}

			FileHelper.WriteAllText(filename, content, new UTF8Encoding(false));
		}

		static public void UpdateFileAssociations()
		{
			try {
				if(OperatingSystem.IsWindows()) {
					FileAssociationHelper.UpdateWindowsFileAssociations();
				} else if(OperatingSystem.IsLinux()) {
					FileAssociationHelper.UpdateLinuxFileAssociations();
				}
			} catch(Exception ex) {
				Dispatcher.UIThread.Post(() => {
					MesenMsgBox.ShowException(ex);
				});
			}
		}

		static public void UpdateWindowsFileAssociations()
		{
			PreferencesConfig cfg = ConfigManager.Config.Preferences;
			FileAssociationHelper.UpdateFileAssociation("nes", cfg.AssociateNesRomFiles);
			FileAssociationHelper.UpdateFileAssociation("fds", cfg.AssociateNesRomFiles);
			FileAssociationHelper.UpdateFileAssociation("qd", cfg.AssociateNesRomFiles);
			FileAssociationHelper.UpdateFileAssociation("unf", cfg.AssociateNesRomFiles);
			FileAssociationHelper.UpdateFileAssociation("studybox", cfg.AssociateNesRomFiles);

			FileAssociationHelper.UpdateFileAssociation("nsf", cfg.AssociateNesMusicFiles);
			FileAssociationHelper.UpdateFileAssociation("nsfe", cfg.AssociateNesMusicFiles);

			FileAssociationHelper.UpdateFileAssociation("gb", cfg.AssociateGbRomFiles);
			FileAssociationHelper.UpdateFileAssociation("gbx", cfg.AssociateGbRomFiles);
			FileAssociationHelper.UpdateFileAssociation("gbc", cfg.AssociateGbRomFiles);
			FileAssociationHelper.UpdateFileAssociation("gbs", cfg.AssociateGbMusicFiles);

			FileAssociationHelper.UpdateFileAssociation("gba", cfg.AssociateGbaRomFiles);

			FileAssociationHelper.UpdateFileAssociation("sms", cfg.AssociateSmsRomFiles);
			FileAssociationHelper.UpdateFileAssociation("gg", cfg.AssociateGameGearRomFiles);
			FileAssociationHelper.UpdateFileAssociation("sg", cfg.AssociateSgRomFiles);
		}

		static private void UpdateFileAssociation(string extension, bool associate)
		{
			if(!OperatingSystem.IsWindows()) {
				return;
			}

			string key = @"HKEY_CURRENT_USER\Software\Classes\." + extension;
			if(associate) {
				ProcessModule? mainModule = Process.GetCurrentProcess().MainModule;
				if(mainModule != null) {
					Registry.SetValue(@"HKEY_CURRENT_USER\Software\Classes\Mesen\shell\open\command", null, mainModule.FileName + " \"%1\"");
					Registry.SetValue(key, null, "Mesen");
				}
			} else {
				object? regKey = Registry.GetValue(key, null, "");
				if(regKey != null && regKey.Equals("Mesen")) {
					Registry.SetValue(key, null, "");
				}
			}
		}
	}
}
