using System.Reflection;
using callback.CBFSConnect;

var cbfsType = typeof(CBFS);

Console.WriteLine("=== CBFS CONSTANTS (Static Fields) ===");
foreach (var f in cbfsType.GetFields(BindingFlags.Public | BindingFlags.Static))
    Console.WriteLine($"  {f.FieldType.Name} {f.Name} = {f.GetValue(null)}");

Console.WriteLine("\n=== CBFS EVENTS ===");
foreach (var e in cbfsType.GetEvents())
    Console.WriteLine($"  {e.Name}");

Console.WriteLine("\n=== CBFS PUBLIC METHODS ===");
foreach (var m in cbfsType.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
    Console.WriteLine($"  {m.ReturnType.Name} {m.Name}({string.Join(", ", m.GetParameters().Select(p => $"{p.ParameterType.Name} {p.Name}"))})");

Console.WriteLine("\n=== CBFSGetVolumeSizeEventArgs Properties ===");
var volType = typeof(CBFSGetVolumeSizeEventArgs);
foreach (var p in volType.GetProperties())
    Console.WriteLine($"  {p.PropertyType.Name} {p.Name}");

Console.WriteLine("\n=== CBFSReadFileEventArgs Properties ===");
var readType = typeof(CBFSReadFileEventArgs);
foreach (var p in readType.GetProperties())
    Console.WriteLine($"  {p.PropertyType.Name} {p.Name}");

Console.WriteLine("\n=== CBFSEnumerateDirectoryEventArgs Properties ===");
var enumType = typeof(CBFSEnumerateDirectoryEventArgs);
foreach (var p in enumType.GetProperties())
    Console.WriteLine($"  {p.PropertyType.Name} {p.Name}");
