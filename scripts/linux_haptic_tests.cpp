//#1122 (spec #1102, follow-up of #1106): the Linux evdev per-device haptic tick.
//
//Linux/LinuxGameController.cpp cannot be linked into scripts/core_unit_tests.cpp
//- it needs libevdev, linux/input.h and an open /dev/input/eventN - so this is
//its own framework-free binary, and it is wired into `make core-unit-tests` so
//the Linux CI leg compiles and runs it with no evdev pad present.
//
//Two halves, deliberately:
//
//  * The decision, driven directly. `LinuxHapticTick::Motor` is host-free and
//    syscall-free - what the game last asked the motor for, and which
//    force-feedback effect a tick may use - so the interesting rules (skip while
//    the cartridge is rumbling, prefer the pad's own slot, remember what a
//    borrowed pulse must put back) are ordinary assertions instead of a read of
//    the source.
//  * The wiring, pinned by reading the backend's own source, which is the
//    pattern this repo already uses for platform code the suite cannot link
//    (scripts/core_unit_tests.cpp, TestTheMacHapticTickKeepsItsStateSound). These
//    say the decision above is actually consulted, and that the two threads that
//    reach for the motor serialise on one lock.
//
//Run from the repo root: the source pins resolve relative paths.
#include "Linux/LinuxHapticTick.h"
#include <cstdio>
#include <fstream>
#include <sstream>
#include <string>

namespace
{
	int gCases = 0;
	int gFailures = 0;

	void Check(bool condition, const std::string& name, const std::string& detail = "")
	{
		gCases++;
		if(condition) {
			printf("PASS  %s\n", name.c_str());
		} else {
			gFailures++;
			printf("FAIL  %s%s%s\n", name.c_str(), detail.empty() ? "" : ": ", detail.c_str());
		}
	}

	//--- the decision, driven directly --------------------------------------

	LinuxHapticTick::Motor MotorWith(uint16_t gameplayRight, uint16_t gameplayLeft)
	{
		LinuxHapticTick::Motor motor;
		motor.SetGameplayRumble(gameplayRight, gameplayLeft);
		return motor;
	}

	//A pad that has force feedback and got a slot of its own for the tick.
	void TestATickIsSkippedWhileTheCartridgeOwnsTheMotor()
	{
		Check(MotorWith(0x8000, 0).Decide(true, true) == LinuxHapticTick::Slot::None,
			"#1122: a game rumbling the right channel keeps its motor - no tick");
		Check(MotorWith(0, 0x8000).Decide(true, true) == LinuxHapticTick::Slot::None,
			"#1122: ...and the left channel alone is just as much a game's rumble");
		Check(MotorWith(0, 0).Decide(true, true) == LinuxHapticTick::Slot::Own,
			"#1122: with the motor free the tick plays, on the pad's own slot");
		Check(!MotorWith(0x8000, 0).GameplayRumbleActive() == false,
			"#1122: a non-zero magnitude is an active rumble");
		Check(MotorWith(0, 0).GameplayRumbleActive() == false,
			"#1122: both channels at zero is a free motor");
	}

	void TestAPadWithNoForceFeedbackNeverTicks()
	{
		Check(MotorWith(0, 0).Decide(false, true) == LinuxHapticTick::Slot::None,
			"#1122: a pad with no force feedback answers false, own slot or not");
		Check(MotorWith(0, 0).Decide(false, false) == LinuxHapticTick::Slot::None,
			"#1122: ...and neither does one with no effect at all");
	}

	void TestAPadWithoutASlotOfItsOwnBorrowsTheGameplayEffect()
	{
		Check(MotorWith(0, 0).Decide(true, false) == LinuxHapticTick::Slot::Borrowed,
			"#1122: a pad with a single force-feedback slot ticks through the gameplay effect");
		Check(MotorWith(0, 0).Decide(true, true) == LinuxHapticTick::Slot::Own,
			"#1122: a pad with a slot of its own never touches the gameplay effect");
	}

	void TestABorrowedPulseIsUndoneFromTheValuesTheGameAskedFor()
	{
		//The mapping is SetForceFeedback's own: strong takes the left magnitude,
		//weak the right. A restore that swapped them would leave the next upload
		//rumbling the wrong motor.
		LinuxHapticTick::EffectValues back = LinuxHapticTick::GameplayValues(MotorWith(0x1234, 0x5678));
		Check(back.Strong == 0x5678, "#1122: the restored strong magnitude is the game's left channel");
		Check(back.Weak == 0x1234, "#1122: ...and the restored weak one is its right");
		Check(back.Length == LinuxHapticTick::GameplayEffectLengthMs,
			"#1122: the restored replay length is the one the gameplay effect was uploaded with");

		LinuxHapticTick::EffectValues pulse = LinuxHapticTick::PulseValues();
		Check(pulse.Strong == LinuxHapticTick::PulseMagnitude && pulse.Weak == LinuxHapticTick::PulseMagnitude,
			"#1122: the pulse drives both motors");
		Check(pulse.Length == LinuxHapticTick::PulseLengthMs && pulse.Length < back.Length,
			"#1122: the pulse is far shorter than the gameplay rumble it borrows the slot from");
	}

	//--- the backend's own shape, source-pinned -------------------------------

	std::string ReadSource(const char* path)
	{
		std::ifstream in(path, std::ios::in | std::ios::binary);
		std::stringstream ss;
		ss << in.rdbuf();
		return ss.str();
	}

	//The body of `signature`, from its opening brace to the next line that closes
	//at column 0 - enough for one function, including one with nested blocks.
	std::string Body(const std::string& src, const std::string& signature)
	{
		size_t at = src.find(signature);
		if(at == std::string::npos) {
			return "";
		}
		size_t open = src.find('{', at);
		size_t end = src.find("\n}", open);
		return open == std::string::npos || end == std::string::npos ? "" : src.substr(open, end - open);
	}

