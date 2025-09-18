
using CleanSpaceShared.Plugin;
using CleanSpaceShared.Struct;
using NLog.Targets;
using ProtoBuf;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

[ProtoContract]
public class MethodIdentifier
{
    [ProtoMember(1)]
    public string FullName { get; set; }

    [ProtoMember(2)]
    public string[] MethodParams { get; set; }

    [ProtoMember(3)]
    public string MethodName { get; set; }

    [ProtoMember(4)]
    public bool InExecutingAssembly { get; set; }

}

public static class ILAttester
{
    public static byte[] GetMethodIlBytes(MethodBase method)
    {
        if (method == null) throw new ArgumentNullException(nameof(method));
        var body = method.GetMethodBody();
        if (body == null) return Array.Empty<byte>();
        byte[] ilBytes = body.GetILAsByteArray();
        if (ilBytes == null) return Array.Empty<byte>();
        return ilBytes;
    }

    public static byte[] GetTypeIlBytes(Type type)
    {
        if (type == null) throw new ArgumentNullException(nameof(type));
        var allBytes = type
            .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .SelectMany(m => { var bytes = GetMethodIlBytes(m); return bytes ?? Array.Empty<byte>(); }).ToArray();
        return allBytes;
    }
}

[ProtoContract]
public class JittestResponse
{
    [ProtoMember(1)]
    public string AssemblyName { get; set; } = "";

    [ProtoMember(2)]
    public string ModuleMvid { get; set; } = "";

    [ProtoMember(3)]
    public int MetadataToken { get; set; }

    [ProtoMember(4)]
    public long RuntimeMethodHandleValue { get; set; }

    [ProtoMember(5)]
    public ulong FunctionPointer { get; set; }

    [ProtoMember(6)]
    public int WindowLength { get; set; }

    [ProtoMember(7)]
    public byte[] WindowBytes { get; set; } = Array.Empty<byte>();

    [ProtoMember(8)]
    public byte[] Mac { get; set; } = Array.Empty<byte>();
}


// JitAttest. While I leave this here, it is currently a failed experiment.
// Normalizing x64 op codes to be reliabily consistent in how similar they are across environments is not easy.
// But, look forward to it, cheaters.
public static class JitAttest
{
    // Maximum bytes to try reading. God hope we dont hit this limit.
    const int MaxMethodSize = 64 * 1024; // 64 KB

    private static byte[] ReadUntilFault(IntPtr ptr, int maxLen)
    {
        var buffer = new List<byte>();
        var tmp = new byte[16]; // small chunk for probing
        int offset = 0;

        while (offset < maxLen)
        {
            try
            {
                Marshal.Copy(ptr + offset, tmp, 0, tmp.Length);
                buffer.AddRange(tmp);
                offset += tmp.Length;
            }
            catch
            {
                break; // stop at first invalid read
            }
        }

        return buffer.ToArray();
    }

    public static JittestResponse BuildAttestation(
        MethodBase method,
        byte[] nonce,
        byte[] macKey)
    {
        RuntimeHelpers.PrepareMethod(method.MethodHandle);

        var rmhVal = method.MethodHandle.Value;
        var fptr = method.MethodHandle.GetFunctionPointer();

        var window = ReadUntilFault(fptr, MaxMethodSize);

        byte[] mac;
        using (var hmac = new HMACSHA256(macKey))
        {
            void Feed(byte[] b) => hmac.TransformBlock(b, 0, b.Length, null, 0);

            Feed(BitConverter.GetBytes(nonce.Length));
            Feed(nonce);

            var asmName = method.DeclaringType?.Assembly.GetName().Name ?? "";
            var asmBytes = Encoding.UTF8.GetBytes(asmName);
            Feed(BitConverter.GetBytes(asmBytes.Length));
            Feed(asmBytes);

            var mvidBytes = method.Module.ModuleVersionId.ToByteArray();
            Feed(BitConverter.GetBytes(mvidBytes.Length));
            Feed(mvidBytes);

            Feed(BitConverter.GetBytes(method.MetadataToken));
            Feed(BitConverter.GetBytes(rmhVal.ToInt64()));
            Feed(BitConverter.GetBytes(fptr.ToInt64()));

            Feed(BitConverter.GetBytes(window.Length));
            Feed(window);

            hmac.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
            mac = hmac.Hash;
        }

        return new JittestResponse
        {
            AssemblyName = method.DeclaringType?.Assembly.GetName().Name ?? "",
            ModuleMvid = method.Module.ModuleVersionId.ToString(),
            MetadataToken = method.MetadataToken,
            RuntimeMethodHandleValue = rmhVal.ToInt64(),
            FunctionPointer = (ulong)fptr.ToInt64(),
            WindowLength = window.Length,
            WindowBytes = window,
            Mac = mac
        };
    }
}




public enum RequestType : byte
{
    MethodIL = 31,
  //  TypeIL = 32, ITS NOT GONE! todo! :(
  //  SaltEcho = 32, Pointless, but maybe
  //  LoadedAssembliesCount = 40, COMPLETELY pointless
  //  JitAttestation = 100   :( experiment unsuccessful
}

public static class ChatterChallengeFactory
{
    private static readonly Random _rng = new Random();

