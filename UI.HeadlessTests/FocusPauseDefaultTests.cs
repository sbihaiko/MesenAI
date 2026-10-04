using Mesen.Config;
using Xunit;

namespace Mesen.HeadlessTests;

//ADR-0254 (accepted 2026-10-04): the second half of the user's decision -
//"Ligado por padrão no Play" - is a default value, and this is the only project
//that can see UI/Config (UI.Tests compiles UI/Logic only, ADR-0123). It needs
//no core and no window: the point is exactly that a configuration nobody has
//touched already pauses.
//
//One preference, one default: the pause is global, so Classic and Advanced get
//it too and keep the silent pause, since W-P4 is a Play surface. A config
//written before this keeps what it stored - nothing can tell "never chose" from
//"chose off".
public class FocusPauseDefaultTests
{
	[Fact]
	public void A_fresh_configuration_pauses_when_the_app_loses_focus()
	{
		Assert.True(new PreferencesConfig().PauseWhenInBackground);
	}

	//The menus-and-config pause is a different decision and stays opt-in: it is
	//the one that fires while the player is *using* the app.
	[Fact]
	public void The_menus_and_config_pause_stays_opt_in()
	{
		Assert.False(new PreferencesConfig().PauseWhenInMenusAndConfig);
	}
}
