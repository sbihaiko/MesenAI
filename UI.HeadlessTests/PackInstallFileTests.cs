using System.IO;
using System.IO.Compression;
using System.Threading.Tasks;
using Mesen.ViewModels;
using Xunit;

namespace Mesen.HeadlessTests;

//#993 review: EnhancementPacksViewModel.InstallPackFile backs a pack dropped
//on the main window (an async void caller), so any failure must come back as
//a message ID - an exception escaping it crashes the app. Core-free.
public class PackInstallFileTests
{
	[Fact]
	public async Task A_failure_outside_io_comes_back_as_a_message_not_an_exception()
	{
		string folder = Path.Combine(Path.GetTempPath(), "mesen-993-" + System.Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(folder);
		try {
			string archive = Path.Combine(folder, "Pack.zip");
			using(ZipArchive zip = ZipFile.Open(archive, ZipArchiveMode.Create)) {
				zip.CreateEntry("pack.json");
			}
			//A NUL in the target path: File.Copy throws ArgumentException, not IOException.
			string error = await EnhancementPacksViewModel.InstallPackFile(archive, folder + "\0bad");
			Assert.Equal("InstallMepPackInvalidZipFile", error);
		} finally {
			Directory.Delete(folder, true);
		}
	}
}
