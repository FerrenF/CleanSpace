using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;


namespace CleanSpaceTorch.Util
{

    public class ILInstruction
    {
        public int Offset { get; set; }
        public OpCode OpCode { get; set; }
        public object Operand { get; set; }
        public override string ToString()
        {
            return Operand == null ? $"{Offset:X4}: {OpCode.Name}" : $"{Offset:X4}: {OpCode.Name} {Operand}";
        }
    }

    public class ILReader : IEnumerable<ILInstruction>
    {
        private readonly byte[] _il;
        private readonly Module _module;
        private static readonly Dictionary<short, OpCode> _opcodeMap;

        static ILReader()
        {
            _opcodeMap = typeof(OpCodes)
                .GetFields(BindingFlags.Public | BindingFlags.Static)
                .Where(f => f.FieldType == typeof(OpCode))
                .Select(f => (OpCode)f.GetValue(null))
                .ToDictionary(c => c.Value, c => c);
        }

        public ILReader(byte[] ilBytes, Module module = null)
        {
            _il = ilBytes ?? Array.Empty<byte>();
            _module = module;
        }

        public IEnumerator<ILInstruction> GetEnumerator()
        {
            int pos = 0;
            while (pos < _il.Length)
            {
                int offset = pos;
                byte b = _il[pos++];
                OpCode op;
                if (b == 0xFE)
                {
                    if (pos >= _il.Length) yield break;
                    byte b2 = _il[pos++];
                    short value = (short)((b << 8) | b2);
                    if (!_opcodeMap.TryGetValue(value, out op))
                        throw new InvalidOperationException($"Unknown two-byte opcode 0x{value:X4} at offset {offset}");
                }
                else
                {
                    short value = b;
                    if (!_opcodeMap.TryGetValue(value, out op))
                        throw new InvalidOperationException($"Unknown opcode 0x{value:X2} at offset {offset}");
                }

                object operand = null;

                switch (op.OperandType)
                {
                    case OperandType.InlineNone:
                        break;

                    case OperandType.ShortInlineI:
                        operand = (sbyte)_il[pos++];
                        break;

                    case OperandType.ShortInlineVar:
                        operand = _il[pos++];
                        break;

                    case OperandType.ShortInlineR:
                        {
                            var val = BitConverter.ToSingle(_il, pos);
                            operand = val;
                            pos += 4;
                        }
                        break;

                    case OperandType.InlineVar:
                        {
                            var val = BitConverter.ToUInt16(_il, pos);
                            operand = val; 
                            pos += 2;
                        }
                        break;

                    case OperandType.InlineI:
                        {
                            var val = BitConverter.ToInt32(_il, pos);
                            operand = val;
                            pos += 4;
                        }
                        break;

                    case OperandType.InlineI8:
                        {
                            var val = BitConverter.ToInt64(_il, pos);
                            operand = val;
                            pos += 8;
                        }
                        break;

                    case OperandType.InlineR:
                        {
                            var val = BitConverter.ToDouble(_il, pos);
                            operand = val;
                            pos += 8;
                        }
                        break;

                    case OperandType.ShortInlineBrTarget:
                        {
                            // sbyte relative
                            sbyte rel = (sbyte)_il[pos++];
                            operand = offset + 1 + rel;
                        }
                        break;

                    case OperandType.InlineBrTarget:
                        {
                            // int32 relative
                            int rel = BitConverter.ToInt32(_il, pos);
                            pos += 4;
                            operand = offset + 5 + rel;
                        }
                        break;

                    case OperandType.InlineSwitch:
                        {
                            int count = BitConverter.ToInt32(_il, pos);
                            pos += 4;
                            var targets = new int[count];
                            for (int i = 0; i < count; i++)
                            {
                                int rel = BitConverter.ToInt32(_il, pos);
                                pos += 4;
                                targets[i] = offset + 5 + (4 * count) + rel;
                            }
                            operand = targets;
                        }
                        break;

                    case OperandType.InlineString:
                    case OperandType.InlineField:
                    case OperandType.InlineMethod:
                    case OperandType.InlineTok:
                    case OperandType.InlineType:
                        {
                            int tk = BitConverter.ToInt32(_il, pos);
                            pos += 4;
                            operand = ResolveToken(op.OperandType, tk);
                        }
                        break;

                    default:
                        throw new NotSupportedException($"Unhandled operand type {op.OperandType} for opcode {op.Name} at {offset}");
                }

                yield return new ILInstruction { Offset = offset, OpCode = op, Operand = operand };
            }
        }

