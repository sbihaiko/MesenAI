#pragma once
#include "pch.h"
#include "Shared/SettingTypes.h"
#include "Utilities/SimpleLock.h"
#include "Utilities/ISerializable.h"
#include <random>

class Emulator;

class EmuSettings final : public ISerializable
{
private:
	Emulator* _emu;
	std::mt19937 _mt;

	VideoConfig _video;
	AudioConfig _audio;
	InputConfig _input;
	EmulationConfig _emulation;
	PreferencesConfig _preferences;
	AudioPlayerConfig _audioPlayer;
	DebugConfig _debug;

	GameConfig _game;
	ShaderConfig _shader;

	SnesConfig _snes;
	GameboyConfig _gameboy;
	NesConfig _nes;
	PcEngineConfig _pce;
	SmsConfig _sms;
	CvConfig _cv;
	EnhancementPackConfig _enhancementPacks;
	GbaConfig _gba;
	WsConfig _ws;

	atomic<uint32_t> _flags;
	atomic<uint64_t> _debuggerFlags;
	//ADR-0246 §5 (P.13): Settings > Look's Hold to Compare. Display state, never
	//saved: while true the decoder skips the scale filter and the NTSC console
	//filter, and the renderer bypasses the shader chain without unloading it.
	atomic<bool> _lookCompare = false;

	string _audioDevice;
	string _soundFontPath;
	string _saveFolder;
	string _saveStateFolder;
	string _screenshotFolder;

	std::unordered_map<uint32_t, KeyCombination> _emulatorKeys[3];
	std::unordered_map<uint32_t, vector<KeyCombination>> _shortcutSupersets[3];
	//ADR-0255 slice 4: the player's press threshold per pad axis direction, keyed
	//by ShortcutKeyRules::PadDirectionOf. Guarded by _updateShortcutsLock, which
	//is also what makes a direction's threshold and the binding that names it
	//arrive together from one ApplyConfig().
	std::unordered_map<uint16_t, int32_t> _padAxisThresholds;

	SimpleLock _updateShortcutsLock;
	SimpleLock _shaderCfgLock;

	void ProcessString(string& str, const char** strPointer);

	void ClearShortcutKeys();
	void SetShortcutKey(EmulatorShortcut shortcut, KeyCombination keyCombination, int keySetIndex);

public:
	EmuSettings(Emulator* emu);

	void CopySettings(EmuSettings& src);

	void Serialize(Serializer& s) override;

	uint32_t GetVersion();
	string GetVersionString();

	void SetVideoConfig(VideoConfig& config);
	VideoConfig& GetVideoConfig();

	void SetShaderConfig(InteropShaderConfig& config);
	ShaderConfig GetShaderConfig();
	bool NeedsShaderUpdate(uint32_t version);

	void SetLookCompare(bool enabled) { _lookCompare = enabled; }
	bool IsLookCompare() { return _lookCompare; }

	void SetAudioConfig(AudioConfig& config);
	AudioConfig& GetAudioConfig();

	void SetInputConfig(InputConfig& config);
	InputConfig& GetInputConfig();

	void SetEmulationConfig(EmulationConfig& config);
	EmulationConfig& GetEmulationConfig();

	void SetSnesConfig(SnesConfig& config);
	SnesConfig& GetSnesConfig();

	void SetNesConfig(NesConfig& config);
	NesConfig& GetNesConfig();

	void SetGameboyConfig(GameboyConfig& config);
	GameboyConfig& GetGameboyConfig();

	void SetGbaConfig(GbaConfig& config);
	GbaConfig& GetGbaConfig();

	void SetPcEngineConfig(PcEngineConfig& config);
	PcEngineConfig& GetPcEngineConfig();

	void SetSmsConfig(SmsConfig& config);
	SmsConfig& GetSmsConfig();

	void SetEnhancementPackConfig(EnhancementPackConfig& config);
	EnhancementPackConfig& GetEnhancementPackConfig();

	void SetCvConfig(CvConfig& config);
	CvConfig& GetCvConfig();

	void SetWsConfig(WsConfig& config);
	WsConfig& GetWsConfig();

	void SetGameConfig(GameConfig& config);
	GameConfig& GetGameConfig();

	void SetPreferences(PreferencesConfig& config);
	PreferencesConfig& GetPreferences();

	void SetAudioPlayerConfig(AudioPlayerConfig& config);
	AudioPlayerConfig& GetAudioPlayerConfig();

	void SetDebugConfig(DebugConfig& config);
	DebugConfig& GetDebugConfig();

	void SetShortcutKeys(vector<ShortcutKeyInfo> shortcuts);
	KeyCombination GetShortcutKey(EmulatorShortcut shortcut, int keySetIndex);
	vector<KeyCombination> GetShortcutSupersets(EmulatorShortcut shortcut, int keySetIndex);

	//ADR-0255 slice 4: the axis directions a shortcut's spare binding names, with
	//the player's threshold each. Set alongside SetShortcutKeys from one
	//ApplyConfig(), and read by the platform's game controller on the input poll -
	//which is why it lives here: every backend already holds the Emulator and
	//already asks it for GetControllerDeadzoneRatio(), so this is the same read
	//path and not a second one.
	void SetPadAxisThresholds(vector<PadAxisThreshold> thresholds);
	//0 when the direction is not in the table, meaning the backend keeps its own
	//deadzone-derived magnitude (ShortcutKeyRules::AxisThresholdRatio).
	int32_t GetPadAxisThresholdUnits(uint16_t direction);

	OverscanDimensions GetOverscan();
	bool IsFastForward();
	uint32_t GetEmulationSpeed();
	double GetAspectRatio(ConsoleRegion region, FrameInfo baseFrameSize);

	void SetFlag(EmulationFlags flag);
	void SetFlagState(EmulationFlags flag, bool enabled);
	void ClearFlag(EmulationFlags flag);
	bool CheckFlag(EmulationFlags flag);

	void SetDebuggerFlag(DebuggerFlags flag, bool enabled);
	bool CheckDebuggerFlag(DebuggerFlags flags);

	bool HasRandomPowerOnState(ConsoleType consoleType);
	int GetRandomValue(int maxValue);
	bool GetRandomBool();
	void InitializeRam(RamState state, void* data, uint32_t length);

	bool IsInputEnabled();
	double GetControllerDeadzoneRatio();

	template<typename T>
	bool IsEqual(T& prevCfg, T& newCfg)
	{
		if(memcmp(&prevCfg, &newCfg, sizeof(T)) == 0) {
			return true;
		}
		memcpy(&prevCfg, &newCfg, sizeof(T));
		return false;
	}
};