using Mesen.Logic;
using Mesen.ViewModels;
using Xunit;

namespace Mesen.HeadlessTests;

//#1038 review: UI.Tests reads the table that UI.Tests.csproj embeds into the
//test assembly, so nothing there proves the *app* ships it. A LogicalName or a
//path wrong in UI/UI.csproj would make LoadEmbedded() return null by design
//(falling back to file names in the library), and every host-free test would
//stay green. This is the one place that opens the resource from the UI
//assembly - the wiring UI.Tests is structurally unable to assert.
//
//Deliberately host-free: no MainWindow, no EmuApi, no core. typeof(...).Assembly
//only picks the app assembly to read from, so this class stays in the parallel
//pool and needs no [Collection(NativeCoreCollection)] (#432).
public class NoIntroTableEmbeddingTests
{
	private const int ExpectedMinimumRows = 17000;

	[Fact]
	public void The_app_assembly_embeds_the_no_intro_name_table()
	{
		NoIntroNameTable? table = NoIntroNameTable.LoadEmbedded(typeof(ShareWorkspaceViewModel).Assembly);

		Assert.NotNull(table);
		Assert.True(table!.Count > ExpectedMinimumRows,
			$"UI.dll embeds a table of {table.Count} rows; the generated artifact holds 17867. " +
			$"A truncated or missing resource would leave the library on file names silently.");
	}
}
