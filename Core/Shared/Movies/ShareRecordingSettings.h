#pragma once
#include "pch.h"
#include "Shared/SettingTypes.h"

//ADR-0205 section 2, slice R.1: the settings contract of the Record-and-share
//action, kept host-free (no Emulator, no EmuSettings.cpp) so it is unit tested
//in `make core-unit-tests`. Every function is a template over "a settings
//object exposing Get<Console>Config()/Set<Console>Config()", which EmuSettings
//is, so the real predicate and the test's fake share one implementation.
//
//The action's guarantee is that the archive carries no SaveState.mss. That is
//decided by one predicate - MovieRecorder::Record writes a state whenever the
//power-on state is random - so the action must drive that predicate to false
//for the loaded console, and must put the player's settings back afterwards.

//Which of the loaded console's settings make the power-on state random. This is
//the single definition: EmuSettings::HasRandomPowerOnState delegates here.
//Returns false for a console it has no case for (Ws); that is "unknown", not
//"deterministic", and ShareRecordingSettings::Apply refuses such a console
//instead of trusting the answer.
template<typename TSettings>
bool PowerOnStateIsRandom(TSettings& settings, ConsoleType consoleType)
{
	switch(consoleType) {
		case ConsoleType::Snes: return settings.GetSnesConfig().RamPowerOnState == RamState::Random || settings.GetSnesConfig().EnableRandomPowerOnState;
		case ConsoleType::Gameboy: return settings.GetGameboyConfig().RamPowerOnState == RamState::Random;
		case ConsoleType::Nes: return settings.GetNesConfig().RamPowerOnState == RamState::Random || settings.GetNesConfig().RandomizeCpuPpuAlignment || settings.GetNesConfig().RandomizeMapperPowerOnState;
		case ConsoleType::PcEngine: return settings.GetPcEngineConfig().RamPowerOnState == RamState::Random || settings.GetPcEngineConfig().EnableRandomPowerOnState;
		case ConsoleType::Sms: return settings.GetSmsConfig().RamPowerOnState == RamState::Random;
		case ConsoleType::Gba: return settings.GetGbaConfig().RamPowerOnState == RamState::Random;
		default: return false;
	}
}

//Exactly the fields PowerOnStateIsRandom reads, for every console. Restoring
//these and nothing else means a setting the player changes while recording is
//not rolled back, and a re-push of the player's configuration cannot make the
//restore wrong.
struct SharePowerOnState
{
	RamState Snes = RamState::Random;
	bool SnesRandomize = false;
	RamState Gameboy = RamState::Random;
	RamState Nes = RamState::Random;
	bool NesCpuPpuAlignment = false;
	bool NesMapper = false;
	RamState PcEngine = RamState::Random;
	bool PcEngineRandomize = false;
	RamState Sms = RamState::Random;
	RamState Gba = RamState::Random;
};

class ShareRecordingSettings
{
public:
	//A console the predicate knows how to make deterministic. The action
	//refuses to start for anything else rather than record an archive the
	//section 3 lint would reject.
	static bool IsSupported(ConsoleType consoleType)
	{
		switch(consoleType) {
			case ConsoleType::Snes:
			case ConsoleType::Gameboy:
			case ConsoleType::Nes:
			case ConsoleType::PcEngine:
			case ConsoleType::Sms:
			case ConsoleType::Gba:
				return true;
			default:
				return false;
		}
	}

	template<typename TSettings>
	static SharePowerOnState Capture(TSettings& settings)
	{
		SharePowerOnState s;
		s.Snes = settings.GetSnesConfig().RamPowerOnState;
		s.SnesRandomize = settings.GetSnesConfig().EnableRandomPowerOnState;
		s.Gameboy = settings.GetGameboyConfig().RamPowerOnState;
		s.Nes = settings.GetNesConfig().RamPowerOnState;
		s.NesCpuPpuAlignment = settings.GetNesConfig().RandomizeCpuPpuAlignment;
		s.NesMapper = settings.GetNesConfig().RandomizeMapperPowerOnState;
		s.PcEngine = settings.GetPcEngineConfig().RamPowerOnState;
		s.PcEngineRandomize = settings.GetPcEngineConfig().EnableRandomPowerOnState;
		s.Sms = settings.GetSmsConfig().RamPowerOnState;
		s.Gba = settings.GetGbaConfig().RamPowerOnState;
		return s;
	}

