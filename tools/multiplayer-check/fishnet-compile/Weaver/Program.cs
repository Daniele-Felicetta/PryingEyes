using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Text.Json;
using FishNet.CodeGenerating.ILCore;
using MonoFN.Cecil;
using Unity.CompilationPipeline.Common.Diagnostics;
using Unity.CompilationPipeline.Common.ILPostProcessing;

internal static class Program
{
    // This program executes the managed postprocessor, never gameplay, Unity Editor, or generated RPC methods.
    static int Main(string[] args)
    {
        if (args.Length != 4) { Console.Error.WriteLine("Usage: Weaver input.dll reference-directory engine-dll-directory output-directory"); return 2; }
        string engine = Path.GetFullPath(args[2]);
        AssemblyLoadContext.Default.Resolving += (_, name) =>
        {
            string path = Path.Combine(engine, name.Name + ".dll");
            return File.Exists(path) ? AssemblyLoadContext.Default.LoadFromAssemblyPath(path) : null;
        };
        return Process(args);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static int Process(string[] args)
    {
        string input = Path.GetFullPath(args[0]);
        string referenceDirectory = Path.GetFullPath(args[1]);
        string engine = Path.GetFullPath(args[2]);
        string output = Path.GetFullPath(args[3]);
        string cache = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../.dependencies"));
        if (!output.StartsWith(cache + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(output, Path.GetDirectoryName(input), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Write only to a separate directory inside the isolated dependency cache.");
        Directory.CreateDirectory(output);
        var references = Directory.GetFiles(referenceDirectory, "*.dll")
            .Concat(Directory.GetFiles(Path.GetDirectoryName(input), "*.dll"))
            .Concat(Directory.GetFiles(engine, "*.dll"))
            .Concat(Directory.GetFiles(Path.GetDirectoryName(typeof(object).Assembly.Location), "*.dll"))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var assembly = new CompiledAssembly(input, references);
        var processor = new FishNetILPP();
        if (!processor.WillProcess(assembly)) throw new InvalidOperationException("Postprocessor declined the assembly.");
        var result = processor.Process(assembly);
        if (result == null) throw new InvalidOperationException("Postprocessor returned no modified assembly.");
        foreach (var diagnostic in result.Diagnostics) Console.WriteLine(diagnostic.DiagnosticType + ": " + diagnostic.MessageData);
        if (result.Diagnostics.Any(d => d.DiagnosticType == DiagnosticType.Error)) return 1;
        if (result.InMemoryAssembly == null || result.InMemoryAssembly.PeData.Length == 0)
            throw new InvalidOperationException("Postprocessor returned no IL.");
        File.WriteAllBytes(Path.Combine(output, Path.GetFileName(input)), result.InMemoryAssembly.PeData);
        File.WriteAllBytes(Path.Combine(output, Path.GetFileNameWithoutExtension(input) + ".pdb"), result.InMemoryAssembly.PdbData);
        if (assembly.Name == "PryingEyes.Multiplayer.FishNetCheck") VerifyAdapter(result.InMemoryAssembly.PeData, output);
        Console.WriteLine("PASS: managed FishNet ILPP generated " + result.InMemoryAssembly.PeData.Length + " bytes for " + assembly.Name);
        return 0;
    }

    static void VerifyAdapter(byte[] bytes, string output)
    {
        using var assembly = AssemblyDefinition.ReadAssembly(new MemoryStream(bytes));
        var types = assembly.MainModule.Types;
        const string backend = "PryingEyes.Multiplayer.FishNetBackend.";
        var world = types.Single(t => t.FullName == backend + "FishNetWorldState");
        var avatar = types.Single(t => t.FullName == backend + "FishNetSeatAvatar");
        var objectives = types.Single(t => t.FullName == backend + "FishNetTowerObjectives");
        foreach (var check in new[] { (world, "FloorReadyServerRpc"), (avatar, "InputServerRpc") })
        {
            foreach (string prefix in new[] { "RpcWriter___", "RpcReader___", "RpcLogic___" })
                if (!check.Item1.Methods.Any(m => m.Name.StartsWith(prefix) && m.Name.Contains(check.Item2) && m.HasBody && m.Body.Instructions.Count > 0))
                    throw new InvalidOperationException("Missing generated RPC path: " + prefix + check.Item2);
            if (!check.Item1.Methods.Any(m => m.Name == "NetworkInitialize___Early" && m.HasBody && m.Body.Instructions.Count > 0))
                throw new InvalidOperationException("Missing generated SyncType initialization for " + check.Item1.Name);
        }
        foreach (var check in new[] { (world, "descriptor"), (world, "movementEnabled"), (avatar, "seatIndex"), (avatar, "pose"), (objectives, "state") })
        {
            var initializer = check.Item1.Methods.Single(m => m.Name == "NetworkInitialize___Early");
            if (!initializer.Body.Instructions.Any(i => i.Operand is FieldReference f && f.Name == check.Item2))
                throw new InvalidOperationException("Missing generated initialization for SyncVar " + check.Item2);
        }
        var inputReader = avatar.Methods.Single(m => m.Name.StartsWith("RpcReader___") && m.Name.Contains("InputServerRpc"));
        if (!inputReader.Body.Instructions.Any(i => i.Operand is MethodReference m && m.Name == "OwnerMatches"))
            throw new InvalidOperationException("Generated input RPC is missing the authenticated ownership guard.");
        var readinessReader = world.Methods.Single(m => m.Name.StartsWith("RpcReader___") && m.Name.Contains("FloorReadyServerRpc"));
        if (!readinessReader.Parameters.Any(p => p.ParameterType.FullName == "FishNet.Connection.NetworkConnection"))
            throw new InvalidOperationException("Generated readiness reader must receive its sender from FishNet.");
        var readers = types.Single(t => t.Name == "GeneratedReaders___Internal");
        var writers = types.Single(t => t.Name == "GeneratedWriters___Internal");
        string[] payloads = { "PryingEyes.Multiplayer.SeatCommand", backend + "FloorDescriptor", backend + "AvatarSnapshot",
            backend + "SeatAdmissionRequest", backend + "SeatAdmissionChallenge", backend + "TowerObjectiveSnapshot" };
        foreach (string payload in payloads)
        {
            if (!readers.Methods.Any(m => m.ReturnType.FullName == payload && m.HasBody && m.Body.Instructions.Count > 0) ||
                !writers.Methods.Any(m => m.Parameters.Any(p => p.ParameterType.FullName == payload) && m.HasBody && m.Body.Instructions.Count > 0))
                throw new InvalidOperationException("Missing generated reader/writer pair for " + payload);
        }
        var report = new
        {
            assembly = assembly.Name.Name,
            rpc = new[] { "FishNetWorldState.FloorReadyServerRpc", "FishNetSeatAvatar.InputServerRpc" },
            serializedPayloads = payloads,
            initializedSyncVars = new[] { "descriptor", "movementEnabled", "seatIndex", "pose", "state" },
            generatedInputOwnershipGuard = true,
            generatedReadinessSenderParameter = true,
            diagnostics = "No FishNet ILPP errors; generated IL inspected, not executed as gameplay.",
            runtimeVerified = false
        };
        File.WriteAllText(Path.Combine(output, "adapter-codegen-report.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine("PASS: 2 RPC writer/reader/logic paths, 5 SyncType initializations, 6 generated payload reader/writer pairs.");
    }

    sealed class CompiledAssembly : ICompiledAssembly
    {
        public InMemoryAssembly InMemoryAssembly { get; }
        public string Name { get; }
        public string[] References { get; }
        public string[] Defines { get; } = { "UNITY_2020_1_OR_NEWER", "UNITY_2022_1_OR_NEWER", "UNITY_6000_5_OR_NEWER", "PRYINGEYES_FISHNET" };
        public CompiledAssembly(string path, string[] references)
        {
            Name = Path.GetFileNameWithoutExtension(path); References = references;
            InMemoryAssembly = new InMemoryAssembly(File.ReadAllBytes(path), File.ReadAllBytes(Path.ChangeExtension(path, ".pdb")));
        }
    }
}
