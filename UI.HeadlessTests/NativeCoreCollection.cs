using Xunit;

namespace Mesen.HeadlessTests;

//Issue #432. The native MesenCore behind EmuApi/DebugApi/ConfigApi/InputApi is
//one process-global emulator: InitializeEmu, LoadRom, LoadStateFile and Stop act
//on the same instance whichever test calls them. xUnit v3 runs test classes
//(one collection each by default) in parallel, so two classes driving the core
//at once swapped each other's ROM under them - the test host crashed with exit
//134/139, or the F12.2 dispatcher scan wrote a copy table of another frame with
//no error.
//
//Every test class that reaches the native core joins this one collection.
//DisableParallelization = true makes xUnit run it on its own, after the
//parallel collections finish, so native tests never overlap each other nor a
//core-free test. Core-free wiring tests stay out of it and keep running in
//parallel. NativeCoreCollectionGuardTests fails when a class that reaches the
//core is missing the [Collection(NativeCoreCollection.Name)] attribute.
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class NativeCoreCollection
{
	public const string Name = "Native MesenCore (serial)";
}
