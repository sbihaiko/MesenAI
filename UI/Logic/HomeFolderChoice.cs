using System;
using System.Collections.Generic;

namespace Mesen.Logic
{
	//Host-free core of the data-folder choice (ADR-0201). The product renamed
	//its runtime surface to MesenAI, and the folder it reads settings, HD packs,
	//saves and recordings from was MesenCE - and Mesen2 before that. The rule is
	//adopt, never move: the new folder wins only when it already holds a
	//settings.json, otherwise the newest legacy folder that does is kept, and a
	//fresh install gets the new name. Nothing is copied or deleted, so a rename
	//cannot relocate or lose a user's data.
	//
	//Takes the candidates and a hasSettings predicate rather than reading the
	//filesystem, so the precedence is unit tested (HomeFolderChoiceTests) while
	//ConfigManager.HomeFolder owns the IO. Kept free of Avalonia/EmuApi so it can
	//be dual-compiled into UI.Tests (see UI.Tests/UI.Tests.csproj).
	public static class HomeFolderChoice
	{
		public static string Resolve(string newFolder, IReadOnlyList<string?> legacyFolders, Func<string, bool> hasSettings)
		{
			if(hasSettings(newFolder)) {
				return newFolder;
			}

			foreach(string? legacy in legacyFolders) {
				if(legacy != null && hasSettings(legacy)) {
					return legacy;
				}
			}

			//No folder has settings yet: a fresh install takes the new name.
			return newFolder;
		}
	}
}
