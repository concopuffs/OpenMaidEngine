namespace Ome.Engine.Hosting;

public interface IHost : IDiagnosticHost, ILifecycleHost, IMovieHost, IGraphicsHost, IInputHost,
                         IAdvHost
{
}
