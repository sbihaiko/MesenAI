using System.Collections.Generic;
using Mesen.Logic;
using Xunit;

namespace Mesen.Tests.Play
{
	//#1107: PadWalk.Judge is the pad-walk rule, pure over what the walk observed.
	//Each failure mode is handed a doctored observation so it is proven to fail
	//without a broken build; the live walk is UI.HeadlessTests/PlayPadWalkTests.
	public class PadWalkJudgeTests
	{
		private static PadWalkObservation Clean() => new(
			"Surface", false, new[] { "A", "B" }, new[] { "A", "B" }, true,
			new[] { ("A", (IReadOnlyList<PlayBarEntry>?)PlayBarDeclarations.PauseOverlay) },
			new HashSet<PlayAction> { PlayAction.Confirm, PlayAction.Back });

		[Fact]
		public void A_clean_observation_has_no_problems()
		{
			Assert.Empty(PadWalk.Judge(Clean()));
		}

		[Fact]
		public void An_unreachable_control_fails_the_walk()
		{
			PadWalkObservation o = Clean() with { Interactive = new[] { "A", "B", "Orphan" } };
			Assert.Contains(PadWalk.Judge(o), p => p.StartsWith("Surface: Orphan is not reachable from the pad"));
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
				BarByFocus = new[] { ("Continue", (IReadOnlyList<PlayBarEntry>?)PlayBarDeclarations.Home) },
			};
			Assert.Empty(PadWalk.Judge(o));
		}

		[Fact]
		public void A_bar_entry_the_surface_does_not_have_fails_the_walk()
		{
			//Library's bar names Search and the console row; this surface has neither.
			PadWalkObservation o = Clean() with {
				BarByFocus = new[] { ("A", (IReadOnlyList<PlayBarEntry>?)PlayBarDeclarations.Library) },
			};
			List<string> problems = PadWalk.Judge(o);
			Assert.Contains(problems, p => p.Contains("names Search") && p.Contains("no such action"));
			Assert.Contains(problems, p => p.Contains("names ConsoleFilter") && p.Contains("no such action"));
			Assert.DoesNotContain(problems, p => p.Contains("names Confirm"));
		}

		[Fact]
		public void A_favorite_entry_fails_when_no_cover_has_the_focus()
		{
			//The bar declares Favorite, but the walk found no cover under any focus,
			//so Available lacks it (PlayFavoriteCover.Declare adds it only on a cover).
			PadWalkObservation o = Clean() with {
				BarByFocus = new[] { ("A", (IReadOnlyList<PlayBarEntry>?)new[] { new PlayBarEntry(PlayAction.Favorite, "BarFavorite") }) },
			};
			Assert.Contains(PadWalk.Judge(o), p => p.Contains("names Favorite"));
		}

		[Fact]
		public void A_favorite_entry_passes_when_a_cover_has_the_focus()
		{
			PadWalkObservation o = Clean() with {
				BarByFocus = new[] { ("A", (IReadOnlyList<PlayBarEntry>?)new[] { new PlayBarEntry(PlayAction.Favorite, "BarFavorite") }) },
				Available = new HashSet<PlayAction> { PlayAction.Confirm, PlayAction.Back, PlayAction.Favorite },
			};
			Assert.Empty(PadWalk.Judge(o));
		}
	}
}
