using Mesen.Interop;
using Mesen.Logic;
using System;
using Xunit;

namespace Mesen.Tests.Mep
{
	// Coverage for the host-free ADR-0244 decision (UI/Logic/PackChangePolicy.cs): which
	// pack changes keep the player's place, and what the HUD says after each answer the
	// native in-place reload can give.
	public class PackChangePolicyTests
	{
		[Theory]
		[InlineData(ConsoleType.Nes)]
		[InlineData(ConsoleType.Sms)]
		[InlineData(ConsoleType.Gameboy)]
		public void MeasuredConsole_WithNoMovieOrNetplay_ChangesInPlace(ConsoleType console)
		{
			Assert.Equal(new PackChangePlan(PackChangeRoute.InPlace, null), PackChangePolicy.Plan(console, false, false));
		}

		[Theory]
		[InlineData(ConsoleType.Snes)]
		[InlineData(ConsoleType.PcEngine)]
		[InlineData(ConsoleType.Gba)]
		[InlineData(ConsoleType.Ws)]
		public void UnmeasuredConsole_KeepsTheSilentRestart(ConsoleType console)
		{
			Assert.Equal(new PackChangePlan(PackChangeRoute.Restart, null), PackChangePolicy.Plan(console, false, false));
		}

		[Fact]
		public void Movie_KeepsTheRestart_WithTheRecordingReason()
		{
			Assert.Equal(new PackChangePlan(PackChangeRoute.Restart, PackChangePolicy.RecordingKey), PackChangePolicy.Plan(ConsoleType.Nes, true, false));
		}

		[Fact]
		public void Netplay_KeepsTheRestart_WithTheNetplayReason_EvenWhileRecording()
		{
			Assert.Equal(new PackChangePlan(PackChangeRoute.Restart, PackChangePolicy.NetplayKey), PackChangePolicy.Plan(ConsoleType.Nes, false, true));
			Assert.Equal(new PackChangePlan(PackChangeRoute.Restart, PackChangePolicy.NetplayKey), PackChangePolicy.Plan(ConsoleType.Nes, true, true));
		}

		[Fact]
		public void Restored_SaysRewindHistoryIsCleared()
		{
			Assert.Equal(new PackChangeOutcome(false, PackChangePolicy.ChangedKey), PackChangePolicy.Outcome(InPlaceReloadResult.Restored));
		}

		[Fact]
		public void FailedRestore_SaysTheGameRestarted_WithoutASecondRestart()
		{
			Assert.Equal(new PackChangeOutcome(false, PackChangePolicy.FallbackRestartKey), PackChangePolicy.Outcome(InPlaceReloadResult.Restarted));
		}

		[Fact]
		public void RomPatch_SaysThePackChangesTheGame_WithoutASecondRestart()
		{
			Assert.Equal(new PackChangeOutcome(false, PackChangePolicy.PatchRestartKey), PackChangePolicy.Outcome(InPlaceReloadResult.PatchRestarted));
		}

		[Fact]
		public void Refused_FallsBackToThePlainRestart()
		{
			Assert.Equal(new PackChangeOutcome(true, null), PackChangePolicy.Outcome(InPlaceReloadResult.Refused));
		}

		[Fact]
		public void Refused_RestartsTheGameTheChangeWasFor()
		{
			PackChangeOutcome refused = PackChangePolicy.Outcome(InPlaceReloadResult.Refused);
			Assert.True(PackChangePolicy.RestartsLoadedGame(refused, changeOpenGeneration: 3, currentOpenGeneration: 3, changeRomSha1: "AB12", currentRomSha1: "ab12"));
		}

		[Fact]
		public void Refused_AfterAnotherGameWasOpened_DoesNotRestartIt()
		{
			// #655: the core answers Refused at once (Emulator::ReloadRomKeepingState reads
			// IsRunning before it takes the lock), also while another game's load has the
			// old console torn down; the posted restart would then reload the new game.
			PackChangeOutcome refused = PackChangePolicy.Outcome(InPlaceReloadResult.Refused);
			Assert.False(PackChangePolicy.RestartsLoadedGame(refused, changeOpenGeneration: 3, currentOpenGeneration: 4, changeRomSha1: "AB12", currentRomSha1: "AB12"));
			Assert.False(PackChangePolicy.RestartsLoadedGame(refused, 3, 3, "AB12", currentRomSha1: "CD34"));
		}

		[Fact]
		public void Refused_WithNoGameLoaded_RestartsNothing()
		{
			PackChangeOutcome refused = PackChangePolicy.Outcome(InPlaceReloadResult.Refused);
			Assert.False(PackChangePolicy.RestartsLoadedGame(refused, 3, 3, "AB12", currentRomSha1: ""));
			Assert.False(PackChangePolicy.RestartsLoadedGame(refused, 3, 3, changeRomSha1: "", currentRomSha1: ""));
		}

		[Theory]
		[InlineData(InPlaceReloadResult.Restored)]
		[InlineData(InPlaceReloadResult.Restarted)]
		[InlineData(InPlaceReloadResult.PatchRestarted)]
		public void AnsweredInPlace_NeverRestartsAgain(InPlaceReloadResult result)
		{
			Assert.False(PackChangePolicy.RestartsLoadedGame(PackChangePolicy.Outcome(result), 3, 3, "AB12", "AB12"));
		}

		[Fact]
		public void ResultValues_MatchCoresInPlaceReloadResult()
		{
			// The export's ABI (Core/Shared/Emulator.h): the byte values are fixed.
			Assert.Equal(0, (byte)InPlaceReloadResult.Restored);
			Assert.Equal(1, (byte)InPlaceReloadResult.Restarted);
			Assert.Equal(2, (byte)InPlaceReloadResult.Refused);
			Assert.Equal(3, (byte)InPlaceReloadResult.PatchRestarted);
			Assert.Equal(4, Enum.GetValues<InPlaceReloadResult>().Length);
		}
	}
}
