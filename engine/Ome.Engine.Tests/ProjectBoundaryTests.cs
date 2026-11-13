using System.Reflection;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Ome.Engine.Hosting;
using Ome.Engine.Devices;
using Ome.Engine.Model;
using Ome.Engine.Natives;
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
    [InlineData(typeof(ILanguageFontRasterizer))]
    [InlineData(typeof(IFontFamilySource))]
    [InlineData(typeof(EnumeratedFontFamily))]
    [InlineData(typeof(IMovieDecoder))]
    [InlineData(typeof(IMovieDecoderFactory))]
    [InlineData(typeof(IPaceClock))]
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

    [Fact]
    public void NoGodotTypeImplementsTheHost()
    {
        // Milestone 4 lifted the host into Ome.Engine (EngineHost); godot/ keeps the front end and device
        // implementations, and reaches the host only through its class. Main implements IFrontEnd, which shows the
        // scan reads base lists.
        string[] host = typeof(IHost).GetInterfaces().Append(typeof(IHost)).Append(typeof(ISurfacePixels))
            .Select(type => type.Name).ToArray();
        var declaration = new Regex(@"\b(?:class|record|struct)\s+(\w+)[^{;=]*?:\s*([^{;]+?)(?:\bwhere\b|\{|;)");
        var bases = new List<(string Type, string Base)>();
        foreach (string path in Directory.EnumerateFiles(Path.Combine(Paths.Repo, "godot"), "*.cs", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(Paths.Repo, path).Replace('\\', '/');
            if (relative.Split('/').Any(part => part is "obj" or "bin" or ".godot")) continue;
            foreach (Match match in declaration.Matches(File.ReadAllText(path)))
            foreach (Match name in Regex.Matches(match.Groups[2].Value, @"[A-Za-z_][\w.]*"))
                bases.Add((match.Groups[1].Value, name.Value.Split('.')[^1]));
        }
        Assert.Contains(("Main", nameof(IFrontEnd)), bases);
        Assert.Empty(bases.Where(pair => host.Contains(pair.Base)));
    }

    [Fact]
    public void TheVmTakesNoPaceClock()
    {
        // Milestone 5: fast-forward pacing belongs to the skip-and-auto native, which composition builds with a clock.
        IEnumerable<Type> parameters = typeof(VirtualMachine).GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .SelectMany(constructor => constructor.GetParameters()).Select(parameter => parameter.ParameterType);
        Assert.DoesNotContain(typeof(IPaceClock), parameters);
        Assert.Contains(typeof(SkipAndAuto), parameters);
        Assert.Contains(typeof(IPaceClock), typeof(SkipAndAuto).GetConstructors().SelectMany(c => c.GetParameters()).Select(p => p.ParameterType));
    }

    [Fact]
    public void SkipAndAutoStateIsPrivateToItsNative()
    {
        // The census (docs/superpowers/plans/2026-11-12-engine-layering.md §4, "Skip and auto") found this state in the
        // VM and the host; milestone 5 gave it one owner, and neither keeps a copy.
        const BindingFlags instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        Assert.Empty(typeof(SkipAndAuto).GetFields(instance).Where(field => !field.IsPrivate).Select(field => field.Name));
        string[] census =
        [
            "_paceClock", "_fastForwardPacing", "_fastForwardOwedMs", "_fastForwardStartMs", "_fastForwardPagePassed",
            "_advSkipServiceEnabled", "_physicalFastForwardLock", "_physicalSkipActionHeld", "_revealPhysicalSkipSuppressed",
            "_skipClickCancelState", "_skipClickCancelTriggers", "_autoMessageEnabled", "_autoMessageTime0Ms",
            "_autoMessageTime1Ms", "_autoVoicePending", "_messageSkipEnabled", "_messageSkipServiceActive",
            "_messageSkipInjected", "_advReadSkipState", "_autoMessageTime0Milliseconds", "_autoMessageTime1Milliseconds",
            "_messageSkipLock", "_scriptMessageSkipActive", "_physicalMessageSkipActive", "_messageSkipActive",
        ];
        foreach (Type keeper in new[] { typeof(VirtualMachine), typeof(EngineHost) })
            Assert.Empty(keeper.GetFields(instance).Select(field => field.Name).Intersect(census));
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
