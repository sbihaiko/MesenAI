namespace Mesen.Interop
{
	//Host-free counterpart to the ConsoleType/CheatType enums that previously
	//lived in EmuApi.cs (Fase 2, docs/roadmap/plano-testes-unitarios.md).
	//EmuApi.cs is Avalonia-tainted (references Avalonia.Media.Imaging), so
	//these two enums were split out into this file so UI/Logic/ helpers (and
	//UI.Tests, via UI.Tests.csproj's dual-compile of this file) can consume
	//ConsoleType/CheatType without pulling in Avalonia or the EmuApi P/Invoke
	//surface. Both stay in the Mesen.Interop namespace and keep their exact
	//member names/values, so every existing consumer of EmuApi.cs continues
	//to compile unchanged.
	public enum ConsoleType
	{
		Snes = 0,
		Gameboy = 1,
		Nes = 2,
		PcEngine = 3,
		Sms = 4,
		Gba = 5,
		Ws = 6,
	}

	//Host-free counterpart to the GamepadBackend enum that lived in
	//UI/Interop/InputApi.cs. It moved here for the same reason ConsoleType and
	//CheatType did: ADR-0255 slice 5's host-free rule (UI/Logic/DeviceReconnect)
	//keys a pad's identity by its backend, and UI.Tests dual-compiles UI/Logic
	//without InputApi.cs (which names EmuApi.DllName, so it is not host-free).
	//Slice 1 needed the same thing first: the Controller sheet's host-free rule
	//(UI/Logic/ControllerSheet.cs) names a backend to pick the core's own button
	//order for it, so the sheet and DeviceReconnect share this one copy.
	//Member names/values match the core's GamepadBackend (IKeyManager.h) and
	//InputApi's previous copy, so every consumer compiles unchanged.
	public enum GamepadBackend : byte
	{
		None = 0,
		XInput = 1,
		DirectInput = 2,
		Evdev = 3,
		GameController = 4
	}

	public enum CheatType : byte
	{
		NesGameGenie = 0,
		NesProActionRocky,
		NesCustom,
		GbGameGenie,
		GbGameShark,
		SnesGameGenie,
		SnesProActionReplay,
		PceRaw,
		PceAddress,
		SmsProActionReplay,
		SmsGameGenie
	}
}
