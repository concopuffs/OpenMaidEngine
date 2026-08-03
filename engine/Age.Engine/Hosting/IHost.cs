namespace Age.Engine.Hosting;

/// <summary>A synchronous AGE-owned diagnostic prompt after native body/context formatting.</summary>
public readonly record struct DiagnosticMessage(string Caption, string Text);

public interface IHost : IDiagnosticHost, ILifecycleHost, IAudioHost, IMovieHost, IGraphicsHost, IInputHost,
                         IAdvHost
{
}
