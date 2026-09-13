#include "pch.h"
#include "Shared/HeadlessInputEngine.h"

HeadlessInputEngine::HeadlessInputEngine(IHeadlessInputHost* host)
{
	_host = host;
}

bool HeadlessInputEngine::LoadScript(const string& text, double frameRate, string& error)
{
	vector<HeadlessInputStep> steps;
	if(!HeadlessInputScript::Parse(text, frameRate, steps, error)) {
		return false;
	}

	auto lock = _lock.AcquireSafe();
	_steps = std::move(steps);
	return true;
}

void HeadlessInputEngine::SetPauseFrame(uint32_t frame)
{
	auto lock = _lock.AcquireSafe();
	_pauseFrame = frame;
	_pauseRequested = false;
}

void HeadlessInputEngine::SetScriptStartFrame(uint32_t frame)
{
	auto lock = _lock.AcquireSafe();
	_scriptStartFrame = frame;
}

uint32_t HeadlessInputEngine::GetScriptFrameCount()
{
	auto lock = _lock.AcquireSafe();
	return HeadlessInputScript::GetFrameCount(_steps);
}

void HeadlessInputEngine::ApplyToTarget(IHeadlessInputTarget& target, const vector<string>& buttons)
{
	if(buttons.empty()) {
		return;
	}

	//Resolved by name rather than by bit index: the same script letter is a
	//different bit on a NES pad, a GB pad and a SMS pad, and a name the loaded
	//device does not expose simply never matches.
	for(HeadlessButtonName& button : target.GetButtonNames()) {
		if(button.IsNumeric) {
			continue;
		}
		for(const string& name : buttons) {
			if(name == button.Name) {
				target.PressButton(button.ButtonId);
				break;
			}
		}
	}
}

bool HeadlessInputEngine::ApplyFrame(IHeadlessInputTarget& target)
{
	uint32_t frame = _host->GetFrameCount();

	auto lock = _lock.AcquireSafe();

	//Port 1 takes the line's first token, port 2 the token after "|" (F9.22);
	//a device on any other port, or on port 2 under a one-player script, is
	//left alone. The harness plugs a controller into port 2 only when the
	//script names one (HeadlessInputScript::UsesPortTwo).
	uint8_t port = target.GetPort();
	if(port < 2 && frame >= _scriptStartFrame) {
		const HeadlessInputStep* step = HeadlessInputScript::GetStep(_steps, frame - _scriptStartFrame);
		if(step) {
			ApplyToTarget(target, port == 0 ? step->Buttons : step->Port2Buttons);
		}
	}

	//The run's end, decided inside the frame it lands on rather than by a host
	//timer that fires whenever the OS gets round to it. Pause() only sets a
	//flag; the emulation thread parks after finishing this frame, so the run
	//covers exactly [0, _pauseFrame) frames on every host.
	if(!_pauseRequested && frame >= _pauseFrame) {
		_pauseRequested = true;
		if(_host->IsDebugging()) {
			_host->Log("[Headless] frame " + std::to_string(frame) + " reached, but the debugger is attached - not pausing");
		} else {
			_host->Pause();
		}
	}

	//Overlay on top of whatever the physical input produced, never replace it
	return false;
}

void HeadlessInputEngine::OnGameLoaded()
{
	_host->RegisterInputProvider();
}
