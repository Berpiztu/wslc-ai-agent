namespace Berpiztu.Dashboard.Fields;

/// <summary>One value a choice offers, and how it reads: fixed (ChoiceField) or read from a list (ListField).</summary>
public sealed record FieldOption<T>(T Value, string Text);
