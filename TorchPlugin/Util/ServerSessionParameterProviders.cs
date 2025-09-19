using System;
using System.Linq;
using System.Reflection;

namespace CleanSpaceTorch.Util
{
    internal class ServerSessionParameterProviders
    {

        // these are here because the client counterparts initiate an app domain check that torch will fail.
        public static void RegisterProviders()
        {          

            ChatterChallengeFactory.RegisterProvider(RequestType.MethodIL, (args) => {         
                
                // One last todo here: unfortunately, we will still be required to load the clean space client assembly to respond to challenges.                
                MethodIdentifier m = ProtoUtil.Deserialize<MethodIdentifier>((byte[])args[0]);
                Type t = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType(m.FullName, throwOnError: false)).FirstOrDefault(x => x != null);

                if (t == null)
                    throw new InvalidOperationException($"Type {m.FullName} not found in loaded assemblies");

                MethodBase method = t.GetMethod( m.MethodName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static );

                if (method == null)
                    throw new InvalidOperationException($"Method {m.MethodName} not found on type {m.FullName}");
                return ILAttester.GetMethodIlBytes(method);
            });

          /* 
           * I am still pondering my orb over this challenge. An entire type's IL bytes might be a pretty big packet (relatively).
           * 
           * SessionParameterFactory.RegisterProvider(RequestType.TypeIL, (args) =>
            {
                MethodIdentifier m = ProtoUtil.Deserialize<MethodIdentifier>((byte[])args[0]);

                // Look through all loaded assemblies for the type
                Type t = AppDomain.CurrentDomain
                    .GetAssemblies()
                    .Select(a => a.GetType(m.FullName, throwOnError: false))
                    .FirstOrDefault(x => x != null);

                if (t == null)
                    throw new InvalidOperationException($"Type {m.FullName} not found in loaded assemblies");

                return ILAttester.GetTypeIlBytes(t);
            });*/


         

            CleanSpaceShared.Plugin.Common.Logger.Info($"{CleanSpaceShared.Plugin.Common.Logger}: Validation providers registered.");
        }
    }
}
