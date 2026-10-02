#import <AppKit/AppKit.h>
#import <Metal/Metal.h>
#import <QuartzCore/CAMetalLayer.h>

#define LIBRA_RUNTIME_METAL
#include "Utilities/Video/librashader_ld.h"

#include "MacOS/MetalPresenter.h"

#include <atomic>
#include <cstring>
#include <memory>
#include <mutex>

//ADR-0237 / PRD slice P.8. See MetalPresenter.h for the contract.

//The view that hosts the layer. A plain NSView cannot be told to back itself
//with a CAMetalLayer, so the presenter adds this subview to the view the UI
//hands over; AppKit then sizes the layer with the view (autoresizing) and the
//host view's own content is untouched. It lets mouse events through to the
//host, so input keeps working the way it does with the software renderer.
@interface MesenMetalHostView : NSView
@property(nonatomic, strong) CAMetalLayer* metalLayer;
@end

@implementation MesenMetalHostView
- (CALayer*)makeBackingLayer
{
	return self.metalLayer;
}
- (BOOL)wantsUpdateLayer
{
	return YES;
}
- (NSView*)hitTest:(NSPoint)point
{
	return nil;
}
@end

static NSString* const kShaderSource = @R"MSL(
#include <metal_stdlib>
using namespace metal;

struct VOut { float4 pos [[position]]; float2 uv; };

vertex VOut quadVertex(uint vid [[vertex_id]])
{
	// triangle strip over the whole target
	float2 p[4] = { float2(-1, -1), float2(1, -1), float2(-1, 1), float2(1, 1) };
	float2 t[4] = { float2(0, 1), float2(1, 1), float2(0, 0), float2(1, 0) };
	VOut o;
	o.pos = float4(p[vid], 0, 1);
	o.uv = t[vid];
	return o;
}

fragment float4 quadFragment(VOut in [[stage_in]], texture2d<float> tex [[texture(0)]], sampler smp [[sampler(0)]])
{
	return tex.sample(smp, in.uv);
}
)MSL";

static const int kRing = 3;

struct Plane
{
	id<MTLTexture> Tex[kRing] = {};
	uint32_t Width = 0;
	uint32_t Height = 0;
	//Overlays only: the content version each ring slot holds. A HUD is
	//re-uploaded into a slot only when the slot is behind the current version,
	//so an unchanged HUD (up to half the drawable's size) costs nothing per frame.
	uint64_t Version = 0;
	uint64_t SlotVersion[kRing] = {};
};

//Issue #584: written by the command buffers' completion handlers (a Metal
//thread), read by Present() on the video thread. Shared so a handler that runs
//late never touches a freed Impl.
struct GpuFaultState
{
	std::atomic<bool> Pending{ false };
	std::mutex Lock;
	std::string Reason;

	void Record(id<MTLCommandBuffer> cmd)
	{
		std::lock_guard<std::mutex> guard(Lock);
		if(!Pending.load()) {
			Reason = cmd.error ? cmd.error.localizedDescription.UTF8String : "a command buffer failed on the GPU";
			Pending = true;
		}
	}
};

struct MetalPresenter::Impl
{
	id<MTLDevice> Device = nil;
	id<MTLCommandQueue> Queue = nil;
	CAMetalLayer* Layer = nil;
	MesenMetalHostView* Host = nil;

	id<MTLRenderPipelineState> OpaquePipe = nil;
	id<MTLRenderPipelineState> BlendPipe = nil;
	id<MTLSamplerState> Nearest = nil;
	id<MTLSamplerState> Linear = nil;

	Plane Frame;
	Plane Emu;
	Plane Script;
	int Ring = 0;
	dispatch_semaphore_t InFlight = nullptr;

	uint32_t OutW = 0;
	uint32_t OutH = 0;

	libra_instance_t Libra = {};
	libra_mtl_filter_chain_t Chain = nullptr;
	bool ShaderOn = false;
	bool ShaderDropped = false;
	std::string Error;
	std::shared_ptr<GpuFaultState> Fault = std::make_shared<GpuFaultState>();

	uint64_t OverlayUploads = 0;

	bool Readback = false;
	std::vector<uint32_t> LastPixels;
	uint32_t LastW = 0;
	uint32_t LastH = 0;

	//Waits until no frame in flight still references the chain or the textures.
	void Drain()
	{
		for(int i = 0; i < kRing; i++) {
			dispatch_semaphore_wait(InFlight, DISPATCH_TIME_FOREVER);
		}
		for(int i = 0; i < kRing; i++) {
			dispatch_semaphore_signal(InFlight);
		}
	}

