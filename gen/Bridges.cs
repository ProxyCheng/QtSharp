using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

// Bridges: the places where the binding deliberately departs from its general rules so a .NET
// capability can sit behind a Qt type. Each bridge is one small class that says which Qt classes
// it applies to, which virtuals it publishes, and how it maps special parameters. The extractor
// asks the registry; a bridge joins it by carrying [VirtualBridge] - there is no list to edit.
//
// Today there is one (QIODevice, so a C# Stream can be a Qt device). The likely next ones are more
// QIODevice-shaped than general: QAbstractItemModel/QStyledItemDelegate are already covered by the
// ordinary virtual machinery, while QNetworkAccessManager or QSqlDriver would each be a large
// protocol bridge of their own rather than a table row.
internal interface IVirtualBridge
{
    /// <summary>Whether this bridge owns the given Qt class.</summary>
    bool AppliesTo(string className);

    /// <summary>Whether a virtual of that class is published as an override point.</summary>
    bool Publishes(string className, string name);

    /// <summary>
    /// Whether the published virtuals apply to subclasses too. A bridge that only needs to wrap
    /// the class itself returns false: its subclasses call their own members straight into Qt, and
    /// a native override installed on them would dispatch back into the managed call that started
    /// it (QIODevice's subclasses are exactly that case).
    /// </summary>
    bool PropagatesToSubclasses { get; }

    /// <summary>Rewrites the virtual's parameters for the managed side (buffers, handles).</summary>
    void MapParameters(ApiModel.FnFact fn, string declaringClass);
}

/// <summary>Marks an <see cref="IVirtualBridge"/> the registry discovers on startup.</summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
internal sealed class VirtualBridgeAttribute : Attribute
{
}

internal static class VirtualBridges
{
    // Discovered from the generator's own assembly, so adding a bridge is adding a class with the
    // attribute. Sorted by type name: GetTypes() makes no ordering promise, and a stable order
    // keeps two runs (and two machines) identical.
    static readonly IVirtualBridge[] All = typeof(VirtualBridges).Assembly
        .GetTypes()
        .Where(type => !type.IsAbstract
                       && typeof(IVirtualBridge).IsAssignableFrom(type)
                       && type.GetCustomAttribute<VirtualBridgeAttribute>(inherit: false) != null)
        .OrderBy(type => type.FullName, StringComparer.Ordinal)
        .Select(type => (IVirtualBridge)Activator.CreateInstance(type, nonPublic: true))
        .ToArray();

    public static bool Publishes(string className, string name)
    {
        foreach (var bridge in All)
            if (bridge.AppliesTo(className) && bridge.Publishes(className, name))
                return true;
        return false;
    }

    /// <summary>Whether a bridge class's published virtuals may appear in a subclass's chain.</summary>
    public static bool PropagatesToSubclasses(string className)
    {
        foreach (var bridge in All)
            if (bridge.AppliesTo(className))
                return bridge.PropagatesToSubclasses;
        return true;
    }

    public static void MapParameters(ApiModel.FnFact fn, string declaringClass)
    {
        foreach (var bridge in All)
            if (bridge.AppliesTo(declaringClass))
                bridge.MapParameters(fn, declaringClass);
    }
}
