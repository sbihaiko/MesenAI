#pragma once
#include "pch.h"
#include "Utilities/Video/librashader_ld.h"
#include "Utilities/VirtualFile.h"
#include "Utilities/StringUtilities.h"

#ifdef _WIN32
	#include <VersionHelpers.h>
#endif

struct ShaderParamDefinition
{
	char Name[200];
	char Description[200];
	double Min;
	double Max;
	double Initial;
	double Step;
};

class LibrashaderUtilities
{
public:
	static bool IsShaderSupportEnabled()
	{
#ifdef _WIN32
		return IsWindows10OrGreater();
#elif __APPLE__
		//ADR-0237: the Metal renderer runs the filter chain; CheckShaderSupport()
		//below stays the gate (no librashader.dylib -> no shader group).
		return true;
#else
		return true;
#endif
	}

	static bool CheckShaderSupport()
	{
		static bool initialized = false;
		static bool checkResult = false;

		if(initialized) {
			return checkResult;
		}

		if(!IsShaderSupportEnabled()) {
			checkResult = false;
		} else {
			libra_instance_t libra = librashader_load_instance();
			checkResult = libra.instance_loaded;
		}
		return checkResult;
	}

	static uint32_t GetShaderParamCount(const char* shaderFile)
	{
		if(!IsShaderSupportEnabled()) {
			return 0;
		}

		libra_instance_t libra = librashader_load_instance();
		if(!libra.instance_loaded || !((VirtualFile)shaderFile).IsValid()) {
			return 0;
		}

		return CountShaderParams(libra, shaderFile);
	}

	static vector<ShaderParamDefinition> GetShaderParams(const char* shaderFile)
	{
		if(!IsShaderSupportEnabled()) {
			return {};
		}

		libra_instance_t libra = librashader_load_instance();
		if(!libra.instance_loaded || !((VirtualFile)shaderFile).IsValid()) {
			return {};
		}

		return ReadShaderParams(libra, shaderFile);
	}

	//The preset/param-list logic behind the two calls above, taking the loaded
	//instance as an argument so scripts/core_unit_tests.cpp can drive it with a
	//fake libra_instance_t (no librashader library, no GPU). Same behavior as
	//before the split; the support/instance/file gates stay in the callers.
	static uint32_t CountShaderParams(const libra_instance_t& libra, const char* shaderFile)
	{
		libra_shader_preset_t preset;
		libra_error_t error = libra.preset_create_with_options(shaderFile, nullptr, nullptr, &preset);
		if(!error) {
			//The list is only valid when the call succeeds: some presets make it fail
			//and leave the list untouched, and freeing it then crashes
			libra_preset_param_list_t paramList = {};
			libra_error_t paramError = libra.preset_get_runtime_params(&preset, &paramList);
			uint32_t paramCount = 0;
			if(!paramError) {
				paramCount = (uint32_t)paramList.length;
				libra.preset_free_runtime_params(paramList);
			} else {
				libra.error_free(&paramError);
			}
			libra.preset_free(&preset);
			return paramCount;
		}
		//No message is read on this path, so the error is only released (#589)
		libra.error_free(&error);
		return 0;
	}

	static vector<ShaderParamDefinition> ReadShaderParams(const libra_instance_t& libra, const char* shaderFile)
	{
		vector<ShaderParamDefinition> result;
		libra_shader_preset_t preset;
		libra_error_t error = libra.preset_create_with_options(shaderFile, nullptr, nullptr, &preset);
		if(!error) {
			libra_preset_param_list_t paramList = {};
			libra_error_t paramError = libra.preset_get_runtime_params(&preset, &paramList);
			if(paramError) {
				libra.error_free(&paramError);
				libra.preset_free(&preset);
				return result;
			}

			for(uint64_t i = 0; i < paramList.length; i++) {
				const libra_preset_param_t& p = paramList.parameters[i];

				ShaderParamDefinition param = {};
				param.Max = p.maximum;
				param.Min = p.minimum;
				param.Initial = p.initial;
				param.Step = p.step;

				string name = p.name;
				string desc = p.description;
				StringUtilities::CopyToBuffer(name, param.Name, 199);
				StringUtilities::CopyToBuffer(desc, param.Description, 199);
				result.push_back(param);
			}

			libra.preset_free_runtime_params(paramList);
			libra.preset_free(&preset);
		} else {
			libra.error_free(&error);
		}

		return result;
	}
};