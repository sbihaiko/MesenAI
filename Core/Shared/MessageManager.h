#pragma once

#include "pch.h"

#include "Core/Shared/Interfaces/IMessageManager.h"
#include <unordered_map>
#include "Utilities/SimpleLock.h"

#ifdef _DEBUG
	#define LogDebug(msg) MessageManager::Log(msg);
	#define LogDebugIf(cond, msg) if(cond) { MessageManager::Log(msg); }
#else
	#define LogDebug(msg)
	#define LogDebugIf(cond, msg)
#endif

class MessageManager
{
private:
	static IMessageManager* _messageManager;
	static std::unordered_map<string, string> _enResources;

	static bool _osdEnabled;
	static bool _outputToStdout;
	static SimpleLock _logLock;
	static SimpleLock _messageLock;
	static std::list<string> _log;
	static std::ofstream _logFile;
	static bool _logFileTried;
	static uint64_t _logDropped;
	static uint64_t _logFileBytes;

	static void RotateLogFile();

public:
	//ADR-0208: both logs now have a retention policy that was chosen rather
	//than inherited. The in-memory ring keeps its historical 1 000 entries but
	//says so when eviction happened; mesen.log is capped by size and rotates
	//into mesen.log.1 through the same single generation the session rotation
	//already uses. Before this, the ring discarded silently (issue #160 spent
	//a bug report on that) while the file grew without bound (issue #302 added
	//~570 KB in a single pack load).
	static constexpr size_t MaxLogEntries = 1000;
	static constexpr uint64_t MaxLogFileBytes = 4 * 1024 * 1024;

	static void SetOptions(bool osdEnabled, bool outputToStdout);

	static string Localize(string key);

	static void RegisterMessageManager(IMessageManager* messageManager);
	static void UnregisterMessageManager(IMessageManager* messageManager);
	static void DisplayMessage(string title, string message, string param1 = "", string param2 = "");

	static void Log(string message = "");
	static void ClearLog();
	static string GetLog();

	//How many entries the ring has evicted since the last ClearLog(). Exposed
	//so a caller can tell a truncated log from a short one without parsing
	//the notice GetLog() prepends.
	static uint64_t GetDroppedLogEntryCount();
	static string FormatTruncationNotice(uint64_t dropped);

	//Rotate mesen.log and reopen it under the current home folder. Called
	//lazily on the first message, again whenever the size cap is reached, and
	//by any caller that has just changed the home folder.
	static void ReopenLogFile();
};
