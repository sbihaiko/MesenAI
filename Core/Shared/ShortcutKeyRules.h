#pragma once
#include "pch.h"
#include "Shared/SettingTypes.h"
#include "Shared/Interfaces/IKeyManager.h"
#include <functional>

//P.4: the pure half of ShortcutKeyHandler's "a keyboard is plugged into the
//console, so keyboard shortcuts are dead keys" rule. Extracted so it can be
//asserted directly by scripts/core_unit_tests.cpp without linking the handler
//(which owns a thread, Emulator, KeyManager and SystemActionManager) - same
//extraction pattern ADR-0142's wave-1 work used, and the one UI/Logic uses on
//the C# side. ShortcutKeyHandler::IsKeyPressed is the only production caller;
//it supplies the "is this host key down right now" probe.
namespace ShortcutKeyRules
{
	//True when every keyboard key must read as "not pressed" for `shortcut`.
	//Pause is always exempt (it is how the player pauses to reach the other
	//shortcuts) and ToggleOverlay is exempt too: in Player mode the overlay is
	//the primary way to pause, so it must stay reachable inside a keyboard
	//game. The UI ignores ToggleOverlay while UiMode == Advanced, so the
	//exemption has no effect outside Player.
	inline bool ShouldBlockKeyboardKeys(EmulatorShortcut shortcut, bool isKeyboardConnected, bool isPaused)
	{
		if(shortcut == EmulatorShortcut::Pause || shortcut == EmulatorShortcut::ToggleOverlay) {
			return false;
		}
		return isKeyboardConnected && !isPaused;
	}

	//The host key probe: keyCode -> is it down, exactly, for the keys that are
	//not pad buttons - a mouse button, a keyboard scancode. Pad buttons are
	//answered from `pressedKeys` instead (see IsShortcutPressed), because their
	//code carries the device and "is this code down" is the wrong question.
	typedef std::function<bool(uint16_t keyCode)> KeyDownProbe;

	//#800: a pad key code is <base> + device * 0x100 + button, so the device is in
	//the code and nowhere else. The pause-menu gesture is seeded from "Pad1
	//Select" + "Pad1 Start", and read literally that means device 0 - with two
	//pads connected, the one in the player's hand then has no way into the
	//overlay, which is the only surface that reaches the menus while a game runs.
	//What a binding names is the *button*; which pad is holding it is the code's
	//own 0x100 block (ADR-0256 Decision 5, "Qualquer controle").
	inline bool IsPadKey(uint16_t keyCode)
	{
		return keyCode >= IKeyManager::BaseGamepadIndex;
	}

	inline uint16_t PadButtonOf(uint16_t keyCode)
	{
		return (uint16_t)(keyCode & 0xFF);
	}

	inline uint16_t PadBlockOf(uint16_t keyCode)
	{
		return (uint16_t)(keyCode & ~0xFF);
	}

	//The pad families the running backend exposes, as their base indices: XInput
	//from IKeyManager::BaseGamepadIndex, and Windows' DirectInput joysticks from
	//BaseDirectInputIndex above it. The families number their buttons independently
	//and there is no mapping between the two tables to consult - DirectInput's own
	//names are axis directions ("Y2-") and But1..But128, with no semantic pad
	//button anywhere - so a button byte is only a button within its own family. A
	//binding is therefore answered inside the family it was written from and
	//nowhere else: without this, one joystick's Y2- and X2- axes (offsets 5 and 6)
	//satisfy the XInput Start+Back chord (suffixes 5 and 6), which is precisely the
	//coincidence that makes them look like the same buttons.
	//
	//The list is supplied by the caller rather than derived from the code, because
	//only the backend knows it. Windows is the one backend with two families; macOS
	//and Linux have a single one whose device index is unbounded, so their 17th pad
	//(device 16) sits at code 0x2000 - the same code as a Windows joystick's first
	//button, meaning something else entirely. A mask over the code cannot tell
	//those apart, and reading it as a second family would take Pad17..Pad20 away
	//from the chord.
	typedef vector<uint16_t> PadFamilies;

	//The one-family backend: macOS and Linux in production, and the shape a test
	//uses when the test is not about families.
	inline const PadFamilies& SinglePadFamily()
	{
		static const PadFamilies families = { (uint16_t)IKeyManager::BaseGamepadIndex };
		return families;
	}

	//The two-family backend: Windows' KeyManager, which reports XInput pads from
	//BaseGamepadIndex and DirectInput joysticks from BaseDirectInputIndex. It lives
	//here rather than next to GetPadFamilies so that the unit tests assert *this*
	//list instead of a second copy of it - the two numbers are the whole fix, and a
	//copy that drifted would keep the suite green while reopening #800.
	inline const PadFamilies& TwoPadFamilies()
	{
		static const PadFamilies families = {
			(uint16_t)IKeyManager::BaseGamepadIndex,
			(uint16_t)IKeyManager::BaseDirectInputIndex
		};
		return families;
	}

	//The family a pad key code belongs to: the highest family base at or below it,
	//or the lowest base when the code sits under all of them. With one family - and
	//however high the device index goes - that is always the same answer.
	inline uint16_t PadFamilyOf(uint16_t keyCode, const PadFamilies& families)
	{
		if(families.empty()) {
			return (uint16_t)IKeyManager::BaseGamepadIndex;
		}
		uint16_t lowest = families[0];
		uint16_t family = 0;
		bool found = false;
		for(uint16_t candidate : families) {
			if(candidate < lowest) {
				lowest = candidate;
			}
			if(candidate <= keyCode && (!found || candidate > family)) {
				family = candidate;
				found = true;
			}
		}
		return found ? family : lowest;
	}

	inline void AddPadBlock(vector<uint16_t>& blocks, uint16_t keyCode)
	{
		if(!IsPadKey(keyCode)) {
			return;
		}
		uint16_t block = PadBlockOf(keyCode);
		if(std::find(blocks.begin(), blocks.end(), block) == blocks.end()) {
			blocks.push_back(block);
		}
	}

	//Whether `blocks` already holds a pad from the family `keyCode` belongs to.
	inline bool HasPadFamily(const vector<uint16_t>& blocks, uint16_t keyCode, const PadFamilies& families)
	{
		uint16_t family = PadFamilyOf(keyCode, families);
		for(uint16_t block : blocks) {
			if(PadFamilyOf(block, families) == family) {
				return true;
			}
		}
		return false;
	}

	//The pads a combination may be resolved on: the block each of its own pad keys
	//was written in (so a one-pad setup still answers exactly the code the binding
	//holds), plus the block of every pad the player is holding that is in the same
	//family as one of those - a second pad of the same family answers it; a pad from
	//another family is a different button numbering and does not. A combination
	//with no pad key at all resolves through the host probe alone, as it always
	//did.
	inline vector<uint16_t> PadBlocksFor(const KeyCombination& comb, const vector<uint16_t>& pressedKeys,
		const PadFamilies& families)
	{
		vector<uint16_t> blocks;
		AddPadBlock(blocks, comb.Key1);
		AddPadBlock(blocks, comb.Key2);
		AddPadBlock(blocks, comb.Key3);
		for(uint16_t keyCode : pressedKeys) {
			if(IsPadKey(keyCode) && HasPadFamily(blocks, keyCode, families)) {
				AddPadBlock(blocks, keyCode);
			}
		}
		return blocks;
	}

	//The probe for one pad: a pad key asks that pad's own button, every other key
	//asks the host. For the block the binding was written in this is the code
	//exactly as written, which is what keeps a one-pad setup on the behaviour it
	//always had.
	//
	//A bound pad key from *another* family is not shifted onto this block: the
	//families number their buttons independently, so re-using one family's button
	//byte on another family's device is exactly the coincidence this rule exists to
	//reject. It falls through to the exact host lookup, which is what an unheld
	//button deserves. Without this, a binding written across two families - an
	//XInput button and a joystick button - was satisfied by one XInput pad whose
	//button byte happened to match the joystick's.
	inline KeyDownProbe ProbeOnPad(uint16_t block, const vector<uint16_t>& pressedKeys, KeyDownProbe isKeyDown,
		const PadFamilies& families)
	{
		return [block, pressedKeys, isKeyDown, families](uint16_t keyCode) {
			if(!IsPadKey(keyCode) || PadFamilyOf(keyCode, families) != PadFamilyOf(block, families)) {
				return isKeyDown(keyCode);
			}
			return std::find(pressedKeys.begin(), pressedKeys.end(), (uint16_t)(block + PadButtonOf(keyCode))) != pressedKeys.end();
		};
	}

	inline bool IsKeyPressed(uint16_t keyCode, bool mergeCtrlAltShift, bool blockKeyboardKeys, const KeyDownProbe& isKeyDown)
	{
		if(blockKeyboardKeys && keyCode < IKeyManager::BaseMouseButtonIndex) {
			return false;
		}

		if(keyCode >= 116 && keyCode <= 121 && mergeCtrlAltShift) {
			//Left/right ctrl/alt/shift
			//Return true if either the left or right key is pressed
			return isKeyDown(keyCode | 1) || isKeyDown(keyCode & ~0x01);
		}

		return isKeyDown(keyCode);
	}

	//`anyKeyDown` is the handler's "_pressedKeys is not empty" guard: with no
	//key down at all nothing can match.
	inline bool IsCombinationPressed(KeyCombination comb, bool blockKeyboardKeys, bool anyKeyDown, const KeyDownProbe& isKeyDown)
	{
		int keyCount = (comb.Key1 ? 1 : 0) + (comb.Key2 ? 1 : 0) + (comb.Key3 ? 1 : 0);

		if(keyCount == 0 || !anyKeyDown) {
			return false;
		}

		bool mergeCtrlAltShift = keyCount > 1;

		return IsKeyPressed(comb.Key1, mergeCtrlAltShift, blockKeyboardKeys, isKeyDown) &&
			(comb.Key2 == 0 || IsKeyPressed(comb.Key2, mergeCtrlAltShift, blockKeyboardKeys, isKeyDown)) &&
			(comb.Key3 == 0 || IsKeyPressed(comb.Key3, mergeCtrlAltShift, blockKeyboardKeys, isKeyDown));
	}

	//One combination, asked once per pad it could be held on - and, when it names
	//no pad key at all, asked once through the host probe, which is every
	//combination that existed before this rule did.
	inline bool IsCombinationPressedOnAnyPad(KeyCombination comb, bool blockKeyboardKeys, bool anyKeyDown,
		const KeyDownProbe& isKeyDown, const vector<uint16_t>& pressedKeys, const PadFamilies& families)
	{
		vector<uint16_t> blocks = PadBlocksFor(comb, pressedKeys, families);
		if(blocks.empty()) {
			return IsCombinationPressed(comb, blockKeyboardKeys, anyKeyDown, isKeyDown);
		}
		for(uint16_t block : blocks) {
			if(IsCombinationPressed(comb, blockKeyboardKeys, anyKeyDown, ProbeOnPad(block, pressedKeys, isKeyDown, families))) {
				return true;
			}
		}
		return false;
	}

	//The whole decision for one shortcut: a pressed superset shadows its
	//subset, otherwise the shortcut's own combination decides.
	//
	//`pressedKeys` is the host's currently-pressed key codes, and it is what says
	//which pads the player is holding. The combination is resolved once per such
	//pad, and every pad key in it has to come from the *same* one - so the chord
	//is "these buttons on a pad", not "these buttons somewhere".
	//
	//`families` is the backend's own pad families, and the production caller is
	//ShortcutKeyHandler::GetPadFamilies (which is where the platform's answer
	//lives - see the note on PadFamilies).
	inline bool IsShortcutPressed(EmulatorShortcut shortcut, KeyCombination comb, const vector<KeyCombination>& supersets,
		bool isKeyboardConnected, bool isPaused, bool anyKeyDown, const KeyDownProbe& isKeyDown,
		const vector<uint16_t>& pressedKeys, const PadFamilies& families)
	{
		bool blockKeyboardKeys = ShouldBlockKeyboardKeys(shortcut, isKeyboardConnected, isPaused);

		//Supersets first and across every pad: a pressed superset shadows its
		//subset whichever pad it was pressed on, which is the precedence this had
		//before there was more than one pad to ask.
		for(const KeyCombination& superset : supersets) {
			if(IsCombinationPressedOnAnyPad(superset, blockKeyboardKeys, anyKeyDown, isKeyDown, pressedKeys, families)) {
				//A superset is pressed, ignore this subset
				return false;
			}
		}

		//No supersets are pressed, check if all matching keys are pressed on some
		//one pad
		return IsCombinationPressedOnAnyPad(comb, blockKeyboardKeys, anyKeyDown, isKeyDown, pressedKeys, families);
	}
}
