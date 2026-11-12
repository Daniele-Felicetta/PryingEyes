using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;

internal static class Program
{
    // Separate process: load the woven SDK and adapter, execute only managed data serializers.
    // No Unity components, network connections, game callbacks or Editor APIs are instantiated/called.
    static int Main(string[] args)
    {
        if (args.Length != 4) { Console.Error.WriteLine("Usage: Packets woven-adapter.dll woven-runtime.dll reference-directory engine-directory"); return 2; }
        string adapterPath = Path.GetFullPath(args[0]), runtimePath = Path.GetFullPath(args[1]);
        string references = Path.GetFullPath(args[2]), engine = Path.GetFullPath(args[3]);
        AssemblyLoadContext.Default.Resolving += (_, name) =>
        {
            string path = name.Name == "FishNet.Runtime" ? runtimePath : Path.Combine(references, name.Name + ".dll");
            if (!File.Exists(path)) path = Path.Combine(engine, name.Name + ".dll");
            return File.Exists(path) ? AssemblyLoadContext.Default.LoadFromAssemblyPath(path) : null;
        };
        var runtime = AssemblyLoadContext.Default.LoadFromAssemblyPath(runtimePath);
        var adapter = AssemblyLoadContext.Default.LoadFromAssemblyPath(adapterPath);
        // Unity normally invokes these generated RuntimeInitializeOnLoad methods.
        // Register actual SDK delegates explicitly; do not replace readers/writers or emulate native engine state.
        foreach (var assembly in new[] { runtime, adapter })
        foreach (string kind in new[] { "Readers", "Writers" })
            assembly.GetType("FishNet.Serializing.Generated.Generated" + kind + "___Internal", true)
                .GetMethod("InitializeOnce", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, null);
        var readers = adapter.GetType("FishNet.Serializing.Generated.GeneratedReaders___Internal", true);
        var writers = adapter.GetType("FishNet.Serializing.Generated.GeneratedWriters___Internal", true);
        string[] names = { "SeatCommand", "FloorDescriptor", "AvatarSnapshot", "SeatAdmissionRequest", "SeatAdmissionChallenge", "TowerObjectiveSnapshot" };
        int packets = 0;
        foreach (string name in names)
        {
            var read = readers.GetMethods().Single(m => m.ReturnType.Name == name);
            Type payloadType = read.ReturnType;
            var write = writers.GetMethods().Single(m => m.GetParameters().Length == 2 && m.GetParameters()[1].ParameterType == payloadType);
            for (int sample = 0; sample < 2; sample++)
            {
                object expected = Activator.CreateInstance(payloadType);
                if (sample == 1) Fill(expected);
                object writer = Activator.CreateInstance(runtime.GetType("FishNet.Serializing.PooledWriter", true));
                write.Invoke(null, new[] { writer, expected });
                var segment = (ArraySegment<byte>)writer.GetType().GetMethod("GetArraySegment").Invoke(writer, null);
                object reader = Activator.CreateInstance(runtime.GetType("FishNet.Serializing.PooledReader", true));
                var initialize = reader.GetType().GetMethods().Single(m => m.Name == "Initialize" && m.GetParameters().Length == 4 &&
                    m.GetParameters()[0].ParameterType == typeof(ArraySegment<byte>));
                var source = Enum.ToObject(initialize.GetParameters()[3].ParameterType, 0);
                initialize.Invoke(reader, new[] { segment, null, null, source });
                object actual = read.Invoke(null, new[] { reader });
                AssertEqual(expected, actual, name);
                int remaining = (int)reader.GetType().GetProperty("Remaining").GetValue(reader);
                if (remaining != 0) throw new InvalidOperationException(name + " reader did not consume the exact payload.");
                packets++;
                Console.WriteLine("PASS: " + name + " sample " + sample + " round-trip (" + segment.Count + " bytes).");
            }
        }
        Console.WriteLine("PASS: " + packets + " generated serializer round-trips; no Unity gameplay or transport executed.");
        return 0;
    }

    static void Fill(object value)
    {
        foreach (var field in value.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance))
        {
            object data;
            if (field.FieldType == typeof(int)) data = field.Name == "RunSeed" ? int.MinValue : 42;
            else if (field.FieldType == typeof(uint)) data = uint.MaxValue;
            else if (field.FieldType == typeof(float)) data = -.375f;
            else if (field.FieldType == typeof(bool)) data = true;
            else if (field.FieldType == typeof(string)) data = new string('a', 64);
            else if (field.FieldType == typeof(byte[])) data = Enumerable.Range(0, 66).Select(i => (byte)i).ToArray();
            else if (field.FieldType == typeof(bool[])) data = new[] { false, true, false, true, true };
            else if (field.FieldType.FullName == "UnityEngine.Vector3")
                data = Activator.CreateInstance(field.FieldType, new object[] { 1.25f, 2.5f, -3.75f });
            else throw new InvalidOperationException("Add an explicit safe fixture for " + field.FieldType.FullName);
            field.SetValue(value, data);
        }
    }

    static void AssertEqual(object expected, object actual, string path)
    {
        if (expected == null || actual == null)
        {
            if (expected != actual) throw new InvalidOperationException("Null mismatch: " + path);
            return;
        }
        Type type = expected.GetType();
        if (type != actual.GetType()) throw new InvalidOperationException("Type mismatch: " + path);
        if (type.IsPrimitive || type == typeof(string))
        {
            if (!expected.Equals(actual)) throw new InvalidOperationException("Value mismatch: " + path);
            return;
        }
        if (expected is byte[] bytes)
        {
            if (!bytes.SequenceEqual((byte[])actual)) throw new InvalidOperationException("Bytes mismatch: " + path);
            return;
        }
        if (expected is bool[] flags)
        {
            if (!flags.SequenceEqual((bool[])actual)) throw new InvalidOperationException("Flags mismatch: " + path);
            return;
        }
        foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
            AssertEqual(field.GetValue(expected), field.GetValue(actual), path + "." + field.Name);
    }
}
