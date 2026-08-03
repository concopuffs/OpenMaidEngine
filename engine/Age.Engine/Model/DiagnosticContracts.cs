namespace Age.Engine.Model;

/// <summary>A synchronous AGE-owned diagnostic prompt after native body/context formatting.</summary>
public readonly record struct DiagnosticMessage(string Caption, string Text);
