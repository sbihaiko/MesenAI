using System;
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

//Review of #441. NativeCoreCollectionGuardTests follows calls into app code, but
//not which branch a test's arguments select there. A class whose only core path
//is such a branch - opening ConfigWindow on the Input tab, whose Audio tab would
//enumerate devices through ConfigApi - carries this attribute with the reason,
//instead of joining the serial collection. The guard rejects it on a class that
//names the core itself, and when the walk no longer sees a path at all. CI backs
//the claim: checks.yml runs this project with no core built, so a class marked
//here that did reach MesenCore would fail there with DllNotFoundException.
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class NativeCoreFreeAttribute(string reason) : Attribute
{
	public string Reason { get; } = reason;
}
