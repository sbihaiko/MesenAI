using System;
using System.Collections.Generic;
using System.Text;

namespace Mesen.Logic
{
	//Host-free rewriting of a Dropbox share URL (allow-list kind "dropbox"):
	//the link renders an HTML preview unless `dl=1` is set. Mirrors
	//scripts/fetch_pack.py::force_dropbox_download. BCL only; dual-compiled
	//into UI.Tests.
	public static class CommunityPackDropbox
	{
		//`dl=1` replaces whatever `dl` the link carried; every other parameter
		//survives - `rlkey` in particular, without which /scl/fi/ links 404.
		public static string ForceDownload(string url)
		{
			if(string.IsNullOrEmpty(url) || !Uri.TryCreate(url, UriKind.Absolute, out Uri? parsed)) {
				return url;
			}
			List<string> kept = new();
			string query = parsed.Query.StartsWith('?') ? parsed.Query.Substring(1) : parsed.Query;
			if(query.Length > 0) {
				foreach(string pair in query.Split('&')) {
					if(pair.Length == 0) {
						continue;
					}
					int eq = pair.IndexOf('=');
					string key = eq < 0 ? pair : pair.Substring(0, eq);
					if(!string.Equals(key, "dl", StringComparison.Ordinal)) {
						kept.Add(pair);
					}
				}
			}
			kept.Add("dl=1");
			StringBuilder sb = new();
			sb.Append(parsed.GetLeftPart(UriPartial.Path));
			sb.Append('?');
			sb.Append(string.Join("&", kept));
			return sb.ToString();
		}
	}
}
