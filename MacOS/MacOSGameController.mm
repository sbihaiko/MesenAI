#import <Foundation/Foundation.h>
#import <Cocoa/Cocoa.h>
#import <GameController/GameController.h>
#import <CoreHaptics/CoreHaptics.h>

#include <iostream>
#include <stdint.h>
#include <optional>

#include "MacOSGameController.h"
//The MacOS SDK defines a global function 'Debugger', colliding with Mesen's Debugger class
//Redefine it temporarily so the headers don't cause compilation errors due to this
#define Debugger MesenDebugger
#include "Shared/MessageManager.h"
#include "Shared/Emulator.h"
#include "Shared/EmuSettings.h"
#include "Shared/ShortcutKeyRules.h"
#include "Shared/Interfaces/IKeyManager.h"
#undef Debugger

//ADR-0255 slice 4: the magnitude one thumbstick direction is compared against.
//`hostRatio` is this backend's own expression (the deadzone ratio times the 0.4
//of full travel the stick directions always used) and stands unchanged unless a
//shortcut's spare binding names the direction, in which case the player's own
//threshold governs it - ShortcutKeyRules::AxisThresholdRatio is the rule, and 0
//from the table means "no binding names this direction", i.e. behave as before.
//The sign convention stays here, where it has always been: only the magnitude
//the axis is compared against is replaced.
static double AxisThresholdForDirection(Emulator* emu, int direction, double hostRatio)
{
	int32_t units = emu->GetSettings()->GetPadAxisThresholdUnits(
		ShortcutKeyRules::PadDirectionOf((uint16_t)IKeyManager::BaseGamepadIndex, (uint16_t)direction));
	return ShortcutKeyRules::AxisThresholdRatio(units, hostRatio);
}

//---------------------------------------------------------------------------
//W-P15 (#912): a pad that exposes no extended gamepad.
//
//The GameController framework gives a controller one of three profiles -
//extended, micro (the Siri Remote and the one-button pads) or the older basic
//one - and this backend used to keep only the extended one: a micro/basic pad
//was dropped by MacOSKeyManager::AddController (so it never got a slot) or,
//one layer down, was kept with a nil handler, and every key it had was lost.
//The unknown-pad pill fires on a pad's first press, so a pad that sends no key
//never reaches it and can never be set up.
//
//A profile's elements drive the same button bits every other pad on this host
//drives - MacOSKeyManager's own `buttonNames` order: A 0, B 1, X 2, Y 3, L1 4,
//R1 5, Start 6, Select 7, Up 8, Down 9, Left 10, Right 11, L2 12, R2 13, L3
//14, R3 15. The tables below are that mapping as data, one per profile a pad
//can expose, so the bits can be read back off disk against that same order
//(UI.Tests/Play/MacOSPadProfileTests). The extended profile's own handler is
//unchanged: it still drives all sixteen bits and the four stick directions.
struct PadProfileKey
{
	//The GameController selector that returns the element.
	const char* element;
	//The button bit it drives, or PadDpad for a direction pad (bits 8..11).
	int bit;
};

//A direction pad's own element: it drives four bits, not one.
static const int PadDpad = -1;

static const PadProfileKey kMicroGamepadKeys[] = {
	{ "buttonA", 0 },
	{ "buttonX", 2 },
	{ "buttonMenu", 6 },
	{ "dpad", PadDpad },
};

static const PadProfileKey kBasicGamepadKeys[] = {
	{ "buttonA", 0 },
	{ "buttonB", 1 },
	{ "buttonX", 2 },
	{ "buttonY", 3 },
	{ "leftShoulder", 4 },
	{ "rightShoulder", 5 },
	{ "dpad", PadDpad },
};

//Lands the element a profile just reported on the bit its own table names.
static void HandleProfileElement(id profile, GCControllerElement* element, const PadProfileKey* keys, size_t count, bool* buttonState)
{
	for(size_t i = 0; i < count; i++) {
		SEL selector = NSSelectorFromString([NSString stringWithUTF8String:keys[i].element]);
		if(![profile respondsToSelector:selector] || (GCControllerElement*)[profile performSelector:selector] != element) {
			continue;
		}
		if(keys[i].bit == PadDpad) {
			GCControllerDirectionPad* dpad = (GCControllerDirectionPad*)element;
			buttonState[8] = [[dpad up] isPressed];
			buttonState[9] = [[dpad down] isPressed];
			buttonState[10] = [[dpad left] isPressed];
			buttonState[11] = [[dpad right] isPressed];
		} else {
			buttonState[keys[i].bit] = [((GCControllerButtonInput*)element) isPressed];
		}
	}
}