        private object ResolveToken(OperandType operandType, int token)
        {
            if (_module == null)
                return $"Token: 0x{token:X8}";

            try
            {
                switch (operandType)
                {
                    case OperandType.InlineString:
                        {
                            // Note: ResolveString may throw for tokens from other modules; catch below.
                            return _module.ResolveString(token);
                        }

                    case OperandType.InlineField:
                        {
                            return _module.ResolveField(token, null, null);
                        }

                    case OperandType.InlineMethod:
                    case OperandType.InlineTok:
                        {
                            // Resolve to MethodBase when possible, otherwise return token
                            try
                            {
                                return _module.ResolveMethod(token, null, null);
                            }
                            catch
                            {
                                // Sometimes token points to a memberref/type/field - try ResolveMember
                                return _module.ResolveMember(token, null, null);
                            }
                        }

                    case OperandType.InlineType:
                        {
                            return _module.ResolveType(token, null, null);
                        }
                }
            }
            catch
            {
                // resolution failed (different load context, generic signature missing etc.)
            }

            // fallback textual token representation
            return $"Token: 0x{token:X8}";
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    /// <summary>
    /// Semantic normalizer for IL blobs.
    /// </summary>
    public static class IlSemanticNormalizer
    {
        /// <summary>
        /// Produce a stable textual representation of the IL suitable for hashing/comparison.
        /// If methodBase is provided, the reader will attempt to resolve metadata tokens against that method's module.
        /// </summary>
        public static string NormalizeIL(byte[] ilBytes, MethodBase methodBase = null)
        {
            Module module = methodBase?.Module;
            var reader = new ILReader(ilBytes, module);
            var sb = new StringBuilder();

            foreach (var instr in reader)
            {
                sb.Append(instr.Offset.ToString("X4"));
                sb.Append(": ");
                sb.Append(instr.OpCode.Name);

                if (instr.Operand != null)
                {
                    sb.Append(' ');

                    switch (instr.Operand)
                    {                      
                        case MethodBase mb:
                            sb.Append(MethodDescriptor(mb));
                            break;

                        case Type t:
                            sb.Append(TypeDescriptor(t));
                            break;

                        case FieldInfo f:
                            sb.Append(FieldDescriptor(f));
                            break;

                        case string s:
                            sb.Append($"\"{s}\"");
                            break;

                        case int[] targets:
                            sb.Append('[');
                            sb.Append(string.Join(", ", targets.Select(t => "0x" + t.ToString("X4"))));
                            sb.Append(']');
                            break;

                        default:
                            sb.Append(instr.Operand.ToString());
                            break;
                    }
                }

                sb.AppendLine();
            }

            return sb.ToString();
        }

        private static string MethodDescriptor(MethodBase m)
        {
            var decl = m.DeclaringType != null ? m.DeclaringType.FullName : "<module>";
            var name = m.Name;
            var paramList = string.Join(",", m.GetParameters().Select(p => p.ParameterType.FullName ?? p.ParameterType.Name));
            return $"{decl}::{name}({paramList})";
        }

        private static string TypeDescriptor(Type t)
        {
            return t.FullName ?? t.Name;
        }

        private static string FieldDescriptor(FieldInfo f)
        {
            var decl = f.DeclaringType != null ? f.DeclaringType.FullName : "<module>";
            return $"{decl}::{f.Name}";
        }
    }

}
