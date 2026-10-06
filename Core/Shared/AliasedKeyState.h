#pragma once
#include <cstdint>
#include <cstring>
#include <vector>

//The published key state a backend derives from its host's key events.
//
//A backend's table between a host key code and a Mesen key code
//(Core/Shared/KeyDefinitions.h) is not always one-to-one, and the macOS table is
//neither total nor injective. Its 128 rows leave eight at 0, naming no key at all
//(host codes 63, 66, 68, 70, 77, 108, 112 and 127), and give one Mesen code to
//four pairs of host codes: 36 Return / 52 keypad Enter -> 6, 24 "=" / 81 keypad
//"=" -> 141, 42 backslash / 93 JIS yen -> 150 and 10 section / 94 JIS underscore
//-> 154. Keeping that in a bare `bool state[MesenCodes]` loses both properties:
//the unnamed host code lands in slot 0, which is not a key any host can press and
//which every host reader drops, and one release clears a code the other half of
//its pair still holds down (#902, #904).
//
//This header owns the translation, host-free, so scripts/core_unit_tests.cpp can
//pin the two rules without an NSEvent, an Emulator or the macOS arm
//(Core/Shared/GamepadButtonOrder.h and Core/Shared/ShortcutKeyRules.h are the
//same pattern). MacOSKeyManager is the caller: it fills the table from its own
//_keyCodeMap and routes every key-down/key-up through SetKeyState. The rules it
//has to answer, both of which a bare bool per Mesen code gets wrong:
//
//  * a host code the table cannot name publishes NOTHING - it is not slot 0, and
//    it is not in GetPressedKeys (#902);
//  * a Mesen code published by several host codes reads pressed while ANY of
//    them is down, and drops on the last release (#904).
class AliasedKeyState
{
public:
	//What the table answers for a host code it cannot name. It is also the code
	//the readers use for "no key here", so it is never a published key.
	static constexpr uint16_t NoKey = 0;

	//Host key codes the table can name. It is the size of the backend's own
	//table: macOS clamps anything at or past it to NoKey before looking the code
	//up (`[event keyCode] >= 128 ? 0 : _keyCodeMap[...]`), so a host code outside
	//it is unmapped by construction, not by a table entry.
	static constexpr uint32_t RawCodeCount = 128;

	//Mesen key codes the published state covers - the same 0x205 slots every
	//IKeyManager in this codebase keeps (Core/Shared/Interfaces/IKeyManager.h).
	static constexpr uint16_t KeyCodeCount = 0x205;

	AliasedKeyState() { Reset(); }

	void Reset()
	{
		memset(_map, 0, sizeof(_map));
		memset(_rawDown, 0, sizeof(_rawDown));
		memset(_downCount, 0, sizeof(_downCount));
	}

	//One row of the backend's table. A NoKey key code, a host code past the
	//table, or a Mesen code past the published range is left as "unmapped".
	void SetMapping(uint32_t rawCode, uint16_t keyCode)
	{
		if(rawCode < RawCodeCount && keyCode != NoKey && keyCode < KeyCodeCount) {
			_map[rawCode] = keyCode;
		}
	}

	//A key-down or key-up of one host key code. Answers whether the published
	//state moved - a host code the table cannot name never moves it.
	bool SetKeyState(uint32_t rawCode, bool down)
	{
		uint16_t keyCode = rawCode < RawCodeCount ? _map[rawCode] : NoKey;
		if(keyCode == NoKey) {
			//Unmapped: this is not a key the backend can name, so it is not
			//published under any code (least of all slot 0, which the host drops).
			return false;
		}

		if(down) {
			if(_rawDown[rawCode]) {
				//A repeat key-down of a key already held - macOS repeats while a
				//key is held - is the same one key to release later.
				return false;
			}
			_rawDown[rawCode] = true;
			return ++_downCount[keyCode] == 1;
		}

		if(!_rawDown[rawCode]) {
			return false;
		}
		_rawDown[rawCode] = false;
		return --_downCount[keyCode] == 0;
	}

	bool IsPressed(uint16_t keyCode) const
	{
		return keyCode != NoKey && keyCode < KeyCodeCount && _downCount[keyCode] > 0;
	}

	std::vector<uint16_t> GetPressedKeys() const
	{
		std::vector<uint16_t> pressedKeys;
		//From NoKey + 1: slot 0 is "no key", not a key a host can press (#902).
		for(uint16_t i = NoKey + 1; i < KeyCodeCount; i++) {
			if(_downCount[i] > 0) {
				pressedKeys.push_back(i);
			}
		}
		return pressedKeys;
	}

private:
	//Host code -> Mesen code, NoKey where the table names none.
	uint16_t _map[RawCodeCount];
	//Host codes currently down, so a repeat key-down is not a second key.
	bool _rawDown[RawCodeCount];
	//Published state, one count per Mesen code: the number of its host codes
	//that are down. A shared code is pressed while the count is not zero.
	uint8_t _downCount[KeyCodeCount];
};
