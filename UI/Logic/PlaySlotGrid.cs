namespace Mesen.Logic
{
	//ADR-0249 (W-P4 › Save States): the slot grid's copy. Player mode speaks
	//the overlay's language ("Slot 1", "Empty"); classic and Advanced keep
	//Mesen's strings ("Load State Menu", "Slot #1", "<empty>"). Returns the
	//resource message key; the ViewModel resolves it (UI/Logic firewall).
	public static class PlaySlotGrid
	{
		public static string TitleKey(bool load, bool player)
		{
			if(player) {
				return load ? "PlayerLoadStateTitle" : "PlayerSaveStateTitle";
			}
			return load ? "LoadStateDialog" : "SaveStateDialog";
		}

		public static string SlotKey(bool player) => player ? "PlayerSlotNumber" : "SlotNumber";

		public static string EmptyKey(bool player) => player ? "PlayerEmptySlot" : "EmptyState";
	}
}
