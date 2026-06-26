using System.Reflection;

var dllPath = @"C:\Users\Hossein\.nuget\packages\callback.cbfsconnect\24.0.9258\runtimes\any\lib\net6.0\callback.CBFSConnect.dll";
var asm = Assembly.LoadFrom(dllPath);

// List all types
Console.WriteLine("=== TYPES ===");
foreach (var t in asm.GetExportedTypes())
    Console.WriteLine(t.FullName);

// Check CBFS class members
Console.WriteLine("\n=== CBFS CONSTANTS (Fields) ===");
var cbfsType = asm.GetExportedTypes().FirstOrDefault(t => t.Name == "CBFS");
if (cbfsType != null)
{
    foreach (var f in cbfsType.GetFields(BindingFlags.Public | BindingFlags.Static))
        Console.WriteLine($"  {f.Name} = {f.GetValue(null)}");

    Console.WriteLine("\n=== CBFS EVENTS ===");
    foreach (var e in cbfsType.GetEvents())
        Console.WriteLine($"  {e.Name}");

    Console.WriteLine("\n=== CBFS PUBLIC METHODS ===");
    foreach (var m in cbfsType.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
        Console.WriteLine($"  {m.Name}({string.Join(", ", m.GetParameters().Select(p => $"{p.ParameterType.Name} {p.Name}"))})");
}

// Check GetVolumeSizeEventArgs
Console.WriteLine("\n=== CBFSGetVolumeSizeEventArgs Properties ===");
var volType = asm.GetExportedTypes().FirstOrDefault(t => t.Name == "CBFSGetVolumeSizeEventArgs");
if (volType != null)
    foreach (var p in volType.GetProperties())
        Console.WriteLine($"  {p.PropertyType.Name} {p.Name}");
