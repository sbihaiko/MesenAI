#pragma once
#include <memory>
#include <mutex>
#include <thread>
#include <atomic>
#include "LinuxHapticTick.h"

struct libevdev;
struct ff_effect;
class Emulator;

class LinuxGameController
{
private:
	int _fd = -1;
	int _deviceID = -1;
	libevdev* _device = nullptr;
	bool _disconnected = false;
	std::thread _eventThread;
	std::atomic<bool> _stopFlag;
	Emulator* _emu = nullptr;

	//#1122: the effects are written from the emulation thread (the game's rumble)
	//and from the GUI thread (the menu tick, and the tester's Test rumble button),
	//so every read and write of them is serialised on this one lock.
	std::mutex _ffMutex;
	unique_ptr<ff_effect> _rumbleEffect;
	//#1122: the tick's own kernel effect, so a menu tick never reprograms the
	//gameplay one. Null on a pad that refused the second slot, or that has no
	//force feedback at all - the tick then borrows _rumbleEffect for its pulse.
	unique_ptr<ff_effect> _tickEffect;
	//#1122: what the game last asked this pad's motor for, so a tick is skipped
	//while a cartridge is rumbling and a borrowed pulse can be undone.
	LinuxHapticTick::Motor _tickMotor;
	//Written from whichever thread polls the pad's buttons, read by the tick.
	std::atomic<bool> _enableForceFeedback{ false };
	int _axisDefaultValue[0x100] = {};

	LinuxGameController(Emulator* emu, int deviceID, int fileDescriptor, libevdev* device);
	bool CheckAxis(unsigned int code, bool forPositive);
	bool CheckButton(int btn);
	bool PlayEffect(int effectId);
	void Calibrate();

public:
	~LinuxGameController();

	static std::shared_ptr<LinuxGameController> GetController(Emulator* emu, int deviceID, bool logInformation);

	bool IsDisconnected();
	int GetDeviceID();
	bool IsButtonPressed(int buttonNumber);
	optional<int16_t> GetAxisPosition(int axis);

	void SetForceFeedback(uint16_t rightMagnitude, uint16_t leftMagnitude);

	//Host input tester (PRD slice I.0): device identity from libevdev - product
	//name, USB vendor/product ids (0 when the device does not expose them) and
	//whether force feedback was enabled during initialisation.
	std::string GetName();
	uint32_t GetVendorId();
	uint32_t GetProductId();
	bool HasRumble();

	//#1122: one short pulse on THIS pad's motor - the menu tick's per-device
	//hook. False when the pad has no usable force feedback, and false while the
	//game's own rumble (or the tester's) is non-zero, so a tick never fights the
	//cartridge for the motor.
	bool PlayTick();
};