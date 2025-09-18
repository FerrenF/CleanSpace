using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using VRage.Plugins;

namespace CleanSpaceTorch.Util
{
    public class ServerUtil
    {
        public static bool IsValidPlugin(Assembly assembly)
        {
            if (assembly.FullName.Contains("Sandbox.Game, Version")) return false;
            Type[] types;

            try { types = assembly.GetTypes(); }
            catch (ReflectionTypeLoadException ex) { types = ex.Types.Where(t => t != null).ToArray(); }
            catch { return false; }
            return types.Any(t => typeof(IPlugin).IsAssignableFrom(t) && t.IsClass && !t.IsAbstract);
        }

        public static bool IsValidPlugin(string dllPath)
        {
            if (string.IsNullOrWhiteSpace(dllPath) || !File.Exists(dllPath))
                return false;
            try {
                var assembly = Assembly.LoadFile(dllPath);
                return IsValidPlugin(assembly);
            } catch {              
                return false;
            }
        }
    }
}
