#pragma once
#include <cstdint>

//#1122 (spec #1102, follow-up of #1106): the Linux evdev menu tick.
//
//The syscall half of a tick is platform code - a second ff_effect, an
//EVIOCSFF upload and one EV_FF write on the pad's own fd - and it only runs on
//a machine with a real evdev pad. What is worth driving from any host is the
//decision around it: may this pad be ticked at all, which force-feedback effect
//does the pulse go through, and what does a borrowed one have to put back. That
//lives here, host-free (no libevdev, no linux/input.h, no syscalls), so
//scripts/linux_haptic_tests.cpp drives it and LinuxGameController only carries
//out what it returns.
namespace LinuxHapticTick
{
	//A 40 ms pulse at 37.5% of full scale: long enough to feel as a click
	//through the shell, short enough not to read as rumble. The gameplay effect
	//this borrows a slot from is uploaded with a 2000 ms replay.
	constexpr uint16_t PulseMagnitude = 0x6000;
	constexpr uint16_t PulseLengthMs = 40;
	constexpr uint16_t GameplayEffectLengthMs = 2000;

	//Where the pulse went. `Own`: the pad took a second kernel effect for the
	//tick, so playing it never reprograms the gameplay one. `Borrowed`: the pad
	//had a single force-feedback slot, so the gameplay effect carries the pulse
	//and is restored afterwards. `None`: nothing was played.
	enum class Slot
	{
		None = 0,
		Own = 1,
		Borrowed = 2
	};

	//The three gameplay-effect fields a borrowed pulse overwrites, and the ones
	//it puts back. The same type carries both so the restore is the same write
	//with the remembered numbers.
	struct EffectValues
	{
		uint16_t Strong = 0;
		uint16_t Weak = 0;
		uint16_t Length = 0;
	};

	inline EffectValues PulseValues()
	{
		return EffectValues{ PulseMagnitude, PulseMagnitude, PulseLengthMs };
	}

	//The game's own rumble, as last handed to this pad. It is remembered for two
	//reasons: a menu tick must never reprogram the motor out from under a
	//cartridge (ADR-0256 Decision 1 - the pad belongs to the console while a game
	//runs), and a borrowed pulse has to put the game's numbers back.
	class Motor
	{
	public:
		//`right`/`left` are SetForceFeedback's own parameters, in its order.
		void SetGameplayRumble(uint16_t right, uint16_t left)
		{
			_right = right;
			_left = left;
		}

		bool GameplayRumbleActive() const { return _right != 0 || _left != 0; }
		uint16_t GameplayRight() const { return _right; }
		uint16_t GameplayLeft() const { return _left; }

		//Which effect a tick on this pad may use. `forceFeedbackEnabled` is the
		//condition HasRumble reports for the same pad, `hasOwnSlot` says the
		//backend got a dedicated tick effect uploaded at setup. `None` while the
		//cartridge is rumbling, so a tick never cuts a game's feedback short and
		//never fights the Test rumble button for the motor.
		Slot Decide(bool forceFeedbackEnabled, bool hasOwnSlot) const
		{
			if(!forceFeedbackEnabled || GameplayRumbleActive()) {
				return Slot::None;
			}
			return hasOwnSlot ? Slot::Own : Slot::Borrowed;
		}

	private:
		uint16_t _right = 0;
		uint16_t _left = 0;
	};

	//What a borrowed pulse puts back: the gameplay magnitudes in the effect's own
	//strong/weak mapping - strong takes the left channel, weak the right, which is
	//the mapping SetForceFeedback already writes - and the replay length the
	//effect was uploaded with at setup. Restoring the length is what keeps the
	//pad's next gameplay rumble a 2 s rumble instead of a 40 ms click.
	inline EffectValues GameplayValues(const Motor& motor)
	{
		return EffectValues{ motor.GameplayLeft(), motor.GameplayRight(), GameplayEffectLengthMs };
	}
}
