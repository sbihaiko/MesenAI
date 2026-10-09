using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Mesen.Logic;

namespace Mesen.Controls
{
	//ADR-0249: the Player theme's attached properties. UI/Styles/PlayerTheme.axaml
	//sets them from the workspace class on a scope (`.player` is Play's tint,
	//`.remaster`/`.share` override it) and every tinted component - primary
	//and tinted buttons, icon badges - binds to them, so one component serves
	//all three workspaces. Both inherit down the tree.
	public class PlayerTheme : AvaloniaObject
	{
		public static readonly AttachedProperty<IBrush?> TintProperty =
			AvaloniaProperty.RegisterAttached<PlayerTheme, AvaloniaObject, IBrush?>("Tint", inherits: true);

		//The tint at 14 % over white: the fill of a `tinted` button.
		public static readonly AttachedProperty<IBrush?> TintSoftProperty =
			AvaloniaProperty.RegisterAttached<PlayerTheme, AvaloniaObject, IBrush?>("TintSoft", inherits: true);

		//The tint darkened for text on a light surface (TINT_TEXT).
		public static readonly AttachedProperty<IBrush?> TintTextProperty =
			AvaloniaProperty.RegisterAttached<PlayerTheme, AvaloniaObject, IBrush?>("TintText", inherits: true);

		public static IBrush? GetTint(AvaloniaObject target) => target.GetValue(TintProperty);
		public static void SetTint(AvaloniaObject target, IBrush? value) => target.SetValue(TintProperty, value);
		public static IBrush? GetTintSoft(AvaloniaObject target) => target.GetValue(TintSoftProperty);
		public static void SetTintSoft(AvaloniaObject target, IBrush? value) => target.SetValue(TintSoftProperty, value);
		public static IBrush? GetTintText(AvaloniaObject target) => target.GetValue(TintTextProperty);
		public static void SetTintText(AvaloniaObject target, IBrush? value) => target.SetValue(TintTextProperty, value);
	}

	//#1111: Preferences.InterfaceSize -> the layout transform at the root of
	//Play's chrome (a LayoutTransformControl, so the chrome re-lays out at the
	//new size instead of being stretched). The game picture is not under it.
	public class InterfaceSizeTransformConverter : IValueConverter
	{
		public static readonly InterfaceSizeTransformConverter Instance = new();

		public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
		{
			double factor = PlayerInterfaceSize.Factor(value is InterfaceSize size ? size : InterfaceSize.Standard);
			return new ScaleTransform(factor, factor);
		}

		public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
		{
			throw new NotSupportedException();
		}
	}

	//Workspace -> its badge glyph (PlayerIconPlay / PlayerIconRemaster /
	//PlayerIconShare from PlayerTheme.axaml). Used by the shell bar's badge.
	public class WorkspaceIconConverter : IValueConverter
	{
		public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
		{
			string key = value switch {
				Workspace.Remaster => "PlayerIconRemaster",
				Workspace.Share => "PlayerIconShare",
				Workspace.Classic => "PlayerIconSettings",
				_ => "PlayerIconPlay",
			};
			return Application.Current?.FindResource(key);
		}

		public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
		{
			throw new NotSupportedException();
		}
	}
}
