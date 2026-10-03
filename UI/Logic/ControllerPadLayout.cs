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

	//ADR-0249 (W-P15): the setup sheet draws a pad - the renderer's 340 x 140
	//body with the D-pad cross centred at (70, 70), Select/Start pills either
	//side of the middle, B low and A high on the right, and the GBA's L/R
	//shoulders along the top - so the lit key shows which button to press.
	public static class ControllerPadLayout
	{
		public const double Width = 340;
		public const double Height = 140;

		//The D-pad's centre square, drawn but never a step.
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
		public static bool ShowsLabel(SetupButton button)
		{
			PadKeyShape shape = Of(button).Shape;
			return shape is PadKeyShape.Round or PadKeyShape.Shoulder;
		}
	}
}
