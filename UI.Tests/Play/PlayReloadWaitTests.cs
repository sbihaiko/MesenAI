using Mesen.Logic;
using Xunit;

namespace Mesen.Tests.Play
{
	//#734 follow-up: a reload of the game on screen (power cycle, Reload, a
	//pack change, Remaster's Build & Show) is a wait like an open - the HD pack
	//decode sits inside it - so it shows the load card until the first picture
	//after the reload. This pins how a reload's wait starts, what it says and
	//every way it ends, so it can never spin forever.
	public class PlayReloadWaitTests
	{
		[Fact]
		public void A_reload_shows_the_card_only_in_Player_mode_with_the_game_on_screen()
		{
			Assert.True(PlayLoadWait.ShowsReloadFor(playerMode: true, gameOnScreen: true));
			Assert.False(PlayLoadWait.ShowsReloadFor(playerMode: true, gameOnScreen: false));
			Assert.False(PlayLoadWait.ShowsReloadFor(playerMode: false, gameOnScreen: true));
		}

		[Fact]
		public void The_reload_card_says_reloading()
		{
			Assert.Equal(("PlayLoadWaitReloading", "Castlevania"), PlayLoadWait.Text("Castlevania", PlayLoadWaitKind.Reload));
			Assert.Equal(("PlayLoadWaitReloadingGame", (string?)null), PlayLoadWait.Text("", PlayLoadWaitKind.Reload));
			Assert.Equal(("PlayLoadWaitOpening", "Castlevania"), PlayLoadWait.Text("Castlevania", PlayLoadWaitKind.Open));
		}

		[Fact]
		public void A_reload_waits_from_the_request_to_the_first_picture()
		{
			PlayLoadWait wait = new();
			int ticket = wait.BeginReload("Contra", openGeneration: 3);
			Assert.Equal(PlayLoadWaitKind.Reload, wait.Kind);
			Assert.Equal(PlayLoadWaitPhase.Opening, wait.Phase);
			Assert.Equal(ticket, wait.Ticket);

			Assert.True(wait.OnGameLoaded(loadedPaused: false, emulatorPaused: false));
			Assert.Equal(PlayLoadWaitPhase.WaitingForPicture, wait.Phase);
			for(int i = 1; i < PlayLoadWait.FramesUntilShown; i++) {
				Assert.False(wait.OnFrameDone());
			}
			Assert.True(wait.OnFrameDone());
			Assert.False(wait.IsActive);
		}

		//A power cycle of a paused game draws one frame and parks: nothing will
		//draw, so the card goes at once (the pause overlay is what is shown).
		[Fact]
		public void A_reload_of_a_paused_game_ends_at_GameLoaded()
		{
			PlayLoadWait wait = new();
			wait.BeginReload("Contra", 3);
			Assert.False(wait.OnGameLoaded(loadedPaused: false, emulatorPaused: true));
			Assert.False(wait.IsActive);
		}

		//An open keeps #734's rule: the emulator's pause flag of the game being
		//replaced says nothing about the new one.
		[Fact]
		public void An_open_ignores_the_previous_games_pause()
		{
			PlayLoadWait wait = new();
			wait.Begin("Contra", 3);
			Assert.True(wait.OnGameLoaded(loadedPaused: false, emulatorPaused: true));
			Assert.Equal(PlayLoadWaitPhase.WaitingForPicture, wait.Phase);
		}

		//The in-place swap (ReloadRomKeepingState) blocks until the state is
		//back: refused (movie, netplay) means no GameLoaded; paused means no
		//frames. Either way its return ends the wait.
		[Fact]
		public void The_reload_calls_return_ends_a_refused_or_paused_reload()
		{
			PlayLoadWait refused = new();
			int t1 = refused.BeginReload("Contra", 3);
			Assert.True(refused.OnReloadReturned(t1, emulatorPaused: false));
			Assert.False(refused.IsActive);

			PlayLoadWait paused = new();
			int t2 = paused.BeginReload("Contra", 3);
			paused.OnGameLoaded(false, false);
			Assert.True(paused.OnReloadReturned(t2, emulatorPaused: true));
			Assert.False(paused.IsActive);

			PlayLoadWait running = new();
			int t3 = running.BeginReload("Contra", 3);
			running.OnGameLoaded(false, false);
			Assert.False(running.OnReloadReturned(t3, emulatorPaused: false));
			Assert.Equal(PlayLoadWaitPhase.WaitingForPicture, running.Phase);
		}

		[Fact]
		public void A_stale_reload_return_does_not_end_a_newer_wait()
		{
			PlayLoadWait wait = new();
			int first = wait.BeginReload("Contra", 3);
			wait.BeginReload("Contra", 3);
			Assert.False(wait.OnReloadReturned(first, emulatorPaused: false));
			Assert.True(wait.IsActive);
		}

		//An open's load-call return is about opens only: a reload begun with the
		//same open generation is not ended by it.
		[Fact]
		public void An_opens_return_does_not_end_a_reload()
		{
			PlayLoadWait wait = new();
			wait.BeginReload("Contra", 3);
			Assert.False(wait.OnLoadReturned(3));
			Assert.True(wait.IsActive);
		}

		//A power cycle that fails (the file is gone) sends GameLoadFailed.
		[Fact]
		public void A_failed_reload_ends_the_wait()
		{
			PlayLoadWait wait = new();
			wait.BeginReload("Contra", 3);
			Assert.True(wait.OnLoadFailed());
			Assert.False(wait.IsActive);

			PlayLoadWait open = new();
			open.Begin("Contra", 3);
			Assert.False(open.OnLoadFailed());
			Assert.True(open.IsActive);
		}

		//The safety net: the wait it names ends whatever its phase; a newer one stays.
		[Fact]
		public void Expiry_ends_only_the_wait_it_names()
		{
			PlayLoadWait wait = new();
			int first = wait.BeginReload("Contra", 3);
			Assert.True(wait.Expire(first));
			Assert.False(wait.IsActive);

			int second = wait.BeginReload("Contra", 3);
			int third = wait.Begin("Metroid", 4);
			Assert.NotEqual(second, third);
			Assert.False(wait.Expire(second));
			Assert.True(wait.IsActive);
			Assert.Equal(PlayLoadWaitKind.Open, wait.Kind);
		}
	}
}