	void FreeChain()
	{
		if(Chain && Libra.instance_loaded) {
			Libra.mtl_filter_chain_free(&Chain);
		}
		Chain = nullptr;
		ShaderOn = false;
	}

	//Issue #584. Measured on Apple Silicon (macOS 26): after two command
	//buffers on one queue fail with "Caused GPU Hang Error", macOS answers every
	//later submission on that queue - an unfiltered blit included, and still
	//after 5 s - with "Ignored (for causing prior/excessive GPU errors)", so the
	//picture freezes for good. A new queue on the same device is accepted
	//again. So a fault drops the chain (which keeps hanging every frame) and
	//replaces the queue; the next frame is presented unfiltered.
	void RecoverFromGpuFault()
	{
		if(!Fault->Pending.load()) {
			return;
		}
		//Frames still in flight may fault too; wait them out before reading.
		Drain();
		std::string reason;
		{
			std::lock_guard<std::mutex> guard(Fault->Lock);
			reason = Fault->Reason;
			Fault->Reason.clear();
			Fault->Pending = false;
		}
		if(ShaderOn) {
			FreeChain();
			ShaderDropped = true;
			Error = "the shader was disabled after a GPU error (" + reason + "); presenting unfiltered";
		} else {
			Error = "a frame failed on the GPU (" + reason + ")";
		}
		id<MTLCommandQueue> queue = [Device newCommandQueue];
		if(queue) {
			Queue = queue;
		}
	}

	void SetLibraError(const char* what, libra_error_t error)
	{
		char* msg = nullptr;
		if(Libra.instance_loaded && Libra.error_write && Libra.error_write(error, &msg) == 0 && msg) {
			Error = std::string(what) + msg;
			Libra.error_free_string(&msg);
		} else {
			Error = what;
		}
		Libra.error_free(&error);
	}

	bool BuildPipelines()
	{
		NSError* err = nil;
		id<MTLLibrary> lib = [Device newLibraryWithSource:kShaderSource options:nil error:&err];
		if(!lib) {
			Error = std::string("MSL compile failed: ") + (err ? err.localizedDescription.UTF8String : "?");
			return false;
		}
		MTLRenderPipelineDescriptor* d = [MTLRenderPipelineDescriptor new];
		d.vertexFunction = [lib newFunctionWithName:@"quadVertex"];
		d.fragmentFunction = [lib newFunctionWithName:@"quadFragment"];
		d.colorAttachments[0].pixelFormat = MTLPixelFormatBGRA8Unorm;
		OpaquePipe = [Device newRenderPipelineStateWithDescriptor:d error:&err];

		d.colorAttachments[0].blendingEnabled = YES;
		d.colorAttachments[0].rgbBlendOperation = MTLBlendOperationAdd;
		d.colorAttachments[0].alphaBlendOperation = MTLBlendOperationAdd;
		d.colorAttachments[0].sourceRGBBlendFactor = MTLBlendFactorSourceAlpha;
		d.colorAttachments[0].sourceAlphaBlendFactor = MTLBlendFactorOne;
		d.colorAttachments[0].destinationRGBBlendFactor = MTLBlendFactorOneMinusSourceAlpha;
		d.colorAttachments[0].destinationAlphaBlendFactor = MTLBlendFactorOneMinusSourceAlpha;
		BlendPipe = [Device newRenderPipelineStateWithDescriptor:d error:&err];
		if(!OpaquePipe || !BlendPipe) {
			Error = std::string("pipeline creation failed: ") + (err ? err.localizedDescription.UTF8String : "?");
			return false;
		}

		MTLSamplerDescriptor* sd = [MTLSamplerDescriptor new];
		sd.minFilter = sd.magFilter = MTLSamplerMinMagFilterNearest;
		sd.sAddressMode = sd.tAddressMode = MTLSamplerAddressModeClampToEdge;
		Nearest = [Device newSamplerStateWithDescriptor:sd];
		sd.minFilter = sd.magFilter = MTLSamplerMinMagFilterLinear;
		Linear = [Device newSamplerStateWithDescriptor:sd];
		return true;
	}

