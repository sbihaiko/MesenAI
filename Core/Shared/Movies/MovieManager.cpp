#include "pch.h"
#include <algorithm>
#include "Utilities/VirtualFile.h"
#include "Utilities/ZipReader.h"
#include "Shared/Emulator.h"
#include "Shared/EmuSettings.h"
#include "Shared/MessageManager.h"
#include "Shared/Movies/MovieManager.h"
#include "Shared/Movies/MesenMovie.h"
#include "Shared/Movies/BizHawkMovie.h"
#include "Shared/Movies/MovieRecorder.h"

MovieManager::MovieManager(Emulator* emu)
{
	_emu = emu;
}

void MovieManager::Record(RecordMovieOptions options)
{
	//Stop any active recording/playback before starting playback for this movie
	Stop();

	shared_ptr<MovieRecorder> recorder(new MovieRecorder(_emu));
	if(recorder->Record(options)) {
		_recorder.reset(recorder);
	}
}

bool MovieManager::RecordAndShare(RecordMovieOptions options)
{
	//No debugger lock: Record() power-cycles the console, and holding the
	//debugger lock across that is the deadlock Emulator::AcquireLock documents
	//(MesenMovie takes the same non-debugger lock for the same reason).
	auto lock = _emu->AcquireLock(false);

	if(!_emu->GetConsole()) {
		return false;
	}

	//Ends any recording or playback first - and with it any earlier share, whose
	//settings are restored here, before this call snapshots them again.
	Stop();

	ConsoleType consoleType = _emu->GetConsoleType();
	EmuSettings* settings = _emu->GetSettings();
	SharePowerOnState snapshot = ShareRecordingSettings::Capture(*settings);
	if(!ShareRecordingSettings::Apply(*settings, consoleType)) {
		//Refuse rather than record an archive the section 3 lint rejects.
		MessageManager::DisplayMessage("Movies", "MovieShareUnsupportedConsole");
		return false;
	}

	options.RecordFrom = RecordMovieFrom::StartWithoutSaveData;
	Record(options);
	if(!Recording()) {
		ShareRecordingSettings::Restore(*settings, snapshot);
		return false;
	}

	_shareRestore = snapshot;
	_shareActive = true;
	return true;
}

void MovieManager::RestoreShareSettings()
{
	if(_shareActive) {
		_shareActive = false;
		ShareRecordingSettings::Restore(*_emu->GetSettings(), _shareRestore);
	}
}

void MovieManager::Play(VirtualFile file, bool forTest)
{
	vector<uint8_t> fileData;
	if(file.IsValid() && file.ReadFile(fileData)) {
		shared_ptr<IMovie> player;
		if(memcmp(fileData.data(), "PK", 2) == 0) {
			//Mesen movie
			ZipReader reader;
			reader.LoadArchive(fileData);

			vector<string> files = reader.GetFileList();
			if(std::find(files.begin(), files.end(), "GameSettings.txt") != files.end()) {
				player.reset(new MesenMovie(_emu, forTest));
			} else if(std::find(files.begin(), files.end(), "Input Log.txt") != files.end()) {
				player.reset(new BizHawkMovie(_emu, false));
			}
		}

		//Stop any active recording/playback before starting playback for this movie
		Stop();

		if(player && player->Play(file)) {
			_player.reset(player);
			if(!forTest) {
				MessageManager::DisplayMessage("Movies", "MoviePlaying", file.GetFileName());
			}
		}
	}
}

void MovieManager::Stop()
{
	shared_ptr<IMovie> player = _player.lock();
	if(player) {
		player->Stop();
	}
	_player.reset();
	//Resetting the recorder writes the .mmo, and GameSettings.txt serializes the
	//settings at that moment, so the player's own settings come back only after.
	_recorder.reset();
	RestoreShareSettings();
}

bool MovieManager::Playing()
{
	return _player != nullptr;
}

bool MovieManager::Recording()
{
	return _recorder != nullptr;
}
