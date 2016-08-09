﻿using System;
using System.IO;
using System.Text;

using VulkanGenerator;

namespace CS_Generator {
    public class Generator {
        Spec spec;
        DateTime time;

        public Generator(Spec spec) {
            this.spec = spec;
            Console.WriteLine("{0} enums, {1} included", spec.Enums.Count, spec.IncludedEnums.Count);
            Console.WriteLine("{0} commands, {1} included", spec.Commands.Count, spec.IncludedCommands.Count);
            Console.WriteLine("{0} structs, {1} included", spec.Structs.Count, spec.IncludedTypes.Count);

            Preprocess();
            time = DateTime.Now;
        }

        void Preprocess() {
            foreach (var e in spec.Enums) {
                var rName = e.Name;
                var nName = GetName(e);
                if (spec.AllEnums.Contains(rName) && spec.AllEnums.Contains(nName) && rName != nName) {
                    var flagEnum = spec.EnumMap[nName];
                    flagEnum.Values = e.Values;
                    spec.AllEnums.Remove(rName);
                }
            }
        }

        public void WriteEnums(string outputFolder, string _namespace) {
            string path = Path.Combine(outputFolder, "enums.cs");
            using (var writer = File.CreateText(path)) {
                writer.WriteLine("//autogenerated on {0}", time.ToString());
                writer.WriteLine("using System;");
                writer.WriteLine();
                writer.WriteLine("namespace {0} {{", _namespace);

                foreach (var e in spec.Enums) {
                    if (!spec.AllEnums.Contains(e.Name)) continue;
                    if (e.Bitmask) writer.WriteLine("    [Flags]");
                    writer.Write("    public enum {0}", GetName(e));

                    if (e.Bitmask) {
                        writer.WriteLine(" : uint {");
                        writer.WriteLine("        None = 0,");
                    } else {
                        writer.WriteLine(" {");
                    }

                    foreach (var v in e.Values) {
                        if (e.Bitmask) {
                            writer.Write("        {0} = ", GetName(v));
                            if (v.Bitpos) {
                                writer.WriteLine("{0},", (int)Math.Pow(2, int.Parse(v.Value)));
                            } else {
                                writer.WriteLine("{0},", v.Value);
                            }
                        } else {
                            writer.WriteLine("        {0} = {1},", GetName(v), v.Value);
                        }
                    }

                    writer.WriteLine("    }");
                    writer.WriteLine();
                }

                writer.WriteLine("}");
            }
        }

        public void WriteCommands(string outputFolder, string _namespace) {
            string path = Path.Combine(outputFolder, "commands.cs");
            using (var writer = File.CreateText(path)) {
                writer.WriteLine("//autogenerated on {0}", time.ToString());
                writer.WriteLine("using System;");
                writer.WriteLine("using System.Security;");
                writer.WriteLine("using System.Runtime.InteropServices;");
                writer.WriteLine();
                writer.WriteLine("namespace {0} {{", _namespace);
                writer.WriteLine("    public static partial class VK {");

                foreach (var c in spec.Commands) {
                    if (!spec.IncludedCommands.Contains(c.Name)) continue;
                    writer.WriteLine("        public static{0} {1}Delegate {2};", "", c.Name, c.Name.Substring(2));
                }

                writer.WriteLine("    }");

                foreach (var c in spec.Commands) {
                    if (!spec.IncludedCommands.Contains(c.Name)) continue;

                    writer.WriteLine("    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]");
                    writer.WriteLine("    [SuppressUnmanagedCodeSecurity]");

                    string _unsafe = "";
                    foreach (var p in c.Params) {
                        if (p.Pointer == 0) continue;
                        var baseType = p.Type.Substring(0, p.Type.Length - 1);
                        if (p.Pointer > 1 || GetType(p.Type) == "byte*" || p.Type == "void*" || p.Type == "VkAllocationCallbacks*") {
                            _unsafe = " unsafe";
                        }
                    }

                    writer.Write("    public{0} delegate {1} {2}Delegate(", _unsafe, GetType(c), c.Name);

                    for (int i = 0; i < c.Params.Count; i++) {
                        var p = c.Params[i];
                        string type = GetType(p);
                        if (p.Pointer != 0) {
                            if (!(p.Type == "void*" || GetType(p.Type) == "byte*" || p.Type == "VkAllocationCallbacks*" || p.Pointer > 1)) {
                                writer.Write("ref ");
                                type = type.Substring(0, type.Length - 1);
                            }
                        }
                        writer.Write("{0} {1}", type, GetName(p));
                        if (i < c.Params.Count - 1) writer.Write(", ");
                    }

                    writer.WriteLine(");");
                    writer.WriteLine();
                }

                writer.WriteLine("}");
            }
        }

