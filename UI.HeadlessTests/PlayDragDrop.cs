using System;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Platform.Storage;
using Avalonia.Threading;

namespace Mesen.HeadlessTests;

//#953: a file dropped on a surface the way the OS hands it over - a drag that
//enters the window and drops at a point - so the drop is hit-tested and routed
//through the real DragDrop.AllowDrop/DropEvent wiring, not called on a handler.
internal static class PlayDragDrop
{
	public static void DropFile(TopLevel root, Control target, string path)
	{
		Point at = target.TranslatePoint(new Point(target.Bounds.Width / 2, target.Bounds.Height / 2), root)
			?? throw new InvalidOperationException($"{target.Name} is not in the window it is dropped on");
		DataTransfer data = new();
		data.Add(DataTransferItem.CreateFile(FileAt(path)));
		//Avalonia raises DropEvent on the target the drag entered.
		root.DragDrop(at, RawDragEventType.DragEnter, data, DragDropEffects.Copy, RawInputModifiers.None);
		root.DragDrop(at, RawDragEventType.Drop, data, DragDropEffects.Copy, RawInputModifiers.None);
		Dispatcher.UIThread.RunJobs();
	}

	//The drop handlers only read the item's Path: a stand-in for the platform's
	//storage file, so the drop never depends on a StorageProvider.
	private static IStorageFile FileAt(string path)
	{
		IStorageFile file = DispatchProxy.Create<IStorageFile, LocalFile>();
		((LocalFile)(object)file).Path = new Uri(path);
		return file;
	}

	public class LocalFile : DispatchProxy
	{
		public Uri Path = null!;

		protected override object? Invoke(MethodInfo? method, object?[]? args)
		{
			return method?.Name switch {
				"get_Path" => Path,
				"get_Name" => System.IO.Path.GetFileName(Path.LocalPath),
				"Dispose" => null,
				_ => throw new NotSupportedException(method?.Name)
			};
		}
	}
}
