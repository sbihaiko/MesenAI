using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Mesen.Config;
using Mesen.ViewModels;
using System;
using System.IO;

namespace Mesen.Windows;

public class ShaderConfigWindow : MesenWindow
{
	public ShaderConfigWindow() : this(false)
	{
	}

	//playerLook (ADR-0249, UI/Logic/PlayerDialog): Look's Adjust… from Player
	//Settings - the theme's sheet, Cancel before OK. Same parameters and buttons.
	public ShaderConfigWindow(bool playerLook)
	{
		InitializeComponent();
		if(playerLook) {
			this.GetControl<Border>("ShaderConfigRoot").Classes.Add("player");
			StackPanel actions = this.GetControl<StackPanel>("ShaderConfigActions");
			Button ok = this.GetControl<Button>("ShaderConfigOk");
			actions.Children.Remove(ok);
			actions.Children.Add(ok);
			actions.Spacing = 10;
			Width = 420;
			Height = 520;
		}
	}

	private void InitializeComponent()
	{
		AvaloniaXamlLoader.Load(this);
	}

	protected override void OnDataContextChanged(EventArgs e)
	{
		base.OnDataContextChanged(e);
		if(DataContext is ShaderConfigViewModel model) {
			Title += ": " + Path.GetFileNameWithoutExtension(model.Config.ShaderFile);
			this.GetControl<TextBlock>("ShaderConfigPlayerTitle").Text = Path.GetFileName(model.Config.ShaderFile);
		}
	}

	protected override void OnClosed(EventArgs e)
	{
		base.OnClosed(e);
		ConfigManager.Config.Video.ApplyConfig();
	}

	private void Ok_OnClick(object sender, RoutedEventArgs e)
	{
		(DataContext as ShaderConfigViewModel)?.Save();
		Close();
	}

	private void Reset_OnClick(object sender, RoutedEventArgs e)
	{
		(DataContext as ShaderConfigViewModel)?.ResetToDefaults();
	}

	private void Cancel_OnClick(object sender, RoutedEventArgs e)
	{
		Close();
	}
}