	void TestTheLinuxBackendUploadsTheTickEffectOnlyAfterTheGameplayOne()
	{
		std::string cpp = ReadSource("Linux/LinuxGameController.cpp");
		Check(!cpp.empty(), "#1122: the Linux backend source is readable");

		//A tick effect is a second kernel slot: it is only asked for once the
		//gameplay effect was accepted, so a pad that cannot rumble at all never
		//holds a tick effect either.
		std::string ctor = Body(cpp, "LinuxGameController::LinuxGameController(");
		size_t rumbleUpload = ctor.find("rc < 0");
		size_t tickUpload = ctor.find("_tickEffect.reset(new ff_effect())");
		Check(tickUpload != std::string::npos && rumbleUpload != std::string::npos && rumbleUpload < tickUpload,
			"#1122: the tick effect is uploaded only after the gameplay effect was accepted");
		Check(ctor.find("LinuxHapticTick::PulseValues()") != std::string::npos,
			"#1122: the tick effect is uploaded with the pulse's own values, not a second rumble");
		Check(ctor.find("EVIOCSFF, _tickEffect.get()") != std::string::npos,
			"#1122: a pad that refuses the second slot keeps working through the gameplay one");
	}

	void TestTheLinuxTickSerialisesTheTwoThreadsOnOneLock()
	{
		std::string header = ReadSource("Linux/LinuxGameController.h");
		Check(header.find("#include <mutex>") != std::string::npos,
			"#1122: the controller header carries the lock's own header");
		Check(header.find("std::mutex _ffMutex;") != std::string::npos,
			"#1122: one mutex guards the force-feedback effects");
		Check(header.find("LinuxHapticTick::Motor _tickMotor;") != std::string::npos,
			"#1122: the backend remembers what the game asked the motor for");
		Check(header.find("bool PlayTick();") != std::string::npos,
			"#1122: LinuxGameController offers the per-device tick");

		std::string cpp = ReadSource("Linux/LinuxGameController.cpp");
		std::string setFf = Body(cpp, "void LinuxGameController::SetForceFeedback(");
		std::string tick = Body(cpp, "bool LinuxGameController::PlayTick(");
		Check(setFf.find("lock_guard<std::mutex>") != std::string::npos
			&& setFf.find("_tickMotor.SetGameplayRumble(") != std::string::npos,
			"#1122: the emulation thread records the game's rumble under the lock");
		Check(tick.find("lock_guard<std::mutex>") != std::string::npos,
			"#1122: the UI thread's tick takes the same lock");
	}

	void TestTheLinuxTickPlaysItsOwnEffectAndRestoresABorrowedOne()
	{
		std::string cpp = ReadSource("Linux/LinuxGameController.cpp");
		std::string tick = Body(cpp, "bool LinuxGameController::PlayTick(");
		Check(!tick.empty(), "#1122: LinuxGameController::PlayTick is implemented");

		//The decision is the host-free one, not a second copy of it.
		Check(tick.find("_tickMotor.Decide(") != std::string::npos,
			"#1122: the backend asks LinuxHapticTick::Motor which effect to use");
		Check(tick.find("EV_FF") == std::string::npos,
			"#1122: ...and the syscall itself lives in one place, not in the tick");

		//Borrowed: the pulse goes in and the gameplay values go back, in that
		//order, on the same effect.
		size_t pulseWritten = tick.find("LinuxHapticTick::PulseValues()");
		size_t restored = tick.find("LinuxHapticTick::GameplayValues(_tickMotor)");
		Check(pulseWritten != std::string::npos && restored != std::string::npos && pulseWritten < restored,
			"#1122: a borrowed pulse writes the pulse first and puts the game's values back after");
	}

	void TestTheLinuxKeyManagerRoutesATickToOnePadByIndex()
	{
		std::string header = ReadSource("Linux/LinuxKeyManager.h");
		Check(header.find("bool PlayGamepadTick(uint32_t index) override;") != std::string::npos,
			"#1122: LinuxKeyManager overrides the per-device tick hook");

		std::string cpp = ReadSource("Linux/LinuxKeyManager.cpp");
		std::string body = Body(cpp, "bool LinuxKeyManager::PlayGamepadTick(");
		Check(!body.empty(), "#1122: LinuxKeyManager::PlayGamepadTick is implemented");
		Check(body.find("index < _controllers.size()") != std::string::npos
			&& body.find("_controllers[index]->PlayTick()") != std::string::npos,
			"#1122: the tick reaches the one pad it was asked for and no other");
		Check(body.find("for(") == std::string::npos && body.find("for (") == std::string::npos,
			"#1122: ...and is not broadcast to every pad");
	}
}

int main()
{
	printf("Linux evdev haptic tick (#1122)\n\n");

	TestATickIsSkippedWhileTheCartridgeOwnsTheMotor();
	TestAPadWithNoForceFeedbackNeverTicks();
	TestAPadWithoutASlotOfItsOwnBorrowsTheGameplayEffect();
	TestABorrowedPulseIsUndoneFromTheValuesTheGameAskedFor();

	TestTheLinuxBackendUploadsTheTickEffectOnlyAfterTheGameplayOne();
	TestTheLinuxTickSerialisesTheTwoThreadsOnOneLock();
	TestTheLinuxTickPlaysItsOwnEffectAndRestoresABorrowedOne();
	TestTheLinuxKeyManagerRoutesATickToOnePadByIndex();

	printf("\n%d/%d cases passed\n", gCases - gFailures, gCases);
	return gFailures == 0 ? 0 : 1;
}
