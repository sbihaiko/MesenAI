namespace Mesen.ViewModels
{
	//#647 (PRD Part B §13.5.3 W-R3, §13.5.4 W-H3, rule 4): Remaster and Share
	//each run their jobs on their own runner, one job at a time per workspace.
	//Linked, each workspace's job gate also sees the other's job, so a pack
	//job and a build never write the same project's mep/ at once; when one
	//ends, the other's buttons come back. The rule is RemasterJobs.RunsOn.
	public static class WorkspaceJobs
	{
		public static void Link(RemasterWorkspaceViewModel remaster, ShareWorkspaceViewModel share)
		{
			remaster.OtherWorkspaceJob = () => share.Job;
			share.OtherWorkspaceJob = () => remaster.Job;
			remaster.JobChanged += share.Refresh;
			share.JobChanged += remaster.Refresh;
		}
	}
}
