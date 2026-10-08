namespace Mesen.Logic;

//#1066: the Play picker's two claims over a bump of the grid. FinishFallback is
//"this bump is the end of a scan whose restore never landed" and RestoreLanding
//is "this bump is the remembered game arriving"; the focus arbiter reads them to
//keep a ring the player has moved. A claim is about ONE bump, so it is spent the
//moment the ring is the player's again and with the visit that made it - never
//left readable for the next bump of the same visit (the console filter's rebuild).
//
//Host-free (ADR-0123): the view-model holds the value and asks this for every
//transition.
public readonly record struct ScanClaims(bool FinishFallback, bool RestoreLanding)
{
	public static readonly ScanClaims None = new(false, false);

	public ScanClaims WithFinishFallback() => this with { FinishFallback = true };

	public ScanClaims WithRestoreLanding() => this with { RestoreLanding = true };

	//A tile took the ring (the player's move or the arbiter's): the claims are spent.
	public ScanClaims AfterRingTaken() => None;

	//The sheet going down ends the visit, and the sheet coming up is a new visit
	//that owes the player nothing it promised before.
	public ScanClaims AfterVisibilityChanged(bool visible) => None;
}
