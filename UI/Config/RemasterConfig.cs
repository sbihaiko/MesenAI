using CommunityToolkit.Mvvm.ComponentModel;

namespace Mesen.Config;

//G.3 (PRD Part B §13.4, W-R0b): what the user located for the Remaster jobs.
//Empty means "search the usual places" (UI/Logic/RemasterFeasibility.cs).
//Never marshaled to the core; never holds a credential.
public partial class RemasterConfig : BaseConfig<RemasterConfig>
{
	//A python3 the user picked with Locate Python…
	[ObservableProperty] public partial string PythonPath { get; set; } = "";
	//A folder holding mep_project.py (or its scripts/), picked with Locate Tools…
	[ObservableProperty] public partial string ToolsFolder { get; set; } = "";
}
