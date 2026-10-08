namespace Mesen.Logic
{
	public enum PadKeyShape
	{
		DPad,
		Pill,
		Round,
		Shoulder
	}

	//Where one button sits on the setup sheet's pad, in the pad's own pixels.
	public readonly record struct PadKey(double Left, double Top, double Width, double Height, PadKeyShape Shape);

	//A console's pad body: its size, the D-pad's center square (drawn, never a
	//step) and, for the Game Boy, the screen drawn above the buttons.
	public sealed record PadBody(double Width, double Height, PadKey DPadCentre, PadKey? Screen);

	//ADR-0249 (W-P15): the setup sheet draws a pad - the renderer's 340 x 140
	//body with the D-pad cross centered at (70, 70), Select/Start pills either
	//side of the middle, B low and A high on the right, and the GBA's L/R
	//shoulders along the top - so the lit key shows which button to press.
	public static class ControllerPadLayout
	{
		public const double Width = 340;
		public const double Height = 140;

		//The D-pad's center square, drawn but never a step.
		public static readonly PadKey DPadCentre = new(58, 58, 24, 24, PadKeyShape.DPad);

		public static PadKey Of(SetupButton button)
		{
			return button switch {
				SetupButton.Up => new(58, 34, 24, 24, PadKeyShape.DPad),
				SetupButton.Down => new(58, 82, 24, 24, PadKeyShape.DPad),
				SetupButton.Left => new(34, 58, 24, 24, PadKeyShape.DPad),
				SetupButton.Right => new(82, 58, 24, 24, PadKeyShape.DPad),
				SetupButton.Select => new(124, 84, 40, 12, PadKeyShape.Pill),
				SetupButton.Start => new(176, 84, 40, 12, PadKeyShape.Pill),
				SetupButton.B => new(227, 67, 34, 34, PadKeyShape.Round),
				SetupButton.A => new(279, 45, 34, 34, PadKeyShape.Round),
				SetupButton.L => new(28, 6, 64, 18, PadKeyShape.Shoulder),
				_ => new(248, 6, 64, 18, PadKeyShape.Shoulder)
			};
		}

		//The D-pad arms and the pills are too small for a word; the face and
		//shoulder buttons carry their letter (or the Master System's 1/2).
		//#940 (PRD §13.5.2 W-P15): the console's own pad. NES and GBA keep the
		//wide body above; the Game Boy is the upright handheld - screen on top,
		//D-pad low left, B/A diagonal low right, Select/Start pills at the
		//bottom; the Master System pad has its 1 (KeyMapping B) and 2 (A) side
		//by side and no Select/Start.
		public static PadBody BodyOf(SetupConsole console)
		{
			return console switch {
				SetupConsole.GameBoy => new(200, 300, new(40, 192, 24, 24, PadKeyShape.DPad), new(24, 20, 152, 116, PadKeyShape.Pill)),
				_ => new(Width, Height, DPadCentre, null)
			};
		}

		public static PadKey Of(SetupConsole console, SetupButton button)
		{
			return (console, button) switch {
				(SetupConsole.GameBoy, SetupButton.Up) => new(40, 168, 24, 24, PadKeyShape.DPad),
				(SetupConsole.GameBoy, SetupButton.Down) => new(40, 216, 24, 24, PadKeyShape.DPad),
				(SetupConsole.GameBoy, SetupButton.Left) => new(16, 192, 24, 24, PadKeyShape.DPad),
				(SetupConsole.GameBoy, SetupButton.Right) => new(64, 192, 24, 24, PadKeyShape.DPad),
				(SetupConsole.GameBoy, SetupButton.B) => new(112, 204, 32, 32, PadKeyShape.Round),
				(SetupConsole.GameBoy, SetupButton.A) => new(152, 180, 32, 32, PadKeyShape.Round),
				(SetupConsole.GameBoy, SetupButton.Select) => new(56, 262, 36, 10, PadKeyShape.Pill),
				(SetupConsole.GameBoy, SetupButton.Start) => new(104, 262, 36, 10, PadKeyShape.Pill),
				(SetupConsole.MasterSystem, SetupButton.B) => new(222, 53, 34, 34, PadKeyShape.Round),
				(SetupConsole.MasterSystem, SetupButton.A) => new(274, 53, 34, 34, PadKeyShape.Round),
				_ => Of(button)
			};
		}

		public static bool ShowsLabel(SetupButton button)
		{
			PadKeyShape shape = Of(button).Shape;
			return shape is PadKeyShape.Round or PadKeyShape.Shoulder;
		}
	}
}
