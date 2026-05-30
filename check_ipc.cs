using System;
using System.Reflection;
using System.Linq;

class Program
{
    static void Main()
    {
        try {
            var asm = Assembly.LoadFrom(@"C:\Users\USER154971\.nuget\packages\dotnetcampus.ipc\2.0.0-alpha436\lib\net6.0\dotnetCampus.Ipc.dll");
            var type = asm.GetType("dotnetCampus.Ipc.IpcRouteds.DirectRouteds.JsonIpcDirectRoutedProvider");
            if (type == null) {
                Console.WriteLine("Type not found. Trying to find it...");
                foreach (var t in asm.GetTypes().Where(t => t.Name.Contains("JsonIpc"))) {
                    Console.WriteLine("Found: " + t.FullName);
                }
                return;
            }
            Console.WriteLine("Type: " + type.FullName);
            foreach (var prop in type.GetProperties()) {
                Console.WriteLine("Prop: " + prop.Name + " Type: " + prop.PropertyType.Name);
            }
        } catch (ReflectionTypeLoadException ex) {
            foreach (var e in ex.LoaderExceptions) {
                Console.WriteLine("LoaderEx: " + e.Message);
            }
        } catch (Exception ex) {
            Console.WriteLine("Ex: " + ex.Message);
        }
    }
}
