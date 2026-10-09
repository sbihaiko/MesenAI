using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Mesen.Logic;

//#1111 (spec #1102, PRD Part B §13.3): Settings › Display › Interface size.
//ADR-0269: a size this version does not know reads as Standard (see
//InterfaceSizeJsonConverter), so the setting never fails the whole file.
public enum InterfaceSize { Standard, Large, ExtraLarge }

//The factor scales Play's chrome (MainWindow's PlayChromeRoot) and nothing
//else: not the emulated picture and not the Scale row, which keep their own
//meaning. Pure, so the rule is pinned host-free (UI.Tests/Play/InterfaceSizeTests).
public static class PlayerInterfaceSize
{
	public static double Factor(InterfaceSize size) => size switch {
		InterfaceSize.Large => 1.25,
		InterfaceSize.ExtraLarge => 1.5,
		_ => 1.0
	};

	//One step, stopping at both ends (no wrap), like the pad's other rows.
	public static InterfaceSize Step(InterfaceSize size, int delta)
	{
		return (InterfaceSize)Math.Clamp((int)size + delta, (int)InterfaceSize.Standard, (int)InterfaceSize.ExtraLarge);
	}
}

//A value a later version wrote (a new name or number) must not throw: the
//settings file would be rebuilt from defaults and every other setting lost.
public sealed class InterfaceSizeJsonConverter : JsonConverter<InterfaceSize>
{
	public override InterfaceSize Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
	{
		InterfaceSize size = InterfaceSize.Standard;
		if(reader.TokenType == JsonTokenType.String) {
			if(Enum.TryParse(reader.GetString(), true, out InterfaceSize parsed) && Enum.IsDefined(parsed)) {
				size = parsed;
			}
		} else if(reader.TokenType == JsonTokenType.Number) {
			if(reader.TryGetInt32(out int number) && Enum.IsDefined(typeof(InterfaceSize), number)) {
				size = (InterfaceSize)number;
			}
		} else {
			//Objects and arrays are skipped whole so the reader stays in step.
			reader.Skip();
		}
		return size;
	}

	public override void Write(Utf8JsonWriter writer, InterfaceSize value, JsonSerializerOptions options)
	{
		writer.WriteStringValue(value.ToString());
	}
}