    public delegate byte[] ChunkValidationProvider(params object[] param);
    public static Dictionary<RequestType, ChunkValidationProvider> providers = new Dictionary<RequestType, ChunkValidationProvider>();

    public static void RegisterProvider(RequestType requestType, ChunkValidationProvider requestDelegate)
    {
        providers[requestType] = requestDelegate;
    }

    private static MethodBase GetRandomMethod(Type type)
    {
        if (type == null) throw new ArgumentNullException(nameof(type));

        var methods = type
            .GetMethods(BindingFlags.Public | BindingFlags.NonPublic |
                        BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Where(m =>
                !m.IsSpecialName &&
                m.Name != ".ctor" && m.Name != ".cctor")
            .Cast<MethodBase>()
            .ToArray();

        if (methods.Length == 0)
            return null;

        return methods[_rng.Next(methods.Length)];
    }


    public static ChatterChallenge CreateSessionParameters(byte[] salt)
    {
        var numRequests = _rng.Next(2, 5);
        List<ChatterChallengeRequest> requests = new List<ChatterChallengeRequest>();

        Common.Logger.Debug($"{Common.PluginName} Starting with {numRequests} request slots, salt length={salt?.Length ?? 0}");

        for (int i = 0; i < numRequests; i++)
        {
            byte newRequesType = GetRandomRequestType();
            Common.Logger.Debug($"{Common.PluginName} Slot {i}: picked request type {(RequestType)newRequesType}");

            switch (newRequesType)
            {
                case ((byte)RequestType.MethodIL):
                    //  case ((byte)RequestType.JitAttestation):

                    bool ct_MakeCoreType = _rng.Next(0, 1) == 1;

                    // On a coin flip, we either take a method from our critical types.
                    if (!ct_MakeCoreType)
                    {
                        Type[] choices = Common.CriticalTypes;
                        Type choice = choices[_rng.Next(choices.Length)];
                        MethodBase target = GetRandomMethod(choice);

                        if (target == null)
                        {
                            Common.Logger.Error($"{Common.PluginName} Failed to resolve a target method for type {choice.FullName}");
                            continue;
                        }

                        var paramTypes = target.GetParameters().Select(p => p.ParameterType.Name).ToArray();
                        Common.Logger.Debug($"{Common.PluginName}: Assigned method: {target.DeclaringType?.FullName}.{target.Name}({string.Join(", ", paramTypes)})");

                        byte[] serializedMethodBase = ProtoUtil.Serialize(
                            new MethodIdentifier
                            {
                                FullName = target.DeclaringType?.FullName ?? "<null>",
                                MethodParams = paramTypes,
                                MethodName = target.Name,
                                InExecutingAssembly = false
                            });

                        Common.Logger.Debug($"{Common.PluginName}: Serialized MethodIdentifier (Base64): {Convert.ToBase64String(serializedMethodBase)}");

                        requests.Add(new ChatterChallengeRequest
                        {
                            request = newRequesType,
                            context = serializedMethodBase
                        });
                    }
                    else
                    {
                        // Or we ask for a method inside the main client class like such so we do not have to load the type (which may not work).
                        string[] choices = Common.CriticalClientMethods;
                        string choice = choices[_rng.Next(choices.Length)];
                        string clientNamespace = "CleanSpaceClient.CleanSpaceClientPlugin";
                        Common.Logger.Debug($"{Common.PluginName}: Assigned method: {clientNamespace}.{choice}");

                        byte[] serializedMethodBase = ProtoUtil.Serialize(
                           new MethodIdentifier
                           {
                               FullName = $"{clientNamespace}.{choice}",
                               MethodParams =  new string[] { },
                               MethodName = choice,
                               InExecutingAssembly = true
                           });
                    }
                    break;                
            }
        }

        string challenges = string.Join(", ", requests.Select(e => Enum.GetName(typeof(RequestType), e.request)));
        Common.Logger.Debug($"{Common.PluginName}: Final challenge sequence: {challenges}");

        var ret = new ChatterChallenge
        {
            requests = requests.ToArray(),
            chatterLength = (byte)_rng.Next(1, 6),
            sessionSalt = salt
        };

        Common.Logger.Debug($"{Common.PluginName}: Created SessionParameters: chatterLength={ret.chatterLength}, totalRequests={ret.requests.Length}");
        return ret;
    }

    public static byte[] AnswerChallenge(ChatterChallenge challenge, params object[] args)
    {

        using (var ms = new MemoryStream())
        {
            foreach (var request in challenge.requests)
            {
                var rt = (RequestType)request.request;
                if (!providers.TryGetValue(rt, out var provider))
                {
                    throw new ArgumentException("Provider not found: " + rt.ToString());
                }
                   
                var fullArgs = new object[] { request.context }.Concat(args).ToArray();
                byte[] slice = provider.Invoke(fullArgs);
                byte[] len = BitConverter.GetBytes(slice.Length);
                ms.WriteByte(request.request);
                ms.Write(len,0,len.Length);
                ms.Write(slice, 0, slice.Length);
            }
            return ms.ToArray();
        }
    }

    private static byte GetRandomRequestType()
    {
        var values = (RequestType[])Enum.GetValues(typeof(RequestType));
        return (byte)values[_rng.Next(values.Length)];
    }

}