MacOSGameController::MacOSGameController(Emulator* emu, GCController* controller)
{
	_emu = emu;
	_controller = [controller retain];
	_extended = [[_controller extendedGamepad] retain];
	_micro = [[_controller microGamepad] retain];
#pragma clang diagnostic push
#pragma clang diagnostic ignored "-Wdeprecated-declarations"
	_basic = [[_controller gamepad] retain];
#pragma clang diagnostic pop

	if(_extended != nil) {
	[_extended setValueChangedHandler:^ void (GCExtendedGamepad* input, GCControllerElement* element) {
		if([input buttonA] == element) _buttonState[0] = [((GCControllerButtonInput*) element) isPressed];
		if([input buttonB] == element) _buttonState[1] = [((GCControllerButtonInput*) element) isPressed];
		if([input buttonX] == element) _buttonState[2] = [((GCControllerButtonInput*) element) isPressed];
		if([input buttonY] == element) _buttonState[3] = [((GCControllerButtonInput*) element) isPressed];
		if([input leftShoulder] == element) _buttonState[4] = [((GCControllerButtonInput*) element) isPressed];
		if([input rightShoulder] == element) _buttonState[5] = [((GCControllerButtonInput*) element) isPressed];
		if([input buttonMenu] == element) _buttonState[6] = [((GCControllerButtonInput*) element) isPressed];
		if([input buttonOptions] == element) _buttonState[7] = [((GCControllerButtonInput*) element) isPressed];
		if([input dpad] == element) HandleDpad((GCControllerDirectionPad*) element);
		if([input leftTrigger] == element) _buttonState[12] = [((GCControllerButtonInput*) element) isPressed];
		if([input rightTrigger] == element) _buttonState[13] = [((GCControllerButtonInput*) element) isPressed];
		if([input leftThumbstickButton] == element) _buttonState[14] = [((GCControllerButtonInput*) element) isPressed];
		if([input rightThumbstickButton] == element) _buttonState[15] = [((GCControllerButtonInput*) element) isPressed];
		if([input leftThumbstick] == element) HandleThumbstick((GCControllerDirectionPad*) element, 0);
		if([input rightThumbstick] == element) HandleThumbstick((GCControllerDirectionPad*) element, 1);
	}];
	} else if(_micro != nil) {
		[_micro setValueChangedHandler:^ void (GCMicroGamepad* input, GCControllerElement* element) {
			HandleProfileElement(input, element, kMicroGamepadKeys, sizeof(kMicroGamepadKeys) / sizeof(kMicroGamepadKeys[0]), _buttonState);
		}];
	} else if(_basic != nil) {
#pragma clang diagnostic push
#pragma clang diagnostic ignored "-Wdeprecated-declarations"
		[_basic setValueChangedHandler:^ void (GCGamepad* input, GCControllerElement* element) {
			HandleProfileElement(input, element, kBasicGamepadKeys, sizeof(kBasicGamepadKeys) / sizeof(kBasicGamepadKeys[0]), _buttonState);
		}];
#pragma clang diagnostic pop
	}
	//(A pad with none of the three never reaches this class:
	//MacOSGameController::Supports is what AddController asks.)

	_haptics = nil;
	_player = nil;
	if([_controller haptics] != nil) {
		_haptics = [[_controller haptics] createEngineWithLocality:GCHapticsLocalityDefault];
		NSError* error = nil;
		[_haptics startAndReturnError:&error];
		if(error) {
			MessageManager::Log("[Input Device] Failed to initialize force feedback");
			_haptics = nil;
		} else {
			[_haptics retain];
		}
	}
}

MacOSGameController::~MacOSGameController()
{
	if(_haptics) {
		if(_player) {
			NSError* error = nil;
			[_player stopAtTime:0.0 error:&error];
			[_player release];
		}
		[_haptics stopWithCompletionHandler:^ void (NSError* error) {}];
		[_haptics release];
	}
	[_extended setValueChangedHandler:nil];
	[_micro setValueChangedHandler:nil];
#pragma clang diagnostic push
#pragma clang diagnostic ignored "-Wdeprecated-declarations"
	[_basic setValueChangedHandler:nil];
#pragma clang diagnostic pop
	[_extended release];
	[_micro release];
#pragma clang diagnostic push
#pragma clang diagnostic ignored "-Wdeprecated-declarations"
	[_basic release];
#pragma clang diagnostic pop
	[_controller release];
}

void MacOSGameController::HandleDpad(GCControllerDirectionPad* dpad)
{
	_buttonState[8] = [[dpad up] isPressed];
	_buttonState[9] = [[dpad down] isPressed];
	_buttonState[10] = [[dpad left] isPressed];
	_buttonState[11] = [[dpad right] isPressed];
}

