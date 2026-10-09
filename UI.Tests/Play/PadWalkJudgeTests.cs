using System.Collections.Generic;
using Mesen.Logic;
using Xunit;

namespace Mesen.Tests.Play
{
	//#1107: PadWalk.Judge is the pad-walk rule, pure over what the walk observed.
	//Each failure mode is handed a doctored observation so it is proven to fail
	//without a broken build; the live walk is UI.HeadlessTests/PlayPadWalkTests.
	//#1154: reachability is asked by LABEL, over this side of the wall - the rule
	//lived in the headless suite, which CI skips, and the labels are what makes it
	//answerable at all (a rebuilt page is not a pad gap).
	public class PadWalkJudgeTests
	{
		private static readonly PadWalkControl A = new("A");
		private static readonly PadWalkControl B = new("B");

		private static PadWalkObservation Clean() => new(
			"Surface", false, new[] { A, B }, new[] { A, B }, true,
			new[] { ("A", (IReadOnlyList<PlayBarEntry>?)PlayBarDeclarations.PauseOverlay, false) },
			new HashSet<PlayAction> { PlayAction.Confirm, PlayAction.Back });

		[Fact]
		public void A_clean_observation_has_no_problems()
		{
			Assert.Empty(PadWalk.Judge(Clean()));
		}

		[Fact]
		public void An_unreachable_control_fails_the_walk()
		{
			PadWalkObservation o = Clean() with { Interactive = new[] { A, B, new PadWalkControl("Orphan") } };
			Assert.Contains(PadWalk.Judge(o), p => p.StartsWith("Surface: Orphan is not reachable from the pad"));
		}

		//#1154: the rule is a LABEL-set compare and not an identity one, and this
		//is the case that made it so. The walk lists a control per INSTANCE, and a
		//surface that rebuilds its page under the pad (the Settings strip replaces
		//the page a press moves away from) hands it the same control again as a new
		//instance - which an identity judgement reports as a pad gap while the pad
		//in fact landed on that control. What a surface shows is a set of control
		//kinds, the names a player would point at.
		[Fact]
		public void A_second_copy_of_a_reached_control_is_not_a_pad_gap()
		{
			PadWalkObservation o = Clean() with { Interactive = new[] { A, B, new PadWalkControl("A") } };
			Assert.Empty(PadWalk.Judge(o));
		}

		[Fact]
		public void A_focus_that_lands_outside_the_surface_fails_the_walk()
		{
			PadWalkObservation o = Clean() with { FocusOutside = new[] { "MainMenu" } };
			Assert.Contains(PadWalk.Judge(o), p => p == "Surface: the pad moved the focus outside the surface to MainMenu");
		}

		[Fact]
		public void A_back_that_does_not_leave_fails_the_walk()
		{
			PadWalkObservation o = Clean() with { BackLeft = false };
			List<string> problems = PadWalk.Judge(o);
			Assert.Contains("Surface: Back does not leave the surface", problems);
			Assert.Contains("Surface: the action bar names Back but B does not leave", problems);
		}

		[Fact]
		public void A_root_surface_without_a_back_may_keep_B_inert()
		{
			PadWalkObservation o = Clean() with {
				IsRoot = true, BackLeft = false,
				BarByFocus = new[] { ("Continue", (IReadOnlyList<PlayBarEntry>?)PlayBarDeclarations.Home, false) },
			};
			Assert.Empty(PadWalk.Judge(o));
		}

		[Fact]
		public void A_bar_entry_the_surface_does_not_have_fails_the_walk()
		{
			//Library's bar names Search and the console row; this surface has neither.
			PadWalkObservation o = Clean() with {
				BarByFocus = new[] { ("A", (IReadOnlyList<PlayBarEntry>?)PlayBarDeclarations.Library, false) },
			};
			List<string> problems = PadWalk.Judge(o);
			Assert.Contains(problems, p => p.Contains("names Search") && p.Contains("no such action"));
			Assert.Contains(problems, p => p.Contains("names ConsoleFilter") && p.Contains("no such action"));
			Assert.DoesNotContain(problems, p => p.Contains("names Confirm"));
		}

		[Fact]
		public void A_favorite_entry_fails_when_no_cover_has_the_focus()
		{
			//The bar declares Favorite, but no cover had this focus
			//(PlayFavoriteCover.Declare adds it only on a cover).
			PadWalkObservation o = Clean() with {
				BarByFocus = new[] { ("A", (IReadOnlyList<PlayBarEntry>?)new[] { new PlayBarEntry(PlayAction.Favorite, "BarFavorite") }, false) },
			};
			Assert.Contains(PadWalk.Judge(o), p => p.Contains("names Favorite"));
		}

		[Fact]
		public void A_favorite_entry_passes_when_a_cover_has_the_focus()
		{
			PadWalkObservation o = Clean() with {
				BarByFocus = new[] { ("A", (IReadOnlyList<PlayBarEntry>?)new[] { new PlayBarEntry(PlayAction.Favorite, "BarFavorite") }, true) },
			};
			Assert.Empty(PadWalk.Judge(o));
		}

		[Fact]
		public void A_favorite_declared_on_a_non_cover_focus_fails_even_when_another_focus_is_a_cover()
		{
			IReadOnlyList<PlayBarEntry>? favorite = new[] { new PlayBarEntry(PlayAction.Favorite, "BarFavorite") };
			PadWalkObservation o = Clean() with {
				BarByFocus = new (string, IReadOnlyList<PlayBarEntry>?, bool)[] { ("Cover", favorite, true), ("Search", favorite, false) },
			};
			List<string> problems = PadWalk.Judge(o);
			Assert.Contains(problems, p => p.Contains("with Search focused") && p.Contains("names Favorite"));
			Assert.DoesNotContain(problems, p => p.Contains("with Cover focused"));
		}
	}
}
