//ADR-0248 section 3 (slice R.3): dumps what the Core's CheatManager decodes
//for a list of cheat codes, so scripts/test_cheat_decoder_parity.py can hold
//the Python decoder of the cheat-submitted workflow (scripts/cheat_decoder.py)
//to the Core's own logic rather than to a re-derivation of it.
//
//It links Core/Shared/CheatManager.cpp unmodified and calls its public
//GetConvertedCheat - the export the UI calls through EmuApi. The decoders read
//no emulator state; the members that do (SetCheats, ClearCheats,
//RefreshRamCheats, ApplyCheat) are never called here, so the Emulator and
//NotificationManager symbols they reference are satisfied by the inert stubs
//at the bottom of this file. The stubs abort if anything ever reaches them.
//
//Input (stdin): one `<CheatType number>\t<code>` per line.
//Output (stdout): one line per input line, tab-separated:
//  invalid
//  ok <type> <cpu> <address hex> <value hex> <compare or -1> <isRam 0|1> <isAbs 0|1> <memType>
//
//Built and run by scripts/test_cheat_decoder_parity.py; no make target.
#include "pch.h"
#include <cstdlib>
#include <cstring>
#include <iostream>
#include "Shared/CheatManager.h"
#include "Shared/Emulator.h"
#include "Shared/EmulatorLock.h"
#include "Shared/DebuggerRequest.h"
#include "Debugger/DebugBreakHelper.h"
#include "Debugger/Debugger.h"
#include "Shared/NotificationManager.h"
#include "Shared/MemoryType.h"

static const char* MemTypeName(MemoryType type)
{
	switch(type) {
		case MemoryType::GbCartRam: return "GbCartRam";
		case MemoryType::GbWorkRam: return "GbWorkRam";
		case MemoryType::SnesWorkRam: return "SnesWorkRam";
		default: return type == MemoryType {} ? "-" : "other";
	}
}

int main()
{
	CheatManager manager(nullptr);
	string line;
	while(std::getline(std::cin, line)) {
		if(!line.empty() && line.back() == '\r') {
			line.pop_back();
		}
		size_t tab = line.find('\t');
		if(tab == string::npos) {
			std::cout << "invalid\n";
			continue;
		}
		CheatCode input = {};
		input.Type = (CheatType)std::atoi(line.substr(0, tab).c_str());
		string code = line.substr(tab + 1);
		//The UI's InteropCheatCode copies at most 15 bytes into the 16-byte field.
		std::memcpy(input.Code, code.data(), std::min<size_t>(code.size(), 15));

		InternalCheatCode output = {};
		if(!manager.GetConvertedCheat(input, output)) {
			std::cout << "invalid\n";
			continue;
		}
		char buffer[160];
		std::snprintf(buffer, sizeof(buffer), "ok\t%d\t%d\t%X\t%X\t%d\t%d\t%d\t%s\n",
			(int)output.Type, (int)output.Cpu, output.Address, output.Value, (int)output.Compare,
			output.IsRamCode ? 1 : 0, output.IsAbsoluteAddress ? 1 : 0, MemTypeName(output.MemType));
		std::cout << buffer;
	}
	return 0;
}

//Inert link stubs: only the emulator-facing CheatManager members reference
//these, and the harness never calls those members.
[[noreturn]] static void Unreachable(const char* name)
{
	std::cerr << "cheat_decode_dump: unexpected call to " << name << "\n";
	std::abort();
}

EmulatorLock Emulator::AcquireLock(bool) { Unreachable("Emulator::AcquireLock"); }
IConsole* Emulator::GetConsoleUnsafe() { Unreachable("Emulator::GetConsoleUnsafe"); }
ConsoleMemoryInfo Emulator::GetMemory(MemoryType) { Unreachable("Emulator::GetMemory"); }
void NotificationManager::SendNotification(ConsoleNotificationType, void*) { Unreachable("NotificationManager::SendNotification"); }
//EmulatorLock is returned by value from AcquireLock, so CheatManager.cpp needs
//its destructor, and that destructor needs the two members it destroys.
EmulatorLock::~EmulatorLock() {}
DebuggerRequest::~DebuggerRequest() {}
void Debugger::BreakRequest(bool) { Unreachable("Debugger::BreakRequest"); }