	//Makes sure the plane's ring of textures is (width x height) and uploads
	//the pixels into the slot this frame owns. The ring plus the in-flight
	//semaphore means the CPU never rewrites a texture the GPU may still read.
	bool Upload(Plane& plane, const uint32_t* pixels, uint32_t w, uint32_t h)
	{
		if(!Resize(plane, w, h) || !pixels) {
			return false;
		}
		[plane.Tex[Ring] replaceRegion:MTLRegionMake2D(0, 0, w, h) mipmapLevel:0 withBytes:pixels bytesPerRow:w * 4];
		return true;
	}

	//The overlay variant: uploads only when this frame's slot is stale.
	void UploadOverlay(Plane& plane, const MetalOverlay& hud)
	{
		bool resized = plane.Width != hud.Width || plane.Height != hud.Height;
		if(!Resize(plane, hud.Width, hud.Height)) {
			return;
		}
		if(resized || hud.Dirty) {
			plane.Version++;
		}
		if(plane.SlotVersion[Ring] != plane.Version) {
			[plane.Tex[Ring] replaceRegion:MTLRegionMake2D(0, 0, hud.Width, hud.Height) mipmapLevel:0 withBytes:hud.Pixels bytesPerRow:hud.Width * 4];
			plane.SlotVersion[Ring] = plane.Version;
			OverlayUploads++;
		}
	}

	bool Resize(Plane& plane, uint32_t w, uint32_t h)
	{
		if(!w || !h) {
			return false;
		}
		if(plane.Width != w || plane.Height != h) {
			MTLTextureDescriptor* td = [MTLTextureDescriptor texture2DDescriptorWithPixelFormat:MTLPixelFormatBGRA8Unorm width:w height:h mipmapped:NO];
			td.usage = MTLTextureUsageShaderRead;
			td.storageMode = MTLStorageModeShared;
			for(int i = 0; i < kRing; i++) {
				plane.Tex[i] = [Device newTextureWithDescriptor:td];
				if(!plane.Tex[i]) {
					plane.Width = plane.Height = 0;
					return false;
				}
			}
			plane.Width = w;
			plane.Height = h;
			plane.Version++;
		}
		return true;
	}

	void DrawOverlay(id<MTLRenderCommandEncoder> enc, Plane& plane)
	{
		if(!plane.Width) {
			return;
		}
		[enc setFragmentTexture:plane.Tex[Ring] atIndex:0];
		[enc setFragmentSamplerState:Nearest atIndex:0];
		[enc drawPrimitives:MTLPrimitiveTypeTriangleStrip vertexStart:0 vertexCount:4];
	}
};

MetalPresenter::MetalPresenter() : _impl(new Impl())
{
	_impl->Device = MTLCreateSystemDefaultDevice();
	if(_impl->Device) {
		_impl->Queue = [_impl->Device newCommandQueue];
		_impl->InFlight = dispatch_semaphore_create(kRing);
	}
}

MetalPresenter::~MetalPresenter()
{
	//Drain the GPU before the textures and the chain go away.
	if(_impl->InFlight) {
		_impl->Drain();
	}
	_impl->FreeChain();
	MesenMetalHostView* host = _impl->Host;
	_impl->Host = nil;
	if(host) {
		dispatch_block_t remove = ^{
			[host removeFromSuperview];
		};
		if([NSThread isMainThread]) {
			remove();
		} else {
			dispatch_async(dispatch_get_main_queue(), remove);
		}
	}
	delete _impl;
}

bool MetalPresenter::HasMetalDevice()
{
	id<MTLDevice> d = MTLCreateSystemDefaultDevice();
	return d != nil;
}

bool MetalPresenter::InitWithView(void* nsView)
{
	if(!_impl->Device || !_impl->Queue) {
		_impl->Error = "no Metal device";
		return false;
	}
	if(!nsView) {
		_impl->Error = "null view handle";
		return false;
	}
	id obj = (__bridge id)nsView;
	if(![obj isKindOfClass:[NSView class]]) {
		_impl->Error = "the viewer handle is not an NSView";
		return false;
	}
	NSView* parent = (NSView*)obj;

	if(!_impl->BuildPipelines()) {
		return false;
	}

	CAMetalLayer* layer = [CAMetalLayer layer];
	layer.device = _impl->Device;
	layer.pixelFormat = MTLPixelFormatBGRA8Unorm;
	//Off on purpose, in the app too: the drawable is then readable, which is
	//what lets scripts/metal_presenter_tests.mm check the configuration that
	//ships rather than a test-only variant. The cost on Apple GPUs is small.
	layer.framebufferOnly = NO;
	layer.opaque = YES;
	layer.presentsWithTransaction = NO;
	layer.contentsGravity = kCAGravityResize;
	_impl->Layer = layer;

	MesenMetalHostView* host = [[MesenMetalHostView alloc] initWithFrame:parent.bounds];
	host.metalLayer = layer;
	host.autoresizingMask = NSViewWidthSizable | NSViewHeightSizable;
	host.wantsLayer = YES;
	_impl->Host = host;

	//AppKit view hierarchy changes belong on the main thread. The layer exists
	//already, so drawables can be taken before the attach lands. Never block on
	//the main thread from here: the UI thread may be waiting on this one.
	dispatch_block_t attach = ^{
		[parent addSubview:host];
	};
	if([NSThread isMainThread]) {
		attach();
	} else {
		dispatch_async(dispatch_get_main_queue(), attach);
	}
	return true;
}