        public void WriteStructs(string outputFolder, string _namespace) {
            string path = Path.Combine(outputFolder, "structs.cs");
            using (var writer = File.CreateText(path)) {
                writer.WriteLine("//autogenerated on {0}", time.ToString());
                writer.WriteLine("using System;");
                writer.WriteLine("using System.Runtime.InteropServices;");
                writer.WriteLine();
                writer.WriteLine("namespace {0} {{", _namespace);

                foreach (var s in spec.Structs) {
                    if (spec.ExtensionTypes.Contains(s.Name) && !spec.IncludedTypes.Contains(s.Name)) continue;
                    if (s.Handle) {
                        GenerateHandle(writer, s);
                    } else {
                        bool union = s.Union;
                        bool _unsafe = false;
                        foreach (var f in s.Fields) {
                            if (f.Pointer || f.ArraySize != null) {
                                _unsafe = true;
                                break;
                            }
                        }

                        if (union) {
                            writer.WriteLine("    [StructLayout(LayoutKind.Explicit)] //union");
                        }
                        writer.Write("    public");
                        if (_unsafe) writer.Write(" unsafe");
                        writer.Write(" struct {0} {{", s.Name);
                        writer.WriteLine();

                        foreach (var f in s.Fields) {
                            bool fieldUnsafe = false;
                            bool _fixed = false;
                            string arraySize = "";
                            string finalType = GetType(f);
                            bool isPrimitive = IsPrimitive(finalType);

                            if (union) writer.WriteLine("        [FieldOffset(0)]");
                            if (f.ArraySize != null) {
                                _fixed = true;
                                fieldUnsafe = true;
                                int dummy;
                                if (int.TryParse(f.ArraySize, out dummy)) {
                                    arraySize = f.ArraySize;
                                } else {
                                    arraySize = spec.EnumValuesMap[f.ArraySize];
                                }
                                if (!isPrimitive) {
                                    fieldUnsafe = false;
                                    writer.WriteLine("        [MarshalAs(UnmanagedType.ByValArray, SizeConst = {0})]", arraySize);
                                }
                            }


                            writer.Write("        public ");
                            if (fieldUnsafe) writer.Write("unsafe ");
                            if (_fixed && isPrimitive) writer.Write("fixed ");
                            writer.Write(finalType);
                            if (!isPrimitive && _fixed) writer.Write("[]");
                            writer.Write(" {0}", GetName(f));
                            if (_fixed && isPrimitive) writer.Write("[{0}]", arraySize);
                            writer.WriteLine(";");
                        }
                        writer.WriteLine("    }");
                        writer.WriteLine();
                    }
                }
                
                writer.WriteLine("}");
            }
        }

        bool IsPrimitive(string input) {
            if (input == "sbyte" ||
                input == "short" ||
                input == "int" ||
                input == "long" ||
                input == "byte" ||
                input == "ushort" ||
                input == "uint" ||
                input == "ulong" ||
                input == "float"
                ) return true;
            return false;
        }

        void GenerateHandle(StreamWriter writer, Struct s) {
            writer.WriteLine("    [StructLayout(LayoutKind.Sequential, Pack = 1)]");
            //writer.WriteLine("    public struct {0} : IEquatable<{0}> {{", s.Name);
            writer.WriteLine("    public struct {0} {{", s.Name);
            foreach (var f in s.Fields) {
                writer.WriteLine("        public {0} {1};",  GetType(f), GetName(f));
            }
            writer.WriteLine();

            writer.WriteLine("        public static {0} Null {{ get; }} = new {0}();", s.Name);
            writer.WriteLine();

            writer.WriteLine("        public bool IsNull {");
            writer.WriteLine("            get {");
            writer.WriteLine("                return this == Null;");
            writer.WriteLine("            }");
            writer.WriteLine("        }");

            writer.WriteLine("        public bool Equals({0} other) {{", s.Name);
            writer.WriteLine("            return native == other.native;");
            writer.WriteLine("        }");
            writer.WriteLine();

            writer.WriteLine("        public override bool Equals(object other) {");
            writer.WriteLine("            if (other is {0}) {{", s.Name);
            writer.WriteLine("                return Equals(({0})other);", s.Name);
            writer.WriteLine("            }");
            writer.WriteLine("            return false;");
            writer.WriteLine("        }");
            writer.WriteLine();

            writer.WriteLine("        public override int GetHashCode() {");
            writer.WriteLine("            return native.GetHashCode();");
            writer.WriteLine("        }");
            writer.WriteLine();

            writer.WriteLine("        public static bool operator == ({0} a, {0} b) {{", s.Name);
            writer.WriteLine("            return a.Equals(b);");
            writer.WriteLine("        }");
            writer.WriteLine();

            writer.WriteLine("        public static bool operator != ({0} a, {0} b) {{", s.Name);
            writer.WriteLine("            return !a.Equals(b);");
            writer.WriteLine("        }");
            writer.WriteLine();

            writer.WriteLine("    }");
            writer.WriteLine();
        }

