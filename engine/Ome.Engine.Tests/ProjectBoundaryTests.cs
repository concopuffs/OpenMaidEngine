using System.Reflection;
using System.Xml.Linq;
using Ome.Engine.Hosting;
using Ome.Engine.Model;
using Ome.Engine.Sys4;
using Ome.Engine.Text;
using Ome.Engine.Vm;
using Xunit;

/// <summary>
/// The three engine projects (docs/superpowers/specs/2026-11-12-engine-layering-design.md §3.3): handlers in
/// <c>Ome.Engine.Vm</c>, natives in <c>Ome.Engine</c>, device interfaces in <c>Ome.Engine.Devices</c>. Calls go one
/// way, and handler code cannot name a device type. The build enforces most of it; these tests are the backstop.
/// </summary>
public class ProjectBoundaryTests
{
    private static readonly Assembly Handlers = typeof(VirtualMachine).Assembly;
    private static readonly Assembly Natives = typeof(IHost).Assembly;
    private static readonly Assembly Devices = typeof(RgbaImage).Assembly;

    [Fact]
    public void EachLayerIsItsOwnAssembly()
    {
        Assert.Equal("Ome.Engine.Vm", Handlers.GetName().Name);
        Assert.Equal("Ome.Engine", Natives.GetName().Name);
        Assert.Equal("Ome.Engine.Devices", Devices.GetName().Name);
    }

    [Theory]
    [InlineData(typeof(RgbaImage))]
    [InlineData(typeof(GlyphMask))]
    [InlineData(typeof(GlyphRasterRequest))]
    [InlineData(typeof(IGlyphMaskRasterizer))]
    [InlineData(typeof(IFontFamilySource))]
    [InlineData(typeof(EnumeratedFontFamily))]
    [InlineData(typeof(IMovieDecoder))]
    [InlineData(typeof(IMovieDecoderFactory))]
    public void DeviceTypesLiveInTheDeviceAssembly(Type type) => Assert.Same(Devices, type.Assembly);

    [Fact]
    public void TheHandlerAssemblyDoesNotReferenceTheDeviceAssembly()
    {
        string[] references = Handlers.GetReferencedAssemblies().Select(name => name.Name!).ToArray();
        Assert.Contains("Ome.Engine", references);
        Assert.DoesNotContain("Ome.Engine.Devices", references);
    }

    [Fact]
    public void CallsGoOneWay()
    {
        Assert.DoesNotContain("Ome.Engine.Vm", Natives.GetReferencedAssemblies().Select(name => name.Name));
        Assert.DoesNotContain(Devices.GetReferencedAssemblies(), name => name.Name!.StartsWith("Ome.", StringComparison.Ordinal));
    }

    [Fact]
    public void TheProjectsReferenceOnlyTheLayerBelow()
    {
        Assert.Equal(["Ome.Engine"], ProjectReferences("Ome.Engine.Vm"));
        Assert.Equal(["Ome.Engine.Devices"], ProjectReferences("Ome.Engine"));
        Assert.Empty(ProjectReferences("Ome.Engine.Devices"));
        // Without it, Ome.Engine.Vm would see Ome.Engine.Devices through Ome.Engine.
        Assert.Equal("true", Property("Ome.Engine.Vm", "DisableTransitiveProjectReferences"));
    }

    [Fact]
    public void TheVmsHostInterfacesNameNoDeviceType()
    {
        // A native interface member that carries a device type cannot be called from handler code (CS0012), so the
        // host the VM is given must not have one (spec §3.3; ISurfacePixels holds the two that did).
        var offenders = new List<string>();
        foreach (Type contract in typeof(IHost).GetInterfaces().Append(typeof(IHost)))
        foreach (MemberInfo member in contract.GetMembers())
        foreach (Type used in Signature(member).SelectMany(Expand))
            if (used.Assembly == Devices)
                offenders.Add($"{contract.Name}.{member.Name}: {used.Name}");
        Assert.Empty(offenders);
    }

    private static IEnumerable<Type> Signature(MemberInfo member) => member switch
    {
        MethodInfo method => method.GetParameters().Select(parameter => parameter.ParameterType).Append(method.ReturnType),
        PropertyInfo property => [property.PropertyType],
        EventInfo { EventHandlerType: { } handler } => [handler],
        _ => [],
    };

    private static IEnumerable<Type> Expand(Type type)
    {
        yield return type;
        if (type.HasElementType)
            foreach (Type inner in Expand(type.GetElementType()!)) yield return inner;
        if (type.IsGenericType)
            foreach (Type argument in type.GetGenericArguments())
            foreach (Type inner in Expand(argument)) yield return inner;
    }

    private static XDocument Project(string name)
        => XDocument.Load(Path.Combine(Paths.Repo, "engine", name, name + ".csproj"));

    private static string[] ProjectReferences(string name)
        => Project(name).Descendants("ProjectReference")
            .Select(reference => Path.GetFileNameWithoutExtension(reference.Attribute("Include")!.Value.Replace('\\', '/')))
            .Order(StringComparer.Ordinal)
            .ToArray();

    private static string? Property(string name, string property)
        => Project(name).Descendants(property).SingleOrDefault()?.Value;
}
