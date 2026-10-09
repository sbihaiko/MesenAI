using System.Text.Json;
using Mesen.Config;
using Mesen.Utilities;
using Xunit;

namespace Mesen.HeadlessTests;

//#1112: the Menu tick is off until the player turns it on. InputConfig is a UI
//type (UI.Tests is host-free and cannot compile it), but these cases never touch
//the host, so they stay out of the native-core collection.
public class MenuTickDefaultTests
{
	[Fact]
	public void The_row_is_off_on_a_new_install()
	{
		Assert.False(new InputConfig().MenuTick);
	}

	[Fact]
	public void A_settings_file_without_a_MenuTick_key_loads_with_the_row_off()
	{
		Configuration loaded = (Configuration)JsonSerializer.Deserialize("{\"Input\":{\"ForceFeedbackIntensity\":5}}", typeof(Configuration), MesenSerializerContext.Default)!;
		Assert.Equal(5u, loaded.Input.ForceFeedbackIntensity);
		Assert.False(loaded.Input.MenuTick);
	}
}