	//Drives PowerOnStateIsRandom(consoleType) to false. Returns false, touching
	//nothing, for a console it does not know. Idempotent.
	template<typename TSettings>
	static bool Apply(TSettings& settings, ConsoleType consoleType)
	{
		if(!IsSupported(consoleType)) {
			return false;
		}

		switch(consoleType) {
			case ConsoleType::Snes: {
				SnesConfig cfg = settings.GetSnesConfig();
				cfg.RamPowerOnState = RamState::AllZeros;
				cfg.EnableRandomPowerOnState = false;
				settings.SetSnesConfig(cfg);
				break;
			}
			case ConsoleType::Gameboy: {
				GameboyConfig cfg = settings.GetGameboyConfig();
				cfg.RamPowerOnState = RamState::AllZeros;
				settings.SetGameboyConfig(cfg);
				break;
			}
			case ConsoleType::Nes: {
				NesConfig cfg = settings.GetNesConfig();
				cfg.RamPowerOnState = RamState::AllZeros;
				cfg.RandomizeCpuPpuAlignment = false;
				cfg.RandomizeMapperPowerOnState = false;
				settings.SetNesConfig(cfg);
				break;
			}
			case ConsoleType::PcEngine: {
				PcEngineConfig cfg = settings.GetPcEngineConfig();
				cfg.RamPowerOnState = RamState::AllZeros;
				cfg.EnableRandomPowerOnState = false;
				settings.SetPcEngineConfig(cfg);
				break;
			}
			case ConsoleType::Sms: {
				SmsConfig cfg = settings.GetSmsConfig();
				cfg.RamPowerOnState = RamState::AllZeros;
				settings.SetSmsConfig(cfg);
				break;
			}
			case ConsoleType::Gba: {
				GbaConfig cfg = settings.GetGbaConfig();
				cfg.RamPowerOnState = RamState::AllZeros;
				settings.SetGbaConfig(cfg);
				break;
			}
			default:
				return false;
		}

		return !PowerOnStateIsRandom(settings, consoleType);
	}

	template<typename TSettings>
	static void Restore(TSettings& settings, const SharePowerOnState& s)
	{
		SnesConfig snes = settings.GetSnesConfig();
		snes.RamPowerOnState = s.Snes;
		snes.EnableRandomPowerOnState = s.SnesRandomize;
		settings.SetSnesConfig(snes);

		GameboyConfig gb = settings.GetGameboyConfig();
		gb.RamPowerOnState = s.Gameboy;
		settings.SetGameboyConfig(gb);

		NesConfig nes = settings.GetNesConfig();
		nes.RamPowerOnState = s.Nes;
		nes.RandomizeCpuPpuAlignment = s.NesCpuPpuAlignment;
		nes.RandomizeMapperPowerOnState = s.NesMapper;
		settings.SetNesConfig(nes);

		PcEngineConfig pce = settings.GetPcEngineConfig();
		pce.RamPowerOnState = s.PcEngine;
		pce.EnableRandomPowerOnState = s.PcEngineRandomize;
		settings.SetPcEngineConfig(pce);

		SmsConfig sms = settings.GetSmsConfig();
		sms.RamPowerOnState = s.Sms;
		settings.SetSmsConfig(sms);

		GbaConfig gba = settings.GetGbaConfig();
		gba.RamPowerOnState = s.Gba;
		settings.SetGbaConfig(gba);
	}
};
