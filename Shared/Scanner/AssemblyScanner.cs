using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using VRage.Plugins;

namespace CleanSpaceShared.Scanner
{    
    internal sealed class AssemblyScanner
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static byte[] ClientProviderMethod1_ILBytes(params object[] args)
        {
            if (!IsInValidAppDomain())
                throw new InvalidOperationException();

            MethodIdentifier m = ProtoUtil.Deserialize<MethodIdentifier>((byte[])args[0]);
            Type t = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType(m.FullName, throwOnError: false)).FirstOrDefault(x => x != null);

            if (t == null)
                throw new InvalidOperationException($"E55: Attestation failed.");

            MethodBase method = t.GetMethod(m.MethodName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);

            if (method == null)
                throw new InvalidOperationException($"E56: Attestation failed.");

            return ILAttester.GetMethodIlBytes(method);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static bool IsInValidAppDomain()
        {
            try
            {
                var found = AppDomain.CurrentDomain.GetAssemblies()
                    .Any(a => a.FullName?.IndexOf("Sandbox.Game", StringComparison.OrdinalIgnoreCase) >= 0
                           || a.FullName?.IndexOf("Sandbox.Engine", StringComparison.OrdinalIgnoreCase) >= 0);
                if (found) return true;

                var t = Type.GetType("Sandbox.Game.World.MySession, Sandbox.Game", throwOnError: false);
                if (t != null) return true;

                var t2 = Type.GetType("CleanSpaceTorch.CleanSpaceTorchPlugin, CleanSpace", throwOnError: false);
                if (t2 != null) return true;

                var procName = System.Diagnostics.Process.GetCurrentProcess().ProcessName;
                if (procName.IndexOf("SpaceEngineers", StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;

                var thisAsm = Assembly.GetExecutingAssembly();
                var thisName = thisAsm.GetName().Name;
                var duplicates = AppDomain.CurrentDomain.GetAssemblies()
                    .Where(a =>
                    {
                        try { return string.Equals(a.GetName().Name, thisName, StringComparison.OrdinalIgnoreCase); }
                        catch { return false; }
                    })
                    .ToList();

                if (duplicates.Count > 1)
                {
                    return false;
                }
            }
            catch
            {
            }

            return false;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static List<Assembly> GetPluginAssemblies()
        {
            List<Assembly> assemblies = new List<Assembly>();
            return AppDomain.CurrentDomain.GetAssemblies()
                .Where((asm)=> IsValidPlugin(asm)).ToList();
        }

        internal struct PluginAssemblyInfo
        {
            public string Name;
            public string Hash;
            public Assembly Assembly;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static List<PluginAssemblyInfo> GetRawPluginAssembliesData()
        {

            List<PluginAssemblyInfo> result = new List<PluginAssemblyInfo>();
            List<Assembly> assemblies = new List<Assembly>();
            var validAssemblies = AppDomain.CurrentDomain.GetAssemblies()
                .Where((asm) => IsValidPlugin(asm)).ToList();
            return validAssemblies.Select((asm) => new PluginAssemblyInfo() { Assembly = asm, Hash = GetAssemblyFingerprint(asm), Name = asm.GetName().Name }).ToList();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static bool IsValidPlugin(Assembly assembly)
        {
            if (assembly.FullName.Contains("Sandbox.Game, Version")) return false;
            if (assembly.FullName.Contains("Legacy, Version")) return false;
            Type[] types;
                        
            try                                     {   types = assembly.GetTypes();                            }
            catch (ReflectionTypeLoadException ex)  {   types = ex.Types.Where(t => t != null).ToArray();       }
            catch                                   {   return false;                                           }
            return types.Any(t => typeof(IPlugin).IsAssignableFrom(t) && t.IsClass && !t.IsAbstract);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static string GetAssemblyFingerprint(Assembly assembly)
        {
            var sb = new StringBuilder();
            var name = assembly.GetName();
            sb.AppendLine(name.Name);
            sb.AppendLine(name.Version.ToString());
            sb.AppendLine(BitConverter.ToString(name.GetPublicKeyToken() ?? new byte[0]));

            var types = assembly.GetTypes().OrderBy(t => t.FullName);
            foreach (var type in types)
            {
                sb.AppendLine(type.FullName);
                sb.AppendLine(type.Attributes.ToString());
                foreach (var field in type.GetFields().OrderBy(f => f.Name))
                    sb.AppendLine($"{field.Name}:{field.FieldType}");

                foreach (var method in type.GetMethods().OrderBy(m => m.Name))
                {
                    sb.AppendLine(method.ToString());
                    var body = method.GetMethodBody();
                    if (body != null)
                    {
                        var il = body.GetILAsByteArray();
                        if (il != null)
                            sb.AppendLine(BitConverter.ToString(il));
                    }
                }
            }

            using (SHA256 sha256 = SHA256.Create())
            {
                var hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(sb.ToString()));
                return Convert.ToBase64String(hash);
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static string UnscrambleSecureFingerprint(string encoded, byte[] secret)
        {
            byte[] encrypted = Convert.FromBase64String(encoded);
            byte[] hash = new byte[encrypted.Length];
            for (int i = 0; i < encrypted.Length; i++)
                hash[i] = (byte)(encrypted[i] ^ secret[i % secret.Length]);
            return Convert.ToBase64String(hash);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static string GetSecureAssemblyFingerprint(Assembly assembly, byte[] secret)
        {
            byte[] hash = Convert.FromBase64String(GetAssemblyFingerprint(assembly));         
            byte[] encrypted = new byte[hash.Length];
            for (int i = 0; i < hash.Length; i++)
                encrypted[i] = (byte)(hash[i] ^ secret[i % secret.Length]);
            return Convert.ToBase64String(encrypted);
        }
    }
}
