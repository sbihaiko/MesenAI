using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Mesen.Localization;
using Mesen.Logic;
using Mesen.Utilities;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Mesen.Windows
{
	public partial class MessageBox : MesenWindow
	{
		public MessageBox()
		{
			InitializeComponent();
		}

		private void InitializeComponent()
		{
			AvaloniaXamlLoader.Load(this);
		}

		public static Task<DialogResult> Show(Window? parent, string text, string title, MessageBoxButtons buttons, MessageBoxIcon icon)
		{
			return Show(parent, text, title, buttons, icon, out _);
		}

		public static Task<DialogResult> Show(Window? parent, string text, string title, MessageBoxButtons buttons, MessageBoxIcon icon, out MessageBox outMsgbox)
		{
			DialogResult result = DialogResult.OK;
			MessageBox msgbox = new MessageBox() { Title = title };
			outMsgbox = msgbox;
			msgbox.GetControl<TextBlock>("Text").Text = text;

			switch(icon) {
				case MessageBoxIcon.Error: msgbox.GetControl<Image>("imgError").IsVisible = true; break;
				case MessageBoxIcon.Warning: msgbox.GetControl<Image>("imgWarning").IsVisible = true; break;
				case MessageBoxIcon.Question: msgbox.GetControl<Image>("imgQuestion").IsVisible = true; break;
				case MessageBoxIcon.Info: msgbox.GetControl<Image>("imgInfo").IsVisible = true; break;
			}

			parent ??= ApplicationHelper.GetActiveOrMainWindow();
			bool player = PlayerDialogScope.UsesPlayerLook(parent);
			if(player) {
				msgbox.UsePlayerLook(text, icon);
			}

			List<Button> added = new();
			StackPanel buttonPanel = msgbox.GetControl<StackPanel>(player ? "PlayerMsgButtons" : "pnlButtons");
			void AddButton(string caption, DialogResult r)
			{
				Button btn = new Button { Content = caption };
				added.Add(btn);
				if(r == DialogResult.Cancel || (r == DialogResult.No && buttons != MessageBoxButtons.YesNoCancel) || (r == DialogResult.OK && buttons == MessageBoxButtons.OK)) {
					btn.IsCancel = true;
				}
				btn.Click += (_, _) => {
					result = r;
					msgbox.Close();
				};
				buttonPanel.Children.Add(btn);
			}

			if(buttons == MessageBoxButtons.OK || buttons == MessageBoxButtons.OKCancel) {
				AddButton(ResourceHelper.GetMessage("btnOK"), DialogResult.OK);
				result = DialogResult.OK;
			}

			if(buttons == MessageBoxButtons.YesNo || buttons == MessageBoxButtons.YesNoCancel) {
				AddButton(ResourceHelper.GetMessage("btnYes"), DialogResult.Yes);
				AddButton(ResourceHelper.GetMessage("btnNo"), DialogResult.No);
				result = DialogResult.No;
			}

			if(buttons == MessageBoxButtons.OKCancel || buttons == MessageBoxButtons.YesNoCancel) {
				AddButton(ResourceHelper.GetMessage("btnCancel"), DialogResult.Cancel);
				result = DialogResult.Cancel;
			}

			if(player) {
				ArrangePlayerButtons(buttonPanel, added);
			}

			TaskCompletionSource<DialogResult> tcs = new TaskCompletionSource<DialogResult>();
			msgbox.Closed += (_, _) => { tcs.TrySetResult(result); };

			if(parent != null) {
				if(!OperatingSystem.IsWindows()) {
					//TODOv2 - This fixes Avalonia apparently not working properly with CenterOwner on X11 with SizeToContent="WidthAndHeight"
					msgbox.Opened += (_, _) => { WindowExtensions.CenterWindow(msgbox, parent); };
				}

				msgbox.WindowStartupLocation = WindowStartupLocation.CenterOwner;
				msgbox.ShowDialog(parent);
			} else {
				Console.WriteLine(title + " - " + text);
			}

			return tcs.Task;
		}
	}

	public partial class MessageBox
	{
		//ADR-0249 (W-X1, W-X2): the Player look - the banner on a white sheet in Inter,
		//the classic box hidden.
		private void UsePlayerLook(string text, MessageBoxIcon icon)
		{
			this.GetControl<StackPanel>("ClassicMsgRoot").IsVisible = false;
			this.GetControl<Border>("PlayerMsgRoot").IsVisible = true;
			this.GetControl<Border>("PlayerMsgRoot").Classes.Add("player");
			this.GetControl<TextBlock>("PlayerMsgText").Text = text;

			BannerKind kind = PlayerDialog.BannerOf(icon switch {
				MessageBoxIcon.Error => DialogTone.Error,
				MessageBoxIcon.Warning => DialogTone.Warning,
				MessageBoxIcon.Question => DialogTone.Question,
				_ => DialogTone.Info,
			});
			Border banner = this.GetControl<Border>("PlayerMsgBanner");
			PathIcon glyph = this.GetControl<PathIcon>("PlayerMsgIcon");
			switch(kind) {
				case BannerKind.Stop:
					banner.Classes.Add("stop");
					glyph.Data = (Geometry)this.FindResource("PlayerIconStop")!;
					glyph.Margin = new Thickness(3, 4, 14, 0);
					break;
				case BannerKind.Info:
					banner.Classes.Add("info");
					glyph.Data = (Geometry)this.FindResource("PlayerIconCheck")!;
					break;
				default:
					banner.Classes.Add("warning");
					glyph.Data = (Geometry)this.FindResource("PlayerIconWarning")!;
					break;
			}
		}

		//The classic buttons (OK/Yes first) in the renders' order: the safe one
		//first, the one that goes on last, as primary.
		private static void ArrangePlayerButtons(StackPanel panel, List<Button> classic)
		{
			panel.Children.Clear();
			foreach(int i in PlayerDialog.ButtonOrder(classic.Count)) {
				Button btn = classic[i];
				btn.Classes.Add(PlayerDialog.IsPrimary(i) ? "primary" : "secondary");
				btn.Classes.Add("regular");
				btn.Name = PlayerDialog.IsPrimary(i) ? "PlayerMsgPrimary" : null;
				panel.Children.Add(btn);
			}
		}
	}

	public enum MessageBoxButtons
	{
		OK,
		OKCancel,
		YesNo,
		YesNoCancel
	}

	public enum MessageBoxIcon
	{
		Error,
		Warning,
		Question,
		Info
	}

	public enum DialogResult
	{
		OK,
		Cancel,
		Yes,
		No
	}
}
