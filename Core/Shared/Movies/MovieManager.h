#pragma once
#include "pch.h"
#include "Shared/MessageManager.h"
#include "Shared/Interfaces/IInputProvider.h"
#include "Shared/Movies/MovieTypes.h"
#include "Shared/Movies/MovieRecorder.h"
#include "Shared/Movies/ShareRecordingSettings.h"
#include "Utilities/safe_ptr.h"

class VirtualFile;
class Emulator;

class IMovie : public IInputProvider
{
public:
	virtual ~IMovie() = default;

	virtual bool Play(VirtualFile& file) = 0;
	virtual void Stop() = 0;
	virtual bool IsPlaying() = 0;
};

class MovieManager
{
private:
	Emulator* _emu = nullptr;
	safe_ptr<IMovie> _player;
	safe_ptr<MovieRecorder> _recorder;

	//ADR-0205 section 2: the player's power-on settings, snapshotted before
	//Record and share changed them. Restored by Stop(), after the recorder has
	//written GameSettings.txt (which serializes the settings as they are *now*).
	SharePowerOnState _shareRestore;
	bool _shareActive = false;

	void RestoreShareSettings();

public:
	MovieManager(Emulator* emu);

	void Record(RecordMovieOptions options);

	//The single Record-and-share action (ADR-0205 section 2): records from
	//power-on with the loaded console's power-on state made deterministic, so
	//the archive has neither SaveState.mss nor Battery*. options.RecordFrom is
	//ignored. Returns false - recording nothing, changing nothing - for a
	//console it cannot make deterministic or when the file cannot be written.
	bool RecordAndShare(RecordMovieOptions options);
	bool SharingRecording() { return _shareActive; }
	void Play(VirtualFile file, bool silent = false);
	void Stop();
	bool Playing();
	bool Recording();
};