using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using Mesen.Interop;
using Xunit;

namespace Mesen.HeadlessTests;

//Issue #432: the guard that keeps native-core tests serial. It walks the IL of
//every test class in this assembly (its methods, its lambdas and async state
//machines, any helper in this assembly they call, and - since the #441 review -
//the app code in the UI assembly they call, up to AppCallDepth deep) and flags
//the class when that code reaches NativeCore or a type in UI/Interop that holds
//P/Invoke methods (EmuApi, DebugApi, ConfigApi, InputApi, ...). Every flagged class must
//carry [Collection(NativeCoreCollection.Name)], whose definition disables
//parallelization. A new native test class that forgets the attribute fails here
//on every run, core built or not - so on CI too, where the native tests
//themselves self-skip (ADR-0150 §3) and the race could never show.
//
//Pure reflection, no core call: this class stays in the parallel pool.
public class NativeCoreCollectionGuardTests
{
	[Fact]
	public void Every_test_class_that_reaches_the_native_core_runs_in_the_serial_collection()
	{
		List<Type> native = TestClasses().Where(ReachesNativeCore).ToList();

		//Keeps the scan honest: if it stopped seeing the core (a refactor of the
		//IL walk, a renamed marker), every class would pass vacuously.
		Assert.Contains(typeof(CopyAfterStateLoadTests), native);
		Assert.Contains(typeof(CopyAsMepSheetCellTests), native);

		string[] violations = Violations(TestClasses());
		Assert.True(violations.Length == 0,
			$"These test classes drive the process-global native core but are not in [Collection(NativeCoreCollection.Name)], " +
			$"so xUnit runs them in parallel with other core tests (#432):\n{string.Join("\n", violations)}");
	}

	//Review of #441: a test can reach the core only through app code - it
	//constructs MainWindow, whose constructor calls EmuApi.InitDll() - without
	//naming NativeCore or an Interop type itself. The fixtures below are never
	//run and carry no [Fact], so discovery ignores them; the guard is pointed at them.
	[Fact]
	public void A_class_that_reaches_the_core_only_through_app_code_is_flagged()
	{
		string violation = Assert.Single(Violations(new[] { typeof(ReachesCoreOnlyThroughAppCode) }));
		Assert.StartsWith($"{nameof(ReachesCoreOnlyThroughAppCode)} (collection: <none>)", violation);
		Assert.EndsWith("MainWindow..ctor -> EmuApi.InitDll", violation);
	}

	//[NativeCoreFree] covers only what the walk over-approximates - a path
	//through app code the test's arguments never take. It never excuses a class
	//that names the core itself, and it goes stale once the walk stops flagging.
	[Fact]
	public void NativeCoreFree_excuses_only_an_indirect_path()
	{
		Assert.Empty(Violations(new[] { typeof(ReachesCoreThroughAppCodeOnABranchItNeverTakes) }));
		Assert.Contains("does not cover a direct reference", Assert.Single(Violations(new[] { typeof(NamesTheCoreButClaimsToBeFree) })));
		Assert.Contains("drop the attribute", Assert.Single(Violations(new[] { typeof(CoreFreeAndMarkedSo) })));
	}

	private static class ReachesCoreOnlyThroughAppCode
	{
		public static object Open() => new Mesen.Windows.MainWindow();
	}

	[NativeCoreFree("fixture")]
	private static class ReachesCoreThroughAppCodeOnABranchItNeverTakes
	{
		public static object Open() => new Mesen.Windows.MainWindow();
	}

	[NativeCoreFree("fixture")]
	private static class NamesTheCoreButClaimsToBeFree
	{
		public static void Init() => EmuApi.InitDll();
	}

	[NativeCoreFree("fixture")]
	private static class CoreFreeAndMarkedSo
	{
		public static int Answer() => 42;
	}

	[Fact]
	public void The_serial_collection_disables_parallelization()
	{
		CustomAttributeData definition = typeof(NativeCoreCollection).GetCustomAttributesData()
			.Single(a => a.AttributeType == typeof(CollectionDefinitionAttribute));
		Assert.Equal(NativeCoreCollection.Name, definition.ConstructorArguments.Single().Value);
		CustomAttributeNamedArgument disable = definition.NamedArguments
			.SingleOrDefault(a => a.MemberName == nameof(CollectionDefinitionAttribute.DisableParallelization));
		Assert.True(disable.TypedValue.Value is true, "NativeCoreCollection must set DisableParallelization = true (#432).");
	}