void MetalPresenter::SetOutputSize(uint32_t width, uint32_t height)
{
	if(width == 0 || height == 0) {
		return;
	}
	_impl->OutW = width;
	_impl->OutH = height;
	if(_impl->Layer) {
		_impl->Layer.drawableSize = CGSizeMake(width, height);
	}
}

void MetalPresenter::SetVsync(bool enabled)
{
	if(_impl->Layer) {
		_impl->Layer.displaySyncEnabled = enabled ? YES : NO;
	}
}

bool MetalPresenter::SetShader(const std::string& presetPath, const std::vector<MetalShaderParam>& params)
{
	_impl->Drain();
	//The new chain is built on the queue: never on one macOS stopped accepting.
	_impl->RecoverFromGpuFault();
	_impl->FreeChain();
	_impl->Error.clear();

	if(!_impl->Libra.instance_loaded) {
		_impl->Libra = librashader_load_instance();
	}
	if(!_impl->Libra.instance_loaded || !_impl->Libra.mtl_filter_chain_create) {
		_impl->Error = "librashader.dylib is not available";
		return false;
	}

	libra_shader_preset_t preset = {};
	libra_error_t error = _impl->Libra.preset_create_with_options(presetPath.c_str(), nullptr, nullptr, &preset);
	if(error) {
		_impl->SetLibraError("preset_create_with_options failed: ", error);
		return false;
	}

	error = _impl->Libra.mtl_filter_chain_create(&preset, _impl->Queue, nullptr, &_impl->Chain);
	if(error) {
		_impl->Chain = nullptr;
		_impl->SetLibraError("mtl_filter_chain_create failed: ", error);
		return false;
	}

	_impl->ShaderOn = true;
	UpdateShaderParams(params);
	return true;
}

void MetalPresenter::UpdateShaderParams(const std::vector<MetalShaderParam>& params)
{
	if(!_impl->ShaderOn || !_impl->Chain) {
		return;
	}
	for(const MetalShaderParam& p : params) {
		libra_error_t error = _impl->Libra.mtl_filter_chain_set_param(&_impl->Chain, p.Name.c_str(), p.Value);
		if(error) {
			//An unknown parameter name is not fatal for the preset.
			_impl->Libra.error_free(&error);
		}
	}
}

void MetalPresenter::ClearShader()
{
	_impl->Drain();
	_impl->FreeChain();
}

bool MetalPresenter::ShaderActive() const
{
	return _impl->ShaderOn;
}

const std::string& MetalPresenter::LastError() const
{
	return _impl->Error;
}

bool MetalPresenter::TakeShaderDropped()
{
	bool dropped = _impl->ShaderDropped;
	_impl->ShaderDropped = false;
	return dropped;
}

void MetalPresenter::SetReadbackEnabled(bool enabled)
{
	_impl->Readback = enabled;
}

bool MetalPresenter::LayerIsMetalLayer() const
{
	return _impl->Host && [_impl->Host.layer isKindOfClass:[CAMetalLayer class]] && _impl->Host.layer == _impl->Layer;
}

uint64_t MetalPresenter::OverlayUploadCount() const
{
	return _impl->OverlayUploads;
}

bool MetalPresenter::GetLastPresented(std::vector<uint32_t>& pixels, uint32_t& width, uint32_t& height) const
{
	if(_impl->LastPixels.empty()) {
		return false;
	}
	pixels = _impl->LastPixels;
	width = _impl->LastW;
	height = _impl->LastH;
	return true;
}