void MacOSGameController::HandleThumbstick(GCControllerDirectionPad* stick, int stickNumber)
{
	double hostRatio = _emu->GetSettings()->GetControllerDeadzoneRatio() * 0.4;

	//The four direction buttons of this stick in the pad's own button table
	//("Pad1 X+", "X-", "Y+", "Y-", then X2/Y2 for the right stick), which is what
	//a shortcut's spare binding names - so the same numbers the threshold table is
	//keyed by.
	int direction = (stickNumber * 4) + 16;
	float xAxis = [[stick xAxis] value];
	float yAxis = [[stick yAxis] value];
	_buttonState[direction + 0] = xAxis > AxisThresholdForDirection(_emu, direction + 0, hostRatio);
	_buttonState[direction + 1] = xAxis < -AxisThresholdForDirection(_emu, direction + 1, hostRatio);
	_buttonState[direction + 2] = yAxis > AxisThresholdForDirection(_emu, direction + 2, hostRatio);
	_buttonState[direction + 3] = yAxis < -AxisThresholdForDirection(_emu, direction + 3, hostRatio);
	_axisState[(stickNumber * 2) + 0] = INT16_MAX * xAxis;
	_axisState[(stickNumber * 2) + 1] = INT16_MAX * yAxis;
}

bool MacOSGameController::IsGameController(GCController* controller)
{
	return _controller == controller;
}

bool MacOSGameController::Supports(GCController* controller)
{
	if(controller == nil) {
		return false;
	}
	if([controller extendedGamepad] != nil || [controller microGamepad] != nil) {
		return true;
	}
#pragma clang diagnostic push
#pragma clang diagnostic ignored "-Wdeprecated-declarations"
	return [controller gamepad] != nil;
#pragma clang diagnostic pop
}

bool MacOSGameController::IsButtonPressed(int buttonNumber)
{
	if(buttonNumber < 0 || buttonNumber >= 24) {
		return false;
	}
	return _buttonState[buttonNumber];
}

std::optional<int16_t> MacOSGameController::GetAxisPosition(int axis)
{
	axis -= 24;
	if(axis < 0 || axis >= 4) {
		return std::nullopt;
	}
	return _axisState[axis];
}

std::string MacOSGameController::GetName()
{
	NSString* name = [_controller vendorName];
	return name != nil ? std::string([name UTF8String]) : std::string();
}

bool MacOSGameController::HasRumble()
{
	return _haptics != nil;
}

void MacOSGameController::SetForceFeedback(uint16_t magnitudeRight, uint16_t magnitudeLeft)
{
	NSError* error = nil;

	if(_haptics == nil) {
		return;
	}

	if(_player != nil) {
		[_player stopAtTime:0.0 error:&error];
		if(error) {
			MessageManager::Log("[Input Device] Failed to stop force feedback effect");
			return;
		}
		[_player release];
		_player = nil;
	}

	//If magnitude is zero, only stop current effect
	if(magnitudeRight == 0 && magnitudeLeft == 0) {
		return;
	}

	double strength = (magnitudeRight < magnitudeLeft ? magnitudeLeft : magnitudeRight) / (double)UINT16_MAX;
	CHHapticEventParameter* intensityPar = [[CHHapticEventParameter alloc] initWithParameterID:CHHapticEventParameterIDHapticIntensity value:strength];
	CHHapticEventParameter* sharpnessPar = [[CHHapticEventParameter alloc] initWithParameterID:CHHapticEventParameterIDHapticSharpness value:0.6];
	CHHapticEvent* event = [[CHHapticEvent alloc] initWithEventType:CHHapticEventTypeHapticContinuous parameters:@[intensityPar, sharpnessPar] relativeTime:0.0 duration:GCHapticDurationInfinite];

	CHHapticPattern* pattern = [[CHHapticPattern alloc] initWithEvents:@[event] parameters:@[] error:&error];
	[intensityPar release];
	[sharpnessPar release];
	[event release];
	if(error) {
		MessageManager::Log("[Input Device] Failed to create force feedback pattern");
		[pattern release];
		return;
	}

	_player = [_haptics createPlayerWithPattern:pattern error:&error];
	[pattern release];
	if(error) {
		MessageManager::Log("[Input Device] Failed to create force feedback effect");
		_player = nil;
		return;
	}
	[_player retain];

	[_player startAtTime:0.0 error:&error];
	if(error) {
		MessageManager::Log("[Input Device] Failed to start force feedback effect");
	}
}
