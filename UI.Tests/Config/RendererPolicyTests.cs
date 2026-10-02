using Mesen.Logic;
using Xunit;

namespace Mesen.Tests.Config;

//ADR-0237 / PRD slice P.8: which renderer the window asks the core for, and
//when the shader settings are offered. The native half (what the core builds
//for each answer, and the Metal presenter itself) is scripts/metal_presenter_tests.mm.
public class RendererPolicyTests
{
	[Theory]
	//softwareRequested, isMacOs -> software
	[InlineData(false, false, false)] //Windows/Linux default: native renderer
	[InlineData(true, false, true)]   //Windows/Linux, user picked software
	[InlineData(false, true, false)]  //macOS default: the Metal renderer (it used to be forced to software)
	[InlineData(true, true, true)]    //macOS, user picked software: the escape hatch
	public void UsesSoftwareRenderer_OnlyTheUserSettingDecides(bool softwareRequested, bool isMacOs, bool expected)
	{
		Assert.Equal(expected, RendererPolicy.UsesSoftwareRenderer(softwareRequested, isMacOs));
	}

	[Theory]
	//coreSupport, isMacOs, usesSoftware -> shader group offered
	[InlineData(true, false, false, true)]
	[InlineData(true, false, true, true)]   //inherited behavior off macOS: unchanged
	[InlineData(false, false, false, false)] //no librashader.dll/.so: hidden, as before
	[InlineData(false, true, false, false)] //no librashader.dylib: hidden
	[InlineData(true, true, true, false)]   //macOS software renderer would ignore the shader: hidden
	[InlineData(true, true, false, true)]   //macOS Metal renderer + dylib: offered
	public void ShaderGroupAvailable_NeverOffersASettingThatDoesNothing(bool coreSupport, bool isMacOs, bool usesSoftware, bool expected)
	{
		Assert.Equal(expected, RendererPolicy.ShaderGroupAvailable(coreSupport, isMacOs, usesSoftware));
	}
}
