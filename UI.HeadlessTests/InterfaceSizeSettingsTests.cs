using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Mesen.Config;
using Mesen.Logic;
using Mesen.ViewModels;
using Xunit;

namespace Mesen.HeadlessTests;

//#1111 (spec #1102): the Interface size preference and the Display row's
//scope. The factor and step rules are host-free (UI.Tests/Play/InterfaceSizeTests);
//these need PreferencesConfig and the settings view model, which UI.Tests cannot
//compile. SetVideoConfig reaches the native core, so the class runs in its serial collection.
[Collection(NativeCoreCollection.Name)]
public class InterfaceSizeSettingsTests
{
	[Fact]
	public void A_fresh_install_is_standard()
	{
		Assert.Equal(InterfaceSize.Standard, new PreferencesConfig().InterfaceSize);
	}

	[Fact]
	public void The_choice_is_persisted_and_a_missing_key_reads_standard()
	{
		PreferencesConfig saved = new() { InterfaceSize = InterfaceSize.ExtraLarge };
		string json = JsonSerializer.Serialize(saved, typeof(PreferencesConfig), MesenSerializerContext.Default);
		PreferencesConfig? loaded = (PreferencesConfig?)JsonSerializer.Deserialize(json, typeof(PreferencesConfig), MesenSerializerContext.Default);
		Assert.Equal(InterfaceSize.ExtraLarge, loaded!.InterfaceSize);

		PreferencesConfig? old = (PreferencesConfig?)JsonSerializer.Deserialize("{}", typeof(PreferencesConfig), MesenSerializerContext.Default);
		Assert.Equal(InterfaceSize.Standard, old!.InterfaceSize);
	}

	//Scope: the row writes the preference and nothing of the picture. The
	//window's scale callback is never called and the Scale row keeps its list.
	[Fact]
	public void Choosing_a_size_leaves_the_picture_scale_and_the_scale_row_alone()
	{
		PreferencesConfig preferences = new();
		List<double> resized = new();
		PlayerWindowSettingsViewModel display = new(new VideoConfig(), false, 3, () => { }, resized.Add, preferences);
		double[] scalesBefore = display.Scales.Select(s => s.Value).ToArray();

		display.SelectedInterfaceSize = InterfaceSize.Large;

		Assert.Equal(InterfaceSize.Large, preferences.InterfaceSize);
		Assert.Empty(resized);
		Assert.Equal(scalesBefore, display.Scales.Select(s => s.Value).ToArray());
		Assert.Equal(3, display.SelectedScale!.Value);
	}

	[Fact]
	public void The_row_opens_on_the_saved_size()
	{
		PlayerWindowSettingsViewModel display = new(new VideoConfig(), false, 2, () => { }, _ => { }, new PreferencesConfig { InterfaceSize = InterfaceSize.ExtraLarge });
		Assert.Equal(InterfaceSize.ExtraLarge, display.SelectedInterfaceSize);
	}

	//A hand-edited settings file can hold a number no size has.
	[Fact]
	public void An_undefined_size_in_the_settings_falls_back_to_the_first_choice()
	{
		PlayerWindowSettingsViewModel display = new(new VideoConfig(), false, 2, () => { }, _ => { }, new PreferencesConfig { InterfaceSize = (InterfaceSize)3 });
		Assert.Equal(display.InterfaceSizes[0].Value, display.SelectedInterfaceSize);
	}
}
