using System.Collections.Generic;

namespace Mesen.Logic;

public enum RemasterScanKind
{
	//The recordings' shapes seen (ADR-0252 §1).
	Shapes,
	//The project's painted cells (ADR-0252 §2).
	Cells,
	//The tiles' paint comparisons and cells.
	Tiles,
	//W-R0's recent projects' painted cells.
	Recent
}

//The user's rule (2026-10-03): every wait moves. Opening or refreshing a
//Remaster project reads files off the UI thread; this tracks which scans are
//in flight so the screen shows a wait until the last answers. Each Begin hands
//out a token: only the latest scan of a kind may clear it, so a stale scan
//never hides the indicator of a newer one.
public sealed class RemasterScanWait
{
	private readonly Dictionary<RemasterScanKind, int> _latest = new();
	private readonly HashSet<RemasterScanKind> _active = new();

	public bool IsWaiting => _active.Count > 0;

	public bool IsWaitingFor(RemasterScanKind kind) => _active.Contains(kind);

	//Any scan of the open project (not W-R0's list).
	public bool IsWaitingForProject => _active.Count > (_active.Contains(RemasterScanKind.Recent) ? 1 : 0);

	public int Begin(RemasterScanKind kind)
	{
		int token = Next(kind);
		_active.Add(kind);
		return token;
	}

	//The scan is no longer needed (nothing to read): its late answer is ignored.
	public void Cancel(RemasterScanKind kind)
	{
		Next(kind);
		_active.Remove(kind);
	}

	//The scan answered or failed; false when a newer scan of the kind exists.
	public bool End(RemasterScanKind kind, int token)
	{
		if(!_latest.TryGetValue(kind, out int latest) || latest != token) {
			return false;
		}
		_active.Remove(kind);
		return true;
	}

	private int Next(RemasterScanKind kind)
	{
		int next = (_latest.TryGetValue(kind, out int n) ? n : 0) + 1;
		_latest[kind] = next;
		return next;
	}
}
