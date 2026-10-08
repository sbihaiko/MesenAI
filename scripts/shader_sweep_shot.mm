//ADR-0237: one preset through the shipped macOS presenter, for the manual
//shader sweep (scripts/shader_sweep.py, `make shader-sweep`). macOS only, needs
//a Metal device. Not a test and not a CI gate: the sweep spawns one process of
//this per preset so a crash, a hang or a GPU fault costs one row, not the run.
//
//It attaches MacOS/MetalPresenter to an offscreen NSView (as
//metal_presenter_tests.mm does), loads the preset, presents the reference frame
//NFRAMES times (temporal presets need more than a few frames to settle), reads
//the last drawable back and compares it with a CPU nearest scale of the frame,
//which is what the presenter shows with no shader.
//
//usage: shader_sweep_shot <frame.png|builtin> <scale> <nframes> <preset|none> [out.png]
//prints one line: SWEEP set=<0|1> present=<0|1> frame=<0|1> w=<w> h=<h> diffpx=<n> black=<%> mean=<v> err=<text>
//frame=1: a frame call of the filter chain failed (MetalPresenter::TakeFrameError,
//#593). That frame was presented unfiltered and Present() still returned true,
//so the pixels alone would read as IDENTICAL; err= then carries the frame error.
//MESEN_SWEEP_INJECT_FRAME_FAILURES=<n> fails the first n frame calls on purpose
//(MetalPresenter::InjectFrameFailures), to check that path of the sweep itself.
//librashader.dylib is found next to this binary, then in the working directory.
#import <AppKit/AppKit.h>
#import <ImageIO/ImageIO.h>

#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <string>
#include <vector>

#include "MacOS/MetalPresenter.h"

//ROM-free reference frame, 256x240 like an NES picture: flat NES-palette
//tiles on black, 1-pixel lines, a checkerboard and a gray ramp, so a filter
//that blurs, masks, curves or recolors changes a measurable number of pixels.
static void BuiltinFrame(std::vector<uint32_t>& px, uint32_t& w, uint32_t& h)
{
	static const uint32_t pal[] = { 0x0F0F0F, 0x2038EC, 0xB0287C, 0xE45C10, 0x58F898, 0xF8D878, 0xFCFCFC, 0x00A800 };
	w = 256;
	h = 240;
	px.assign((size_t)w * h, 0xFF000000u);
	for(uint32_t y = 0; y < h; y++) {
		for(uint32_t x = 0; x < w; x++) {
			uint32_t c = 0;
			if(y < 96) {
				c = ((x % 16) < 14 && (y % 16) < 14) ? pal[((x / 16) * 5 + (y / 16) * 3) % 8] : 0;
			} else if(y < 128) {
				c = ((x + y) & 1) ? 0xFCFCFC : 0;
			} else if(y < 160) {
				uint32_t v = x;
				c = (v << 16) | (v << 8) | v;
			} else {
				c = (x % 8 == 0 || y % 8 == 0) ? pal[(x / 32) % 8] : 0;
			}
			px[(size_t)y * w + x] = 0xFF000000u | c;
		}
	}
}

static bool LoadPng(const char* path, std::vector<uint32_t>& px, uint32_t& w, uint32_t& h)
{
	CFURLRef url = CFURLCreateFromFileSystemRepresentation(nullptr, (const UInt8*)path, strlen(path), false);
	CGImageSourceRef src = CGImageSourceCreateWithURL(url, nullptr);
	CFRelease(url);
	if(!src) {
		return false;
	}
	CGImageRef img = CGImageSourceCreateImageAtIndex(src, 0, nullptr);
	CFRelease(src);
	if(!img) {
		return false;
	}
	w = (uint32_t)CGImageGetWidth(img);
	h = (uint32_t)CGImageGetHeight(img);
	px.assign((size_t)w * h, 0);
	CGColorSpaceRef cs = CGColorSpaceCreateWithName(kCGColorSpaceSRGB);
	//BGRA in memory (little-endian 0xAARRGGBB), the core's frame buffer layout.
	CGContextRef ctx = CGBitmapContextCreate(px.data(), w, h, 8, w * 4, cs, kCGImageAlphaPremultipliedFirst | kCGBitmapByteOrder32Little);
	CGContextDrawImage(ctx, CGRectMake(0, 0, w, h), img);
	CGContextRelease(ctx);
	CGColorSpaceRelease(cs);
	CGImageRelease(img);
	for(uint32_t& p : px) {
		p |= 0xFF000000u;
	}
	return true;
}

