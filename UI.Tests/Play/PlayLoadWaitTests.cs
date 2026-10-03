using System.Threading.Tasks;
using Mesen.Logic;
using Xunit;

namespace Mesen.Tests.Play
{
	//#734: every wait the player can see has an animation. In Play the open of a
	//game is a wait from the click to the game's first picture; this pins when
	//it starts, what it says and every way it ends.
	public class PlayLoadWaitTests
	{
		[Fact]
		public void Only_Player_modes_Play_workspace_shows_the_load_card()
		{
			Assert.True(PlayLoadWait.ShowsFor(playerMode: true, playWorkspace: true));
			Assert.False(PlayLoadWait.ShowsFor(playerMode: true, playWorkspace: false));
			Assert.False(PlayLoadWait.ShowsFor(playerMode: false, playWorkspace: true));
		}

		[Fact]
		public void Nothing_waits_before_an_open()
		{
			PlayLoadWait wait = new();
			Assert.False(wait.IsActive);
			Assert.Equal(PlayLoadWaitPhase.Idle, wait.Phase);
			Assert.False(wait.OnFrameDone());
			Assert.False(wait.OnGameLoaded(loadedPaused: false));
			Assert.False(wait.EndPictureWait());
		}

		[Fact]
		public void The_card_names_the_game_and_has_a_sentence_without_one()
		{
			Assert.Equal(("PlayLoadWaitOpening", "Castlevania"), PlayLoadWait.Text("Castlevania"));
			Assert.Equal(("PlayLoadWaitOpeningGame", (string?)null), PlayLoadWait.Text("  "));
		}

		[Fact]
		public void An_open_waits_until_the_first_picture_is_shown()
		{
			PlayLoadWait wait = new();
			wait.Begin("Castlevania", openGeneration: 4);
			Assert.True(wait.IsActive);
			Assert.Equal(PlayLoadWaitPhase.Opening, wait.Phase);
			Assert.Equal("Castlevania", wait.GameName);

			//A frame before GameLoaded belongs to the previous game.
			Assert.False(wait.OnFrameDone());

			//GameLoaded: the home (and the card) stay, the native picture stays hidden.
			Assert.True(wait.OnGameLoaded(loadedPaused: false));
			Assert.Equal(PlayLoadWaitPhase.WaitingForPicture, wait.Phase);

			//Frame N is decoded once frame N+2 is emulated (VideoDecoder spins on
			//the previous frame), so the first picture is out at the third.
			for(int i = 1; i < PlayLoadWait.FramesUntilShown; i++) {
				Assert.False(wait.OnFrameDone());
				Assert.True(wait.IsActive);
			}
			Assert.True(wait.OnFrameDone());
			Assert.False(wait.IsActive);
			Assert.False(wait.OnFrameDone());
		}

		[Fact]
		public void A_game_that_loads_paused_ends_the_wait_at_once()
		{
			PlayLoadWait wait = new();
			wait.Begin("Castlevania", 1);
			Assert.False(wait.OnGameLoaded(loadedPaused: true));
			Assert.False(wait.IsActive);
		}

		[Fact]
		public void A_failed_open_ends_the_wait()
		{
			PlayLoadWait wait = new();
			wait.Begin("Contra", 2);
			Assert.True(wait.OnLoadReturned(2));
			Assert.False(wait.IsActive);
		}

		//The load call returns after GameLoaded: a game that opened is not a failure.
		[Fact]
		public void A_load_that_returns_after_GameLoaded_keeps_waiting_for_the_picture()
		{
			PlayLoadWait wait = new();
			wait.Begin("Contra", 2);
			wait.OnGameLoaded(false);
			Assert.False(wait.OnLoadReturned(2));
			Assert.Equal(PlayLoadWaitPhase.WaitingForPicture, wait.Phase);
		}

		//#674's rule: an earlier open's return must not end a newer open's wait.
		[Fact]
		public void An_earlier_opens_return_does_not_end_a_newer_wait()
		{
			PlayLoadWait wait = new();
			wait.Begin("Contra", 2);
			wait.Begin("Metroid", 3);
			Assert.False(wait.OnLoadReturned(2));
			Assert.True(wait.IsActive);
			Assert.Equal("Metroid", wait.GameName);
		}

		//A pause, a stop or the timeout (a game that never draws) ends the
		//picture wait; only that phase, since a game switch stops the old game
		//while the new one is still opening.
		[Fact]
		public void Pause_stop_or_timeout_end_only_the_picture_wait()
		{
			PlayLoadWait wait = new();
			wait.Begin("Contra", 5);
			Assert.False(wait.EndPictureWait());
			Assert.Equal(PlayLoadWaitPhase.Opening, wait.Phase);

			wait.OnGameLoaded(false);
			Assert.False(wait.EndPictureWait(openGeneration: 4));
			Assert.True(wait.IsActive);
			Assert.True(wait.EndPictureWait(openGeneration: 5));
			Assert.False(wait.IsActive);

			wait.Begin("Contra", 6);
			wait.OnGameLoaded(false);
			Assert.True(wait.EndPictureWait());
			Assert.False(wait.IsActive);
		}

		[Fact]
		public void The_timeout_outlasts_a_stalled_audio_device()
		{
			//#733: the audio device stalled ~10 s; the card must not give up first.
			Assert.True(PlayLoadWait.PictureTimeout.TotalSeconds >= 20);
		}

		//PpuFrameDone arrives on the emulation thread: exactly one caller wins.
		[Fact]
		public void Concurrent_frames_end_the_wait_exactly_once()
		{
			PlayLoadWait wait = new();
			wait.Begin("Contra", 1);
			wait.OnGameLoaded(false);
			int ended = 0;
			Parallel.For(0, 1000, _ => {
				if(wait.OnFrameDone()) {
					System.Threading.Interlocked.Increment(ref ended);
				}
			});
			Assert.Equal(1, ended);
			Assert.False(wait.IsActive);
		}
	}
}
