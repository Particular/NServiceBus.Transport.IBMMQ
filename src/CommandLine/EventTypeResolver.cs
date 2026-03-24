namespace NServiceBus.Transport.IBMMQ.CommandLine;

using System.Reflection;
using System.Runtime.InteropServices;

static class EventTypeResolver
{
    public static IReadOnlyList<string> Resolve(string eventTypeName, string assemblyPath)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(assemblyPath))!;
        var runtimeAssemblies = Directory.GetFiles(RuntimeEnvironment.GetRuntimeDirectory(), "*.dll");
        var localAssemblies = Directory.GetFiles(directory, "*.dll");

        var resolver = new PathAssemblyResolver(runtimeAssemblies.Concat(localAssemblies));
        using var mlc = new MetadataLoadContext(resolver);

        var assembly = mlc.LoadFromAssemblyPath(Path.GetFullPath(assemblyPath));

        var eventType = assembly.GetType(eventTypeName)
            ?? throw new InvalidOperationException(
                $"Type '{eventTypeName}' not found in assembly '{Path.GetFileName(assemblyPath)}'.");

        var targetFullName = eventType.FullName!;
        var names = new List<string> { targetFullName };

        foreach (var type in assembly.GetTypes())
        {
            if (type.FullName != targetFullName && IsAssignableTo(type, targetFullName))
            {
                names.Add(type.FullName!);
            }
        }

        return names;
    }

    static bool IsAssignableTo(Type type, string targetFullName)
    {
        // Walk the base class chain
        try
        {
            var current = type.BaseType;
            while (current is not null)
            {
                if (current.FullName == targetFullName)
                {
                    return true;
                }

                current = current.BaseType;
            }
        }
        catch (FileNotFoundException)
        {
            // Referenced assembly not available in MetadataLoadContext — skip base chain
        }

        // Check interfaces
        try
        {
            foreach (var iface in type.GetInterfaces())
            {
                if (iface.FullName == targetFullName)
                {
                    return true;
                }
            }
        }
        catch (FileNotFoundException)
        {
            // Referenced assembly not available in MetadataLoadContext — skip interfaces
        }

        return false;
    }
}