static void SavePng(const std::string& path, const std::vector<uint32_t>& px, uint32_t w, uint32_t h)
{
	CGColorSpaceRef cs = CGColorSpaceCreateWithName(kCGColorSpaceSRGB);
	CGContextRef ctx = CGBitmapContextCreate((void*)px.data(), w, h, 8, w * 4, cs, kCGImageAlphaNoneSkipFirst | kCGBitmapByteOrder32Little);
	CGImageRef img = CGBitmapContextCreateImage(ctx);
	CFURLRef url = CFURLCreateFromFileSystemRepresentation(nullptr, (const UInt8*)path.c_str(), path.size(), false);
	CGImageDestinationRef dst = CGImageDestinationCreateWithURL(url, CFSTR("public.png"), 1, nullptr);
	if(dst) {
		CGImageDestinationAddImage(dst, img, nullptr);
		CGImageDestinationFinalize(dst);
		CFRelease(dst);
	}
	CFRelease(url);
	CGImageRelease(img);
	CGContextRelease(ctx);
	CGColorSpaceRelease(cs);
}

static void Report(bool set, bool present, bool frameFailed, uint32_t w, uint32_t h, size_t diff, double black, double mean, std::string err)
{
	for(char& c : err) {
		if(c == '\n' || c == '\r' || c == '\t') {
			c = ' ';
		}
	}
	printf("SWEEP set=%d present=%d frame=%d w=%u h=%u diffpx=%zu black=%.1f mean=%.1f err=%s\n", set, present, frameFailed, w, h, diff, black, mean, err.c_str());
	fflush(stdout);
}

int main(int argc, char** argv)
{
	if(argc < 5) {
		fprintf(stderr, "usage: shader_sweep_shot <frame.png|builtin> <scale> <nframes> <preset|none> [out.png]\n");
		return 2;
	}
	@autoreleasepool {
		[NSApplication sharedApplication];
		std::vector<uint32_t> frame;
		uint32_t fw = 0, fh = 0;
		if(strcmp(argv[1], "builtin") == 0) {
			BuiltinFrame(frame, fw, fh);
		} else if(!LoadPng(argv[1], frame, fw, fh)) {
			fprintf(stderr, "cannot load %s\n", argv[1]);
			return 2;
		}
		uint32_t scale = (uint32_t)atoi(argv[2]);
		uint32_t nframes = (uint32_t)atoi(argv[3]);
		std::string preset = argv[4];
		if(scale == 0 || nframes == 0) {
			fprintf(stderr, "scale and nframes must be positive\n");
			return 2;
		}
		uint32_t ow = fw * scale, oh = fh * scale;

		NSView* view = [[NSView alloc] initWithFrame:NSMakeRect(0, 0, ow, oh)];
		MetalPresenter p;
		if(!p.InitWithView((__bridge void*)view)) {
			fprintf(stderr, "InitWithView failed: %s\n", p.LastError().c_str());
			return 2;
		}
		p.SetOutputSize(ow, oh);
		p.SetReadbackEnabled(true);

		bool set = true;
		if(preset != "none") {
			set = p.SetShader(preset, {});
			if(!set) {
				Report(false, false, false, 0, 0, 0, 0, 0, p.LastError());
				return 0;
			}
		}

		if(const char* inject = getenv("MESEN_SWEEP_INJECT_FRAME_FAILURES")) {
			p.InjectFrameFailures((uint32_t)atoi(inject));
		}

		bool ok = true;
		bool frameFailed = false;
		std::string frameError;
		for(uint32_t f = 1; f <= nframes && ok; f++) {
			ok = p.Present(frame.data(), fw, fh, f, false, MetalOverlay(), MetalOverlay());
			//A frame error makes the presenter fall back to an unfiltered blit
			//and Present() still succeed; keep the first one, later frames
			//would only repeat it.
			if(p.TakeFrameError(frameError)) {
				frameFailed = true;
				break;
			}
		}
		std::vector<uint32_t> out;
		uint32_t w = 0, h = 0;
		if(!ok || !p.GetLastPresented(out, w, h)) {
			Report(set, false, frameFailed, 0, 0, 0, 0, 0, frameFailed ? frameError : p.LastError());
			return 0;
		}

		size_t diff = 0, black = 0;
		double sum = 0;
		bool sameSize = (w == ow && h == oh);
		for(size_t k = 0; k < out.size(); k++) {
			uint32_t c = out[k] & 0xFFFFFFu;
			black += (c == 0);
			sum += ((c >> 16) & 0xFF) + ((c >> 8) & 0xFF) + (c & 0xFF);
			if(sameSize) {
				uint32_t x = (uint32_t)(k % w), y = (uint32_t)(k / w);
				diff += c != (frame[(size_t)(y / scale) * fw + x / scale] & 0xFFFFFFu);
			}
		}
		if(!sameSize) {
			diff = out.size();
		}
		double n = out.empty() ? 1.0 : (double)out.size();
		Report(set, true, frameFailed, w, h, diff, 100.0 * black / n, sum / (3.0 * n), frameFailed ? frameError : p.LastError());
		if(argc > 5) {
			SavePng(argv[5], out, w, h);
		}
	}
	return 0;
}