        public void WriteLoader(string outputFolder, string _namespace) {
            string path = Path.Combine(outputFolder, "loader.cs");
            using (var writer = File.CreateText(path)) {
                writer.WriteLine("//autogenerated on {0}", time.ToString());
                writer.WriteLine("using System;");
                writer.WriteLine();
                writer.WriteLine("namespace {0} {{", _namespace);
                writer.WriteLine("    public static partial class VK {");

                writer.WriteLine("        static readonly string[] commands = {");
                foreach (var c in spec.Commands) {
                    if (!spec.IncludedCommands.Contains(c.Name)) continue;
                    writer.WriteLine("            \"{0}\",", c.Name);
                }
                writer.WriteLine("        };");

                writer.WriteLine("    }");
                writer.WriteLine("}");
            }
        }

        string GetName(VulkanGenerator.Enum e) {
            return BitsToFlag(e.Name);
        }

        string GetName(EnumValue e) {
            string[] tokens = e.Name.Split('_');
            StringBuilder builder = new StringBuilder();
            int parts = 0;
            for (int i = 0; i < tokens.Length; i++) {
                var t = tokens[i];
                if (t == "VK") continue;

                if (parts == 0 && char.IsDigit(t[0])) builder.Append('_');

                builder.Append(char.ToUpper(t[0]));
                for (int j = 1; j < t.Length; j++) {
                    builder.Append(char.ToLower(t[j]));
                }
                parts++;
            }

            return builder.ToString();
        }

        string GetType(Field field) {
            string result = BitsToFlag(field.Type);

            if (result.Contains("PFN_")) {
                return "IntPtr";
            }
            switch (result) {
                case "size_t": return "long";
                case "VkBool32": return "bool";
                case "VkDeviceSize": return "ulong";
                case "VkSampleMask": return "uint";
                case "char": return "byte";

                case "uint8_t": return "byte";
                case "uint16_t": return "ushort";
                case "uint32_t": return "uint";
                case "uint64_t": return "ulong";

                case "int8_t": return "sbyte";
                case "int16_t": return "short";
                case "int32_t": return "int";
                case "int64_t": return "long";

                case "char*": return "byte*";
                case "char**": return "byte**";
                case "uint32_t*": return "uint*";
                case "VkSampleMask*": return "uint*";

                case "HWND": return "IntPtr";
                case "HINSTANCE": return "IntPtr";
                default: return result;
            }
        }

        string GetName(Field field) {
            switch (field.Name) {
                case "object": return "_object";
                default: return field.Name;
            }
        }

        string GetType(Command c) {
            return GetType(c.ReturnType);
        }

        string GetType(Param p) {
            return GetType(BitsToFlag(p.Type));
        }

        string GetName(Param p) {
            switch (p.Name) {
                case "event": return "_event";
                case "object": return "_object";
                default: return p.Name;
            }
        }

        string GetType(string input) {
            switch (input) {
                case "size_t": return "long";
                case "VkBool32": return "uint";
                case "VkDeviceSize": return "ulong";
                case "VkSampleMask": return "uint";
                case "PFN_vkVoidFunction": return "IntPtr";

                case "uint8_t": return "byte";
                case "uint16_t": return "ushort";
                case "uint32_t": return "uint";
                case "uint64_t": return "ulong";
                case "char": return "byte";

                case "int8_t": return "sbyte";
                case "int16_t": return "short";
                case "int32_t": return "int";
                case "int64_t": return "long";

                case "char*": return "byte*";
                case "char**": return "byte**";
                case "uint32_t*": return "uint*";
                case "VkSampleMask*": return "uint*";
                case "VkBool32*": return "uint*";
                case "VkDeviceSize*": return "ulong*";
                case "size_t*": return "ulong*";

                case "HWND": return "IntPtr";
                case "HINSTANCE": return "IntPtr";
                default: return input;
            }
        }

        string BitsToFlag(string input) {
            string result = input;
            const string check = "FlagBits";
            if (input.Contains(check)) {
                int i = input.IndexOf(check);
                result = input.Substring(0, i) + "Flags";
            }
            return result;
        }
    }
}
