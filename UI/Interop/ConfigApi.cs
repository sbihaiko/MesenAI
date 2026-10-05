using Mesen.Config;
using Mesen.Config.Shortcuts;
using Mesen.Logic;
using Mesen.Utilities;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace Mesen.Interop
{
	public class ConfigApi
	{
		private const string DllPath = EmuApi.DllName;

		[DllImport(DllPath)] public static extern void SetVideoConfig(InteropVideoConfig config);
		[DllImport(DllPath)] public static extern void SetAudioConfig(InteropAudioConfig config);
		[DllImport(DllPath)] public static extern void SetInputConfig(InteropInputConfig config);
		[DllImport(DllPath)] public static extern void SetEmulationConfig(InteropEmulationConfig config);

		[DllImport(DllPath)] public static extern void SetGameboyConfig(InteropGameboyConfig config);
		[DllImport(DllPath)] public static extern void SetGbaConfig(InteropGbaConfig config);
		[DllImport(DllPath)] public static extern void SetNesConfig(InteropNesConfig config);

		[DllImport(DllPath)] public static extern void SetSmsConfig(InteropSmsConfig config);
		[DllImport(DllPath)] public static extern void SetEnhancementPackConfig(InteropEnhancementPackConfig config);

		[DllImport(DllPath)] public static extern void SetGameConfig(InteropGameConfig config);

		[DllImport(DllPath)] public static extern void SetPreferences(InteropPreferencesConfig config);
		[DllImport(DllPath)] public static extern void SetAudioPlayerConfig(InteropAudioPlayerConfig config);
		[DllImport(DllPath)] public static extern void SetShortcutKeys([MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 1)] InteropShortcutKeyInfo[] shortcuts, UInt32 count);

		//ADR-0255 slice 4: what the core holds for one shortcut in one key set.
		//SetShortcutKeys is the only writer and the engine is the only reader, so
		//a binding the engine dropped was invisible from here - which is how a
		//shortcut with both key combinations already filled lost its pad slot
		//while the sheet showed it bound. The Sheet's own test reads it back.
		[DllImport(DllPath)] public static extern InteropKeyCombination GetShortcutKey(EmulatorShortcut shortcut, Int32 keySetIndex);

		//ADR-0255 slice 4: pushed right after the shortcut list, in the same
		//ApplyConfig(), so the direction a spare binding names and the threshold
		//that governs it reach the core together.
		[DllImport(DllPath)] public static extern void SetPadAxisThresholds([MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 1)] InteropPadAxisThreshold[] thresholds, UInt32 count);

		[DllImport(DllPath)] public static extern void SetDebugConfig(InteropDebugConfig config);

		[DllImport(DllPath)] public static extern void SetEmulationFlag(EmulationFlags flag, bool enabled);
		[DllImport(DllPath)] public static extern void SetDebuggerFlag(DebuggerFlags flag, bool enabled);

		[DllImport(DllPath)] public static extern InteropNesConfig GetNesConfig();

		[DllImport(DllPath, EntryPoint = "GetAudioDevices")] private static extern void GetAudioDevicesWrapper(IntPtr outDeviceList, Int32 maxSize);
		public unsafe static List<string> GetAudioDevices()
		{
			return Utf8Utilities.CallStringApi(GetAudioDevicesWrapper).Split(new String[1] { "||" }, StringSplitOptions.RemoveEmptyEntries).ToList();
		}

		[DllImport(DllPath)] public static extern void SetShaderConfig(InteropShaderConfig config);

		[DllImport(DllPath, EntryPoint = "CheckShaderSupport")][return: MarshalAs(UnmanagedType.I1)] public static extern bool CheckShaderSupportWrapper();
		private static bool? _shadersSupported = null;
		private static ShaderAvailability _shaderAvailability = ShaderAvailability.NotInBuild;

		//ADR-0246 §6: why shaders cannot run, for Settings › Look's reason line.
		//Decided once at startup, like CheckShaderSupport.
		public static ShaderAvailability GetShaderAvailability()
		{
			CheckShaderSupport();
			return _shaderAvailability;
		}

		public static bool CheckShaderSupport()
		{
			if(_shadersSupported == null) {
				bool coreSupport = CheckShaderSupportWrapper();
				//ADR-0237: on macOS only the Metal renderer applies shaders.
				bool usesSoftware = RendererPolicy.UsesSoftwareRenderer(ConfigManager.Config.Video.UseSoftwareRenderer, OperatingSystem.IsMacOS());
				_shaderAvailability = LookLayers.ResolveShaderAvailability(coreSupport, OperatingSystem.IsMacOS(), usesSoftware);
				_shadersSupported = RendererPolicy.ShaderGroupAvailable(coreSupport, OperatingSystem.IsMacOS(), usesSoftware);
				if(coreSupport == false) {
					EmuApi.WriteLogEntry("[librashader] Could not load librashader (missing file, or wrong version)");
				}
			}
			return _shadersSupported.Value;
		}

		[DllImport(DllPath)] private static extern UInt32 GetShaderParams(string shaderFile, IntPtr shaderParams);
		public static unsafe InteropShaderParam[] GetShaderParams(string shaderFile)
		{
			UInt32 paramCount = ConfigApi.GetShaderParams(shaderFile, IntPtr.Zero);
			if(paramCount == 0) {
				return Array.Empty<InteropShaderParam>();
			}

			InteropShaderParam[] shaderParams = new InteropShaderParam[paramCount];
			fixed(InteropShaderParam* ptr = shaderParams) {
				ConfigApi.GetShaderParams(shaderFile, (IntPtr)ptr);
			}
			return shaderParams;
		}
	}

	public enum EmulationFlags : UInt32
	{
		Turbo = 0x01,
		Rewind = 0x02,
		MaximumSpeed = 0x04,
		InBackground = 0x08,
		ConsoleMode = 0x10,
		TestMode = 0x20,
		OutputToStdout = 0x40
	}

	public enum DebuggerFlags : UInt32
	{
		SnesDebuggerEnabled = (1 << 0),
		SpcDebuggerEnabled = (1 << 1),
		Sa1DebuggerEnabled = (1 << 2),
		GsuDebuggerEnabled = (1 << 3),
		NecDspDebuggerEnabled = (1 << 4),
		Cx4DebuggerEnabled = (1 << 5),
		St018DebuggerEnabled = (1 << 6),
		GbDebuggerEnabled = (1 << 7),
		NesDebuggerEnabled = (1 << 8),
		PceDebuggerEnabled = (1 << 9),
		SmsDebuggerEnabled = (1 << 10),
		GbaDebuggerEnabled = (1 << 11),
		WsDebuggerEnabled = (1 << 12),
	}

	public struct InteropShortcutKeyInfo
	{
		public EmulatorShortcut Shortcut;
		public InteropKeyCombination KeyCombination;

		public InteropShortcutKeyInfo(EmulatorShortcut key, InteropKeyCombination keyCombination)
		{
			Shortcut = key;
			KeyCombination = keyCombination;
		}
	}

	public struct InteropKeyCombination
	{
		public UInt32 Key1;
		public UInt32 Key2;
		public UInt32 Key3;
	}

	//ADR-0255 slice 4: one stick direction's press threshold, in the core's own
	//field order (Core/Shared/SettingTypes.h's PadAxisThreshold). Direction is
	//PadAxisAction.DirectionKey - the family base plus the button byte; the units
	//are PadAxisAction.ThresholdUnits of the binding's effective percent.
	//Sequential, like every other struct crossing this boundary (InteropShaderParam
	//spells it out); the two fields are 2+4 bytes, so the layout is the same
	//either way and the attribute is what says so.
	[StructLayout(LayoutKind.Sequential)]
	public struct InteropPadAxisThreshold
	{
		public UInt16 Direction;
		public Int32 ThresholdUnits;
	}

	public struct InteropShaderConfig
	{
		public UInt32 ConfigVersion;
		[MarshalAs(UnmanagedType.LPStr)] public string ShaderFile;
		public IntPtr Params;
		public UInt32 ParamCount;
	}

	[StructLayout(LayoutKind.Sequential)]
	public unsafe struct InteropShaderParamValue
	{
		public fixed byte Name[200];
		public double Value;

		public string GetName()
		{
			fixed(byte* ptr = Name) {
				return Utf8Utilities.PtrToStringUtf8(ptr, 200);
			}
		}
	}

	[StructLayout(LayoutKind.Sequential)]
	public unsafe struct InteropShaderParam
	{
		public fixed byte Name[200];
		public fixed byte Description[200];

		public double Min;
		public double Max;
		public double Initial;
		public double Step;

		public string GetName()
		{
			fixed(byte* ptr = Name) {
				return Utf8Utilities.PtrToStringUtf8(ptr, 200);
			}
		}

		public string GetDescription()
		{
			fixed(byte* ptr = Description) {
				return Utf8Utilities.PtrToStringUtf8(ptr, 200);
			}
		}
	}
}
