namespace Ome.Engine.Hosting;

public interface IHost : IDiagnosticHost, ILifecycleHost, IAudioHost, IMovieHost, IGraphicsHost, IInputHost,
                         IAdvHost
{
}
