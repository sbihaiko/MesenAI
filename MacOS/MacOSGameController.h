#pragma once

#import <Foundation/Foundation.h>
#import <Cocoa/Cocoa.h>
#import <GameController/GameController.h>
#import <CoreHaptics/CoreHaptics.h>

#include <optional>

class Emulator;

class MacOSGameController
{
private:
	Emulator* _emu;

	GCController* _controller;
	//W-P15 (#912): the profile this pad exposes. A controller that offers no
	//extended gamepad is still a pad - micro (the Siri Remote and the
	//one-button pads) or the older basic one - and the profile it does have
	//drives the same button bits, so its keys reach the input layer instead of
	//nothing at all. Exactly one of the three is non-nil.
	GCExtendedGamepad* _extended;
	GCMicroGamepad* _micro;
#pragma clang diagnostic push
#pragma clang diagnostic ignored "-Wdeprecated-declarations"
	GCGamepad* _basic;
#pragma clang diagnostic pop
	CHHapticEngine* _haptics;
	id<CHHapticPatternPlayer> _player;

	bool _buttonState[24] = {};
	int16_t _axisState[4] = {};

	void HandleDpad(GCControllerDirectionPad* dpad);
	void HandleThumbstick(GCControllerDirectionPad* stick, int stickNumber);

public:
	MacOSGameController(Emulator* emu, GCController* controller);
	~MacOSGameController();

	bool IsGameController(GCController* controller);

	//Whether this backend can use the pad at all: it exposes one of the three
	//profiles. MacOSKeyManager::AddController asks this instead of dropping
	//every pad without an extended gamepad (#912).
	static bool Supports(GCController* controller);

	bool IsButtonPressed(int buttonNumber);
	std::optional<int16_t> GetAxisPosition(int axis);

	void SetForceFeedback(uint16_t magnitudeRight, uint16_t magnitudeLeft);

	//Host input tester (PRD slice I.0): the controller's product name as the
	//GameController framework reports it, and whether a haptic engine is
	//available for force feedback.
	std::string GetName();
	bool HasRumble();
};