	private static IEnumerable<Type> TestClasses()
	{
		return typeof(NativeCoreCollectionGuardTests).Assembly.GetTypes().Where(t =>
			//This class names the markers via typeof, which the walk would read as a core reference.
			t.IsClass && !t.IsNested && t != typeof(NativeCoreCollectionGuardTests) && t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)
				.Any(m => m.GetCustomAttributes<FactAttribute>(true).Any()));
	}

	private static string[] Violations(IEnumerable<Type> classes)
	{
		List<string> violations = new();
		foreach(Type type in classes) {
			(string Path, int AppDepth)? found = NativePath(type);
			bool serial = CollectionName(type) == NativeCoreCollection.Name;
			string? freeReason = type.GetCustomAttribute<NativeCoreFreeAttribute>()?.Reason;
			bool markedFree = !string.IsNullOrWhiteSpace(freeReason);
			if(found is not (string path, int appDepth)) {
				if(markedFree) {
					violations.Add($"{type.Name} is marked [NativeCoreFree] but the walk sees no core reference: drop the attribute");
				}
			} else if(serial && markedFree) {
				violations.Add($"{type.Name} is in the serial collection and also marked [NativeCoreFree]: pick one");
			} else if(!serial && !(markedFree && appDepth > 0)) {
				string excuse = markedFree ? " - [NativeCoreFree] does not cover a direct reference" : "";
				violations.Add($"{type.Name} (collection: {CollectionName(type) ?? "<none>"}){excuse}: {path}");
			}
		}
		return violations.OrderBy(v => v, StringComparer.Ordinal).ToArray();
	}

	private static string? CollectionName(Type type)
	{
		CustomAttributeData? attribute = type.GetCustomAttributesData().SingleOrDefault(a => a.AttributeType == typeof(CollectionAttribute));
		object? argument = attribute?.ConstructorArguments.SingleOrDefault().Value;
		return argument switch {
			string name => name,
			//[Collection(typeof(X))] names the collection after the definition type.
			Type definition => definition.GetCustomAttributesData()
				.SingleOrDefault(a => a.AttributeType == typeof(CollectionDefinitionAttribute))?
				.ConstructorArguments.FirstOrDefault().Value as string,
			_ => null
		};
	}

	private static readonly ConcurrentDictionary<Type, bool> MarkerCache = new();

	private static bool IsNativeMarker(Type? type)
	{
		if(type == null) {
			return false;
		}
		if(type == typeof(NativeCore)) {
			return true;
		}
		if(type.Assembly != typeof(EmuApi).Assembly) {
			return false;
		}
		return MarkerCache.GetOrAdd(type, static t => {
			for(Type? declaring = t; declaring != null; declaring = declaring.DeclaringType) {
				if(declaring.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.DeclaredOnly).Any(IsPInvoke)) {
					return true;
				}
			}
			return false;
		});
	}

	private static bool IsPInvoke(MethodBase method)
	{
		return method.Attributes.HasFlag(MethodAttributes.PinvokeImpl);
	}

	//How many calls deep the walk follows app code (the UI assembly, which holds
	//EmuApi & co.). Hops inside this test assembly are not counted. The deepest
	//chain found today is 4 (PlayerSettingsTabsTests: ConfigWindow -> ConfigViewModel
	//-> SelectTab -> AudioConfigViewModel -> ConfigApi); deeper walks mostly add
	//branches no test takes.
	private const int AppCallDepth = 6;

	private static bool ReachesNativeCore(Type testClass)
	{
		return NativePath(testClass) != null;
	}

	//Breadth-first walk from every method of the test class. Returns the call
	//chain to the first core reference and how many app-code calls deep it is
	//(0 = the test assembly names the core itself), or null. Follows direct calls only
	//(call/callvirt/newobj/ldftn operands, plus the static constructor of any app
	//type it touches); it does not guess virtual overrides or event handlers the
	//framework may later invoke, which would flag core-free classes such as
	//ControllerHighlightTests (KeyBindingButton.OnPropertyChanged can call
	//InputApi, but only for a property that test never sets). It is also blind to
	//which branch the test's arguments select; a class that reaches the core only
	//on such a branch declares so with [NativeCoreFree] (see Violations).
	private static (string Path, int AppDepth)? NativePath(Type testClass)
	{
		Module tests = testClass.Module;
		Module app = typeof(EmuApi).Module;
		Queue<(MethodBase Method, int Depth)> pending = new();
		Dictionary<MethodBase, MethodBase?> parent = new();
		foreach(MethodBase m in AllMethods(testClass)) {
			parent[m] = null;
			pending.Enqueue((m, 0));
		}
		while(pending.Count > 0) {
			(MethodBase method, int depth) = pending.Dequeue();
			foreach(MemberInfo member in ReferencedMembers(method)) {
				if(IsNativeMarker(member as Type ?? member.DeclaringType) || (member is MethodBase callee && IsPInvoke(callee))) {
					return (Chain(parent, method) + " -> " + Describe(member), depth);
				}
				foreach(MethodBase next in Callees(member, tests, app)) {
					int nextDepth = next.Module == app ? depth + 1 : depth;
					if(nextDepth <= AppCallDepth && parent.TryAdd(next, method)) {
						pending.Enqueue((next, nextDepth));
					}
				}
			}
		}
		return null;
	}

	private static IEnumerable<MethodBase> Callees(MemberInfo member, Module tests, Module app)
	{
		Type? type = member as Type ?? member.DeclaringType;
		if(type == null || type == typeof(NativeCore)) {
			yield break;
		}
		if(type.Module == app) {
			//Touching a type's static member (or constructing it) runs its static constructor.
			if(type.TypeInitializer is ConstructorInfo cctor) {
				yield return cctor;
			}
			//An async/iterator state machine of app code: its body is MoveNext.
			if(type.Name.Contains('<') && type.GetMethod("MoveNext", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance) is MethodInfo moveNext) {
				yield return moveNext;
			}
		}
		if(member is not MethodBase callee) {
			yield break;
		}
		if(callee.Module == app) {
			yield return callee;
		} else if(callee.Module == tests) {
			//A lambda closure or state machine of this assembly: its sibling methods run too.
			foreach(MethodBase m in type.Name.Contains('<') ? AllMethods(type) : new[] { callee }) {
				yield return m;
			}
		}
	}

	private static string Chain(Dictionary<MethodBase, MethodBase?> parent, MethodBase last)
	{
		List<string> names = new();
		for(MethodBase? m = last; m != null; m = parent[m]) {
			names.Add(Describe(m));
		}
		names.Reverse();
		return string.Join(" -> ", names);
	}

	private static string Describe(MemberInfo member)
	{
		return member is Type t ? t.Name : $"{member.DeclaringType?.Name}.{member.Name}";
	}

	private static IEnumerable<MethodBase> AllMethods(Type type)
	{
		const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
		IEnumerable<MethodBase> own = type.GetMethods(all).Cast<MethodBase>().Concat(type.GetConstructors(all));
		return own.Concat(type.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic).SelectMany(AllMethods));
	}

	private static readonly Dictionary<short, OpCode> OpCodesByValue = typeof(OpCodes)
		.GetFields(BindingFlags.Public | BindingFlags.Static)
		.Select(f => (OpCode)f.GetValue(null)!)
		.ToDictionary(o => o.Value);

	//Decodes the method body instruction by instruction and resolves every
	//member/type token operand (call, newobj, ldsfld, ldftn, ldtoken, ...).
	private static IEnumerable<MemberInfo> ReferencedMembers(MethodBase method)
	{
		byte[]? il = method.GetMethodBody()?.GetILAsByteArray();
		if(il == null) {
			yield break;
		}
		Type[]? typeArgs = method.DeclaringType?.IsGenericType == true ? method.DeclaringType.GetGenericArguments() : null;
		Type[]? methodArgs = method.IsGenericMethod ? method.GetGenericArguments() : null;
		int pos = 0;
		while(pos < il.Length) {
			short value = il[pos] == 0xFE ? (short)(0xFE00 | il[pos + 1]) : il[pos];
			pos += il[pos] == 0xFE ? 2 : 1;
			OpCode op = OpCodesByValue[value];
			int operandSize = op.OperandType switch {
				OperandType.InlineNone => 0,
				OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
				OperandType.InlineVar => 2,
				OperandType.InlineI8 or OperandType.InlineR => 8,
				OperandType.InlineSwitch => 4 + 4 * BitConverter.ToInt32(il, pos),
				_ => 4
			};
			if(op.OperandType is OperandType.InlineMethod or OperandType.InlineField or OperandType.InlineType or OperandType.InlineTok) {
				MemberInfo? member = null;
				try {
					member = method.Module.ResolveMember(BitConverter.ToInt32(il, pos), typeArgs, methodArgs);
				} catch(ArgumentException) {
					//A token that needs a generic context this walk does not carry; not a core reference.
				}
				if(member != null) {
					yield return member;
				}
			}
			pos += operandSize;
		}
	}
}
