using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using Mesen.Interop;
using Xunit;

namespace Mesen.HeadlessTests;

//Issue #432: the guard that keeps native-core tests serial. It walks the IL of
//every test class in this assembly (its methods, its lambdas and async state
//machines, and any helper in this assembly they call) and flags the class when
//that code reaches NativeCore or a type in UI/Interop that holds P/Invoke
//methods (EmuApi, DebugApi, ConfigApi, InputApi, ...). Every flagged class must
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

		string[] missing = native.Where(t => CollectionName(t) != NativeCoreCollection.Name)
			.Select(t => $"{t.Name} (collection: {CollectionName(t) ?? "<none>"})")
			.OrderBy(n => n, StringComparer.Ordinal).ToArray();
		Assert.True(missing.Length == 0,
			$"These test classes drive the process-global native core but are not in [Collection(NativeCoreCollection.Name)], " +
			$"so xUnit runs them in parallel with other core tests (#432): {string.Join(", ", missing)}");
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
		for(Type? t = type; t != null; t = t.DeclaringType) {
			if(t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.DeclaredOnly)
				.Any(m => m.Attributes.HasFlag(MethodAttributes.PinvokeImpl))) {
				return true;
			}
		}
		return false;
	}

	private static bool ReachesNativeCore(Type testClass)
	{
		Module module = testClass.Module;
		Queue<MethodBase> pending = new(AllMethods(testClass));
		HashSet<MethodBase> seen = new(pending);
		while(pending.Count > 0) {
			MethodBase method = pending.Dequeue();
			foreach(MemberInfo member in ReferencedMembers(method)) {
				if(IsNativeMarker(member as Type ?? member.DeclaringType)) {
					return true;
				}
				if(member is not MethodBase callee || callee.Module != module || callee.DeclaringType == typeof(NativeCore)) {
					continue;
				}
				//A lambda closure or state machine: its sibling methods run too.
				IEnumerable<MethodBase> next = callee.DeclaringType?.Name.Contains('<') == true ? AllMethods(callee.DeclaringType) : new[] { callee };
				foreach(MethodBase m in next.Where(seen.Add)) {
					pending.Enqueue(m);
				}
			}
		}
		return false;
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
