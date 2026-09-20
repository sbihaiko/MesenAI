#pragma once
//Proof that a CDL file is on disk, read from the bytes themselves.
//
//Issue #347: headless_record used to "verify" its `cdl=` write by calling
//LoadCdlFile on the path it had just saved to and comparing the statistics
//before and after. CodeDataLogger::LoadCdlFile leaves the in-memory map
//untouched when the file cannot be read, so on an unwritable path the
//comparison was the run's own statistics against themselves: it passed, the
//tool printed "round-trip verified" and exited 0, and nothing was on disk.
//The seam - not the writer - was the bug: the DllExport SaveCdlFile returns
//void, so a caller outside the Core has no way to hear the ofstream fail.
//
//This check is what a caller can do instead: open the target for read after
//the write and require the bytes to be there. It is deliberately host-free
//(<fstream> and the standard library only) so scripts/core_unit_tests.cpp can
//exercise every branch against real files without an emulator.
#include <cstdint>
#include <fstream>
#include <string>
#include <vector>

struct CdlFileOnDiskCheck
{
	bool Ok = false;
	//Empty when Ok - otherwise a sentence naming what the bytes on disk are,
	//meant to be printed after "cdl: <path> ".
	std::string Reason;
	uint64_t SizeOnDisk = 0;
};

//"CDLv2" + a 4-byte CRC32, mirroring CodeDataLogger::HeaderSize. Repeated
//rather than included so this header pulls in no Core type.
constexpr uint64_t CdlFileHeaderSize = 9;

//`prgBytes` is the size of the ROM memory region the map covers (the
//statistics' TotalBytes). A CDL for it is at least the header plus one byte
//per ROM byte; on the NES a CHR block follows, so a larger file is fine.
//Pass 0 to skip the length check and only prove the file exists with a header.
inline CdlFileOnDiskCheck CheckCdlFileOnDisk(const std::string& path, uint64_t prgBytes)
{
	CdlFileOnDiskCheck result;

	std::ifstream file(path, std::ios::in | std::ios::binary | std::ios::ate);
	if(!file) {
		result.Reason = "could not be opened for read after the write - nothing is on disk at that path";
		return result;
	}

	std::streampos end = file.tellg();
	if(end < 0) {
		result.Reason = "could not be measured after the write";
		return result;
	}
	result.SizeOnDisk = (uint64_t)end;
	if(result.SizeOnDisk == 0) {
		result.Reason = "is empty on disk (0 bytes written)";
		return result;
	}
	if(result.SizeOnDisk < CdlFileHeaderSize) {
		result.Reason = "is " + std::to_string(result.SizeOnDisk) + " bytes on disk, shorter than the 9-byte CDLv2 header";
		return result;
	}

	file.seekg(0, std::ios::beg);
	std::vector<char> header(CdlFileHeaderSize, '\0');
	file.read(header.data(), (std::streamsize)CdlFileHeaderSize);
	if(file.gcount() != (std::streamsize)CdlFileHeaderSize) {
		result.Reason = "could not be read back after the write";
		return result;
	}
	if(std::string(header.data(), 5) != "CDLv2") {
		result.Reason = "does not start with the CDLv2 header";
		return result;
	}

	uint64_t needed = CdlFileHeaderSize + prgBytes;
	if(prgBytes > 0 && result.SizeOnDisk < needed) {
		result.Reason = "is " + std::to_string(result.SizeOnDisk) + " bytes on disk, short of the " +
			std::to_string(needed) + " a map of " + std::to_string(prgBytes) + " ROM bytes needs";
		return result;
	}

	//A map whose every byte is zero is the all-zero capture the caller already
	//refuses in memory; seeing it here means the payload did not reach disk.
	std::vector<char> payload((size_t)(result.SizeOnDisk - CdlFileHeaderSize), '\0');
	if(!payload.empty()) {
		file.read(payload.data(), (std::streamsize)payload.size());
		bool anySet = false;
		for(std::streamsize i = 0; i < file.gcount(); i++) {
			if(payload[(size_t)i] != 0) {
				anySet = true;
				break;
			}
		}
		if(!anySet) {
			result.Reason = "holds nothing but zero bytes after its header";
			return result;
		}
	}

	result.Ok = true;
	return result;
}
