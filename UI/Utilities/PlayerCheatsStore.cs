using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Mesen.Config;
using Mesen.Interop;
using Mesen.Logic;

namespace Mesen.Utilities
{
	//P.10 (ADR-0245 §1): the host side of the W-P11 Cheats sheet. Reads the
	//bundled database and the per-game CheatCodes list the classic cheat
	//window uses, and writes the sheet's toggles back to that same list - one
	//store, so the two windows always agree (rule 12). The rules live in the
	//host-free UI/Logic/CheatSheet; this class only maps CheatCode to
	//StoredCheat and back and does the I/O.
	public static class PlayerCheatsStore
	{
		private static readonly Dictionary<ConsoleType, IReadOnlyList<CheatDbGame>> _dbCache = new();

		//The bundled list for a console (CheatDb.<Console>.json), parsed once per
		//session. Empty for a console without a list, or if it fails to load.
		public static IReadOnlyList<CheatDbGame> LoadDatabase(ConsoleType console)
		{
			lock(_dbCache) {
				if(_dbCache.TryGetValue(console, out IReadOnlyList<CheatDbGame>? cached)) {
					return cached;
				}

				IReadOnlyList<CheatDbGame> games = Array.Empty<CheatDbGame>();
				if(CheatConsoleScope.HasCheatList(console)) {
					try {
						string? content = DependencyHelper.GetFileContent("CheatDb." + console.ToString() + ".json");
						if(content != null) {
							CheatDatabase? db = (CheatDatabase?)JsonSerializer.Deserialize(content, typeof(CheatDatabase), MesenCamelCaseSerializerContext.Default);
							games = (db?.Games ?? new List<CheatDbGameEntry>())
								.Select(g => new CheatDbGame(g.Name, g.Sha1, (g.Cheats ?? new List<CheatDbCheatEntry>()).Select(c => new CheatDbCode(c.Desc, c.Code)).ToList()))
								.ToList();
						}
					} catch {
						games = Array.Empty<CheatDbGame>();
					}
				}
				_dbCache[console] = games;
				return games;
			}
		}

		public static IReadOnlyList<StoredCheat> LoadStored()
		{
			return CheatCodes.LoadCheatCodes().Cheats.Select(c => new StoredCheat(c.Description, c.Type, c.Codes, c.Enabled)).ToList();
		}

		//Writes the list to the game's CheatCodes file and applies it, honouring
		//the classic window's "disable all cheats" switch.
		public static void SaveAndApply(IReadOnlyList<StoredCheat> stored)
		{
			CheatCodes codes = new() {
				Cheats = stored.Select(c => new CheatCode() { Description = c.Description, Type = c.Type, Codes = c.Codes, Enabled = c.Enabled }).ToList()
			};
			codes.Save();
			CheatCodes.ApplyCheats();
		}
	}
}
