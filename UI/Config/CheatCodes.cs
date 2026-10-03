using Mesen.Interop;
using Mesen.Logic;
using Mesen.Utilities;
using Mesen.ViewModels;
using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace Mesen.Config
{
	public class CheatCodes
	{
		private static string FilePath { get { return Path.Combine(ConfigManager.CheatFolder, EmuApi.GetRomInfo().GetRomName() + ".json"); } }

		public List<CheatCode> Cheats { get; set; } = new List<CheatCode>();

		public static CheatCodes LoadCheatCodes()
		{
			return Deserialize(CheatCodes.FilePath);
		}

		private static CheatCodes Deserialize(string path)
		{
			CheatCodes cheats = new CheatCodes();

			if(File.Exists(path)) {
				try {
					cheats = (CheatCodes?)JsonSerializer.Deserialize(File.ReadAllText(path), typeof(CheatCodes), MesenSerializerContext.Default) ?? new CheatCodes();
				} catch { }
			}

			return cheats;
		}

		public void Save()
		{
			try {
				if(Cheats.Count > 0) {
					FileHelper.WriteAllText(CheatCodes.FilePath, JsonSerializer.Serialize(this, typeof(CheatCodes), MesenSerializerContext.Default));
				} else {
					if(File.Exists(CheatCodes.FilePath)) {
						File.Delete(CheatCodes.FilePath);
					}
				}
			} catch {
			}
		}

		//ADR-0184 §1 / ADR-0245 §3: while a Remaster recording runs, only RAM
		//codes reach the core, whichever window turned a code on (the classic
		//cheat window included); the others are held back and come back when the
		//recording ends. HeldForRecording lists them, for the recording strip.
		public static bool RecordingArt { get; private set; }
		public static IReadOnlyList<StoredCheat> HeldForRecording { get; private set; } = Array.Empty<StoredCheat>();
		public static event Action? HeldForRecordingChanged;

		public static void SetRecordingArt(bool recordingArt)
		{
			RecordingArt = recordingArt;
			ApplyCheats();
		}

		private static void SetHeld(IReadOnlyList<StoredCheat> held)
		{
			bool changed = !held.SequenceEqual(HeldForRecording);
			HeldForRecording = held;
			if(changed) {
				HeldForRecordingChanged?.Invoke();
			}
		}

		//Every code off, the ones held back for a recording too (#706: the
		//classic window's Disable All left them to come back after it).
		public static void ClearCheats()
		{
			SetHeld(Array.Empty<StoredCheat>());
			EmuApi.ClearCheats();
		}

		public static void ApplyCheats()
		{
			if(ConfigManager.Config.Cheats.DisableAllCheats) {
				ClearCheats();
			} else {
				CheatCodes.ApplyCheats(LoadCheatCodes().Cheats);
			}
		}

		public static void ApplyCheats(IEnumerable<CheatCode> cheats)
		{
			cheats = cheats.ToList();
			//Play's passive automatic recording yields to a code that changes the
			//game (ADR-0245 amendment 2026-10-03); a user-started Remaster recording
			//(RecordingArt) holds it back below instead.
			if(!RecordingArt && EmuApi.IsMepBootstrapping() &&
				CheatRecordingRule.PausesPassiveBootstrap(cheats.Select(c => new StoredCheat(c.Description, c.Type, c.Codes, c.Enabled)), RecordingArt, true)) {
				EmuApi.StopMepRecording();
			}
			List<InteropCheatCode> encodedCheats = new List<InteropCheatCode>();
			List<StoredCheat> held = new();
			foreach(CheatCode cheat in cheats) {
				if(!cheat.Enabled) {
					continue;
				}
				if(RecordingArt && !CheatRecordingRule.IsRamCode(cheat.Type, cheat.Codes)) {
					held.Add(new StoredCheat(cheat.Description, cheat.Type, cheat.Codes, true));
					continue;
				}
				encodedCheats.AddRange(cheat.ToInteropCheats());
			}
			SetHeld(held);

			EmuApi.SetCheats(encodedCheats.ToArray(), (UInt32)encodedCheats.Count);
		}
	}

	public partial class CheatCode : ViewModelBase
	{
		[ObservableProperty] public partial string Description { get; set; } = "";
		[ObservableProperty] public partial CheatType Type { get; set; }
		[ObservableProperty] public partial bool Enabled { get; set; } = true;
		[ObservableProperty] public partial string Codes { get; set; } = "";

		public List<InteropCheatCode> ToInteropCheats()
		{
			List<InteropCheatCode> encodedCheats = new List<InteropCheatCode>();
			foreach(string code in Codes.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)) {
				encodedCheats.Add(new InteropCheatCode(Type, code));
			}
			return encodedCheats;
		}

		public CheatCode Clone()
		{
			return new() {
				Description = Description,
				Type = Type,
				Enabled = Enabled,
				Codes = Codes,
			};
		}

		public void CopyFrom(CheatCode copy)
		{
			Description = copy.Description;
			Type = copy.Type;
			Enabled = copy.Enabled;
			Codes = String.Join(Environment.NewLine, copy.Codes.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
		}
	}
}
