using Mesen.Logic;
using Xunit;

namespace Mesen.Tests.Play
{
	//#897: StateGrid's selection arithmetic, extracted to the host-free
	//GridSelection. These pin the classic 4 x 3 behaviour the extraction must
	//not change (step Down by a row, wrap Up/Left/Right at the edges), then the
	//one-row shape the Play home's tiles draw.
	public class GridSelectionTests
	{
		//A full slot page: 3 rows x 4 columns, 12 entries - the shape Advanced's
		//game-selection and the Save/Load slots draw.
		private const int Count = 12;
		private const int Columns = 4;
		private const int Rows = 3;

		[Theory]
		[InlineData(0, 4)]
		[InlineData(4, 8)]
		[InlineData(8, 0)]
		[InlineData(11, 3)]
		public void Down_steps_a_row_and_wraps_to_the_top(int from, int to)
		{
			Assert.Equal(to, GridSelection.Next(from, GridDirection.Down, Count, Columns, Rows));
		}

		[Theory]
		[InlineData(0, 8)]
		[InlineData(3, 11)]
		[InlineData(4, 0)]
		[InlineData(8, 4)]
		public void Up_steps_a_row_and_wraps_to_the_bottom(int from, int to)
		{
			Assert.Equal(to, GridSelection.Next(from, GridDirection.Up, Count, Columns, Rows));
		}

		[Theory]
		[InlineData(0, 11)]
		[InlineData(5, 4)]
		public void Left_wraps_from_the_first_to_the_last(int from, int to)
		{
			Assert.Equal(to, GridSelection.Next(from, GridDirection.Left, Count, Columns, Rows));
		}

		[Theory]
		[InlineData(0, 1)]
		[InlineData(11, 0)]
		public void Right_wraps_from_the_last_to_the_first(int from, int to)
		{
			Assert.Equal(to, GridSelection.Next(from, GridDirection.Right, Count, Columns, Rows));
		}

		//A short final row is clamped to the last entry, not read past it.
		[Theory]
		[InlineData(8, 0)]
		[InlineData(9, 1)]
		public void Down_from_a_short_last_row_wraps_within_range(int from, int to)
		{
			Assert.Equal(to, GridSelection.Next(from, GridDirection.Down, count: 10, columns: 4, rows: 3));
		}

		[Theory]
		[InlineData(0, 0)]
		[InlineData(3, 3)]
		public void SlotOf_reads_the_index_itself_when_in_range(int index, int slot)
		{
			Assert.Equal(slot, GridSelection.SlotOf(index, Count));
		}

		//#897: the Play home's row of tiles is one row whose columns follow the
		//window, so it can have fewer entries than columns (five columns and two
		//tiles on a wide window). Up there is the classic top-row wrap, which
		//answered a NEGATIVE index - nothing above a single row exists.
		[Fact]
		public void One_row_grid_Up_leaves_the_selection_where_it_is()
		{
			//Exact values, not InRange: the range alone no longer says much once
			//SlotOf clamps the answer, and the rule is "nothing above a single row
			//moved", so Up must answer the index it was handed.
			Assert.Equal(0, GridSelection.Next(0, GridDirection.Up, count: 2, columns: 5, rows: 1));
			Assert.Equal(1, GridSelection.Next(1, GridDirection.Up, count: 2, columns: 5, rows: 1));
		}

		//#897, the case that says the ROW count is the fact and "entries <= columns"
		//is not: the Play home's row of tiles takes up to five columns and PAGES the
		//rest, so eight tiles are still one row. Inferring the row from the counts
		//gets this shape wrong - eight entries over five columns reads as "there are
		//rows below me" - and Up then answers the classic wrap, a jump to entry 3.
		//Nothing is above a single row, paged or not, so Up leaves the selection
		//alone and Down is what reaches page two.
		[Fact]
		public void One_row_grid_Up_stays_put_even_when_the_row_pages()
		{
			Assert.Equal(0, GridSelection.Next(0, GridDirection.Up, count: 8, columns: 5, rows: 1));
			Assert.Equal(3, GridSelection.Next(3, GridDirection.Up, count: 8, columns: 5, rows: 1));
			Assert.Equal(5, GridSelection.Next(0, GridDirection.Down, count: 8, columns: 5, rows: 1));
		}

		//#897: the read must answer a real slot for any index, including a
		//negative one the host set - C#'s % keeps the sign, so the old
		//Entries[-3 % 2] was Entries[-1].
		[Fact]
		public void A_negative_selection_still_reads_a_real_slot()
		{
			int slot = GridSelection.SlotOf(-3, count: 2);
			Assert.InRange(slot, 0, 1);
		}

		//#897 audit: once Up could leave a negative index, every other move
		//carried it further - Right's (current + 1) % count stays negative for
		//current <= -2, Down's % keeps the sign, Left just decrements. Every
		//direction must answer in range whatever index it is handed.
		[Theory]
		[InlineData(GridDirection.Up)]
		[InlineData(GridDirection.Down)]
		[InlineData(GridDirection.Left)]
		[InlineData(GridDirection.Right)]
		public void Every_direction_stays_in_range_for_any_start(GridDirection direction)
		{
			foreach(int current in new[] { -7, -3, -1, Count, Count + 3 }) {
				Assert.InRange(GridSelection.Next(current, direction, Count, Columns, Rows), 0, Count - 1);
			}
		}
	}
}