bool MetalPresenter::Present(const uint32_t* frame, uint32_t width, uint32_t height, uint32_t frameNumber, bool bilinear,
	const MetalOverlay& emuHud, const MetalOverlay& scriptHud)
{
	Impl& m = *_impl;
	if(!m.Layer || !m.Queue || m.OutW == 0 || m.OutH == 0) {
		return false;
	}

	@autoreleasepool {
		m.RecoverFromGpuFault();
		dispatch_semaphore_wait(m.InFlight, DISPATCH_TIME_FOREVER);
		m.Ring = (m.Ring + 1) % kRing;

		if(!m.Upload(m.Frame, frame, width, height)) {
			dispatch_semaphore_signal(m.InFlight);
			return false;
		}
		if(emuHud.Pixels) {
			m.UploadOverlay(m.Emu, emuHud);
		}
		if(scriptHud.Pixels) {
			m.UploadOverlay(m.Script, scriptHud);
		}

		//AppKit may resize the drawable when the view's bounds or backing scale
		//change; the picture is always drawn at the size the UI computed.
		CGSize want = CGSizeMake(m.OutW, m.OutH);
		if(!CGSizeEqualToSize(m.Layer.drawableSize, want)) {
			m.Layer.drawableSize = want;
		}

		id<CAMetalDrawable> drawable = [m.Layer nextDrawable];
		if(!drawable) {
			m.Error = "nextDrawable returned nil";
			dispatch_semaphore_signal(m.InFlight);
			return false;
		}

		id<MTLCommandBuffer> cmd = [m.Queue commandBuffer];
		bool filtered = false;

		if(m.ShaderOn && m.Chain) {
			libra_viewport_t viewport = { 0.0f, 0.0f, m.OutW, m.OutH };
			libra_error_t error = m.Libra.mtl_filter_chain_frame(&m.Chain, cmd, frameNumber, m.Frame.Tex[m.Ring], drawable.texture, &viewport, nullptr, nullptr);
			if(error) {
				m.SetLibraError("mtl_filter_chain_frame failed: ", error);
			} else {
				filtered = true;
			}
		}

		MTLRenderPassDescriptor* pass = [MTLRenderPassDescriptor renderPassDescriptor];
		pass.colorAttachments[0].texture = drawable.texture;
		pass.colorAttachments[0].storeAction = MTLStoreActionStore;
		if(filtered) {
			pass.colorAttachments[0].loadAction = MTLLoadActionLoad;
		} else {
			pass.colorAttachments[0].loadAction = MTLLoadActionClear;
			pass.colorAttachments[0].clearColor = MTLClearColorMake(0, 0, 0, 1);
		}

		id<MTLRenderCommandEncoder> enc = [cmd renderCommandEncoderWithDescriptor:pass];
		if(!filtered) {
			[enc setRenderPipelineState:m.OpaquePipe];
			[enc setFragmentTexture:m.Frame.Tex[m.Ring] atIndex:0];
			[enc setFragmentSamplerState:(bilinear ? m.Linear : m.Nearest) atIndex:0];
			[enc drawPrimitives:MTLPrimitiveTypeTriangleStrip vertexStart:0 vertexCount:4];
		}
		[enc setRenderPipelineState:m.BlendPipe];
		if(scriptHud.Pixels) {
			m.DrawOverlay(enc, m.Script);
		}
		if(emuHud.Pixels) {
			m.DrawOverlay(enc, m.Emu);
		}
		[enc endEncoding];

		dispatch_semaphore_t sem = m.InFlight;
		std::shared_ptr<GpuFaultState> fault = m.Fault;
		[cmd addCompletedHandler:^(id<MTLCommandBuffer> done) {
			if(done.status == MTLCommandBufferStatusError) {
				fault->Record(done);
			}
			dispatch_semaphore_signal(sem);
		}];
		[cmd presentDrawable:drawable];
		[cmd commit];

		if(m.Readback) {
			[cmd waitUntilCompleted];
			if(cmd.status != MTLCommandBufferStatusCompleted) {
				//The completion handler may not have run yet; record the fault
				//here too and recover now, so the caller sees it on this frame.
				m.Fault->Record(cmd);
				m.RecoverFromGpuFault();
				return false;
			}
			id<MTLTexture> t = drawable.texture;
			m.LastW = (uint32_t)t.width;
			m.LastH = (uint32_t)t.height;
			m.LastPixels.assign((size_t)m.LastW * m.LastH, 0);
			[t getBytes:m.LastPixels.data() bytesPerRow:m.LastW * 4 fromRegion:MTLRegionMake2D(0, 0, m.LastW, m.LastH) mipmapLevel:0];
		}
	}
	return true;
}
