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

		//ADR-0254: a pause before the first picture leaves the game with no
		//picture until it draws one; a wait that reached its picture, or a new
		//open, says nothing was cut short.
		[Fact]
		public void A_pause_before_the_first_picture_cuts_it_short_until_the_game_draws_it()
		{
			PlayLoadWait wait = new();
			Assert.False(wait.PictureCutShort);
			wait.Begin("Contra", 7);
			wait.OnGameLoaded(false);
			Assert.False(wait.OnFrameDone());
			Assert.False(wait.PictureCutShort);

			Assert.True(wait.EndPictureWait());
			Assert.True(wait.PictureCutShort);

			//Resumed: the frames still count toward the picture, and the
			//third one since GameLoaded puts it out.
			for(int i = 2; i < PlayLoadWait.FramesUntilShown; i++) {
				Assert.False(wait.OnFrameDone());
				Assert.True(wait.PictureCutShort);
			}
			Assert.False(wait.OnFrameDone());
			Assert.False(wait.PictureCutShort);
			Assert.False(wait.IsActive);

			wait.Begin("Contra", 8);
			wait.OnGameLoaded(false);
			Assert.True(wait.EndPictureWait());
			wait.Begin("Castlevania", 9);
			Assert.False(wait.PictureCutShort);
		}

		[Fact]
		public void A_wait_that_reached_its_picture_was_not_cut_short()
		{
			PlayLoadWait wait = new();
			wait.Begin("Contra", 7);
			wait.OnGameLoaded(false);
			for(int i = 0; i < PlayLoadWait.FramesUntilShown; i++) {
				wait.OnFrameDone();
			}
			Assert.False(wait.EndPictureWait());
			Assert.False(wait.PictureCutShort);
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

		//ADR-0254: frames racing a new open while a cut-short wait counts them
		//never reach the new wait early - it still needs its own
		//FramesUntilShown frames after GameLoaded, and ends exactly once.
		[Fact]
		public void Frames_racing_a_new_open_do_not_count_toward_it()
		{
			for(int round = 0; round < 200; round++) {
				PlayLoadWait wait = new();
				wait.Begin("Contra", 1);
				wait.OnGameLoaded(false);
				Assert.True(wait.EndPictureWait());

				using var go = new System.Threading.Barrier(2);
				bool shown = false;
				var frames = new System.Threading.Thread(() => {
					go.SignalAndWait();
					for(int i = 0; i < PlayLoadWait.FramesUntilShown; i++) {
						shown |= wait.OnFrameDone();
					}
				});
				frames.Start();
				go.SignalAndWait();
				wait.Begin("Castlevania", 2);
				frames.Join();

				Assert.False(shown);

				Assert.False(wait.PictureCutShort);
				wait.OnGameLoaded(false);
				for(int i = 1; i < PlayLoadWait.FramesUntilShown; i++) {
					Assert.False(wait.OnFrameDone());
				}
				Assert.True(wait.OnFrameDone());
				Assert.False(wait.IsActive);
			}
		}
	}
}
