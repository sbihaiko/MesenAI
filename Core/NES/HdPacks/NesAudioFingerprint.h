#pragma once
#include "pch.h"
#include "Shared/EnhancementPacks/AudioFingerprint.h"
#include "NES/NesTypes.h"

class NesConsole;
class HdAudioDevice;
struct HdPackData;

//F5.3 (ADR-0047) NES glue for the audio fingerprints: APU state -> NoteFrame,
//the bootstrap recorder (writes auto/audio/) and the run-time replacer that
//starts/stops the OGG of a recognised track through the HD audio device.
class NesAudioFingerprint
{
public:
	//Same audibility rules as the Enhanced Synth tap (EnhancedSynth.cpp)
	static NoteFrame FromApu(const ApuState& apu);
};

//F6.10 (ADR-0240 A4 follow-up): the sound id the host last asked the game's
//driver for - the "trigger id" of the extract-audio tool's enumeration.log.
//While it is set, every segment the bootstrap recorder *opens* is stamped with
//it (AudioFingerprint::TriggerId), which is the join key ADR-0240's A4 needed:
//order and note onsets cannot recover it (docs/validation/slices/f6.10-*).
//-1, the default and what any other host leaves it at, stamps nothing, so this
//is inert for every caller but the extract-audio tool.
//Declared before NesAudioBootstrap: its per-frame Feed reads the id, so the
//declaration has to be visible at that point.
void SetActiveSoundTriggerId(int id);
int GetActiveSoundTriggerId();

//Records the played music into <audioFolder>/fingerprints.json + midi/*.mid.
//Owned by NesConsole while a bootstrap is active; saves when destroyed.
class NesAudioBootstrap
{
private:
	string _audioFolder;
	TrackSegmenter _segmenter;
	uint32_t _frames = 0;
	uint32_t _audible = 0;

public:
	NesAudioBootstrap(const string& audioFolder) : _audioFolder(audioFolder) {}
	~NesAudioBootstrap();

	void OnFrame(const ApuState& apu)
	{
		NoteFrame f = NesAudioFingerprint::FromApu(apu);
		_frames++;
		if(!f.IsSilent()) {
			_audible++;
		}
		//F6.10: read per frame so a host that changes the id between two frames
		//(the extract-audio tool fires one enumerated id at a time) is seen at the
		//segment it is meant to stamp, and stale after the last one.
		_segmenter.SetActiveTriggerId(GetActiveSoundTriggerId());
		_segmenter.Feed(f);
	}
};

//Recognises tracks and drives the replacement OGG (+ APU mute)
class NesAudioReplacer
{
private:
	static constexpr int TrackIdBase = 0x8000; //BgmFilesById keys, away from <bgm> album/track ids

	NesConsole* _console;
	FingerprintMatcher _matcher;
	vector<int> _trackIds; //index -> BgmFilesById key (or -1 when the track has no OGG)
	//F5.4g Block C item 9 (ADR-0133): the last replacement mute mask pushed to
	//the mixer, so we only write it when the SFX picture changed (point 3).
	uint8_t _lastMuteMask = 0;

public:
	NesAudioReplacer(NesConsole* console) : _console(console) {}

	//Loads fingerprints.json from the given layers (lowest precedence first,
	//later layers override same ids), registers the OGG files found under
	//<layer>/bgm/<id>.ogg into hdData->BgmFilesById. Returns the number of
	//tracks with a playable file.
	uint32_t Load(const vector<string>& audioLayers, HdPackData& hdData);

	bool HasTracks() const { return _matcher.HasTracks(); }
	void OnFrame(const ApuState& apu);

private:
	//F5.4g Block C item 9 (ADR-0133): recompute the replacement mute mask from
	//the ChannelRoleClassifier (SFX-flagged melodic channels pass dry) and push
	//it to the mixer only when it changed. Degraded modes (EnhancedAudio or
	//SFX separation off, classifier not warmed up) leave the mask at 0x0F =
	//today's full tonal mute - never "unmute all", which would double the music.
	void UpdateReplacementMuteMask();
};
