using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

var interop = args[0];
var assemblies = Directory.GetFiles(interop, "*.dll")
    .Concat(Directory.GetFiles(Path.GetDirectoryName(typeof(object).Assembly.Location)!, "*.dll"))
    .ToArray();
var resolver = new PathAssemblyResolver(assemblies);
using var mlc = new MetadataLoadContext(resolver);
var names = new SortedSet<string>(StringComparer.Ordinal);
foreach (var path in assemblies)
{
    Assembly asm;
    try { asm = mlc.LoadFromAssemblyPath(path); }
    catch { continue; }
    Type[] types;
    try { types = asm.GetTypes(); }
    catch (ReflectionTypeLoadException ex) { types = ex.Types.Where(t => t != null).Cast<Type>().ToArray(); }
    foreach (var t in types)
    {
        try
        {
            if (!t.IsEnum) continue;
            foreach (var f in t.GetFields(BindingFlags.Public | BindingFlags.Static))
                names.Add(t.FullName + "." + f.Name);
        }
        catch { }
    }
}
File.WriteAllLines(args[1], names);
Console.WriteLine(names.Count);
