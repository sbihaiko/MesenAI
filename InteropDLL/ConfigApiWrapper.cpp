#include "Common.h"
#include "Core/Shared/Emulator.h"
#include "Core/Shared/Interfaces/IAudioDevice.h"
#include "Core/Shared/EmuSettings.h"
#include "Core/Shared/SettingTypes.h"
#include "Utilities/StringUtilities.h"
#include "Utilities/Video/LibrashaderUtilities.h"

extern unique_ptr<Emulator>& _emu;

extern "C"
{
	DllExport void __stdcall SetVideoConfig(VideoConfig config)
	{
		_emu->GetSettings()->SetVideoConfig(config);
	}

	DllExport void __stdcall SetShaderConfig(InteropShaderConfig config)
	{
		_emu->GetSettings()->SetShaderConfig(config);
	}

	DllExport void __stdcall SetAudioConfig(AudioConfig config)
	{
		_emu->GetSettings()->SetAudioConfig(config);
	}

	DllExport void __stdcall SetInputConfig(InputConfig config)
	{
		_emu->GetSettings()->SetInputConfig(config);
	}

	DllExport void __stdcall SetEmulationConfig(EmulationConfig config)
	{
		_emu->GetSettings()->SetEmulationConfig(config);
	}

	DllExport void __stdcall SetGameboyConfig(GameboyConfig config)
	{
		_emu->GetSettings()->SetGameboyConfig(config);
	}

	DllExport void __stdcall SetGbaConfig(GbaConfig config)
	{
		_emu->GetSettings()->SetGbaConfig(config);
	}

	DllExport void __stdcall SetPcEngineConfig(PcEngineConfig config)
	{
		_emu->GetSettings()->SetPcEngineConfig(config);
	}

	DllExport void __stdcall SetNesConfig(NesConfig config)
	{
		_emu->GetSettings()->SetNesConfig(config);
	}

	DllExport void __stdcall SetSnesConfig(SnesConfig config)
	{
		_emu->GetSettings()->SetSnesConfig(config);
	}

	DllExport void __stdcall SetSmsConfig(SmsConfig config)
	{
		_emu->GetSettings()->SetSmsConfig(config);
	}

	DllExport void __stdcall SetEnhancementPackConfig(EnhancementPackConfig config)
	{
		_emu->GetSettings()->SetEnhancementPackConfig(config);
	}

	DllExport void __stdcall SetWsConfig(WsConfig config)
	{
		_emu->GetSettings()->SetWsConfig(config);
	}

	DllExport void __stdcall SetGameConfig(GameConfig config)
	{
		_emu->GetSettings()->SetGameConfig(config);
	}

	DllExport void __stdcall SetPreferences(PreferencesConfig config)
	{
		_emu->GetSettings()->SetPreferences(config);
	}

	DllExport void __stdcall SetAudioPlayerConfig(AudioPlayerConfig config)
	{
		_emu->GetSettings()->SetAudioPlayerConfig(config);
	}

	DllExport void __stdcall SetDebugConfig(DebugConfig config)
	{
		_emu->GetSettings()->SetDebugConfig(config);
	}

	DllExport void __stdcall SetShortcutKeys(ShortcutKeyInfo shortcuts[], uint32_t count)
	{
		vector<ShortcutKeyInfo> shortcutList(shortcuts, shortcuts + count);
		_emu->GetSettings()->SetShortcutKeys(shortcutList);
	}

	//ADR-0255 slice 4: what the core ended up holding for one shortcut in one key
	//set. SetShortcutKeys is the only writer and the engine is the only reader, so
	//without this a binding the engine dropped was invisible from the GUI side -
	//and a shortcut whose two key combinations were already filled had its pad
	//slot dropped exactly that way, silently, while the sheet showed it bound.
	//The Play Controller sheet's own test reads this back to prove the third
	//binding survives beside the other two.
	DllExport KeyCombination __stdcall GetShortcutKey(EmulatorShortcut shortcut, int keySetIndex)
	{
		return _emu->GetSettings()->GetShortcutKey(shortcut, keySetIndex);
	}

	//ADR-0255 slice 4: the player's axis thresholds, pushed with the shortcut
	//list so the binding that names a direction and its threshold land in one
	//config apply. The platform game controllers read it off the settings object
	//they already hold.
	DllExport void __stdcall SetPadAxisThresholds(PadAxisThreshold thresholds[], uint32_t count)
	{
		vector<PadAxisThreshold> thresholdList(thresholds, thresholds + count);
		_emu->GetSettings()->SetPadAxisThresholds(thresholdList);
	}

	DllExport NesConfig __stdcall GetNesConfig()
	{
		return _emu->GetSettings()->GetNesConfig();
	}

	DllExport void __stdcall GetAudioDevices(char* outDeviceList, uint32_t maxLength)
	{
		shared_ptr<IAudioDevice> soundManager = _emu->GetSoundManager();
		StringUtilities::CopyToBuffer(soundManager ? soundManager->GetAvailableDevices() : "", outDeviceList, maxLength);
	}

	DllExport void __stdcall SetEmulationFlag(EmulationFlags flag, bool enabled)
	{
		_emu->GetSettings()->SetFlagState(flag, enabled);
	}

	DllExport void __stdcall SetDebuggerFlag(DebuggerFlags flag, bool enabled)
	{
		_emu->GetSettings()->SetDebuggerFlag(flag, enabled);
	}

	DllExport bool __stdcall CheckShaderSupport()
	{
		return LibrashaderUtilities::CheckShaderSupport();
	}

	DllExport uint32_t __stdcall GetShaderParams(const char* shaderFile, ShaderParamDefinition* params)
	{
		if(params) {
			vector<ShaderParamDefinition> paramList = LibrashaderUtilities::GetShaderParams(shaderFile);
			std::copy(paramList.begin(), paramList.end(), params);
			return (uint32_t)paramList.size();
		} else {
			return LibrashaderUtilities::GetShaderParamCount(shaderFile);
		}
	}
}