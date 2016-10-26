﻿using System;
using System.IO;
using System.Text;
using System.Collections.Generic;

using SpecReader;

namespace Generator {
    public class Generator {
        CSSpec spec;
        DateTime time;
        Offsets offsets;

        public Generator(CSSpec spec, Offsets offsets) {
            this.spec = spec;
            this.offsets = offsets;
            //Console.WriteLine("{0} enums, {1} included", spec.Enums.Count, spec.IncludedEnums.Count);
            //Console.WriteLine("{0} commands, {1} included", spec.Commands.Count, spec.IncludedCommands.Count);
            //Console.WriteLine("{0} structs, {1} included", spec.Structs.Count, spec.IncludedTypes.Count);

            time = DateTime.Now;
        }

        public void WriteStructs(string output, string _namespace) {
            string path = Path.Combine(output, "structs.cs");
            using (var writer = File.CreateText(path)) {
                writer.WriteLine("//autogenerated on {0}", time.ToString());
                writer.WriteLine("using System;");
                writer.WriteLine("using System.Runtime.InteropServices;");
                writer.WriteLine();
                writer.WriteLine("namespace {0} {{", _namespace);

                foreach (var s in spec.Structs) {
                    if (s.Handle) {
                        WriteHandle(writer, s);
                        continue;
                    }

                    if (offsets.Contains(s.Name)) writer.WriteLine("    [StructLayout(LayoutKind.Explicit, Size = {0})]",
                        offsets[s.Name].size);
                    writer.Write("    public");
                    if (s.Unsafe) writer.Write(" unsafe");
                    writer.WriteLine(" partial struct {0} {{", s.Name);

                    for (int i = 0; i < s.Fields.Count; i++) {
                        var f = s.Fields[i];
                        if (f.Attribute != null) {
                            writer.WriteLine("        {0}", f.Attribute);
                        }
                        string type = f.Type;
                        if (spec.FlagsMap.ContainsKey(type)) {
                            type = spec.FlagsMap[type];
                        }

                        int fieldSize = CSSpec.GetSize(type);

                        if (offsets.Contains(type)) {
                            fieldSize = offsets[type].size;
                        }


                        if (f.ArraySize > 0 && offsets.Contains(s.Name) && type != "byte") {
                            Offset o = offsets[s.Name];
                            for (int j = 0; j < f.ArraySize; j++) {
                                writer.WriteLine("        [FieldOffset({0})]", offsets[s.Name].offsets[i] + (j * fieldSize));
                                writer.WriteLine("        public {0} {1}_{2};", type, f.Name, j);
                            }

                            writer.WriteLine();
                            writer.WriteLine("        public {0} Get{1}{2}(int i) {{", f.Type, char.ToUpper(f.Name[0]), f.Name.Substring(1));
                            writer.WriteLine("            switch (i) {");
                            for (int j = 0; j < f.ArraySize; j++) {
                                writer.WriteLine("                case {0}: return {1}_{0};", j, f.Name);
                            }
                            writer.WriteLine("                default: return default({0});", f.Type);
                            writer.WriteLine("            }");
                            writer.WriteLine("        }");

                            writer.WriteLine();
                            writer.WriteLine("        public void Set{1}{2}(int i, {0} value) {{", f.Type, char.ToUpper(f.Name[0]), f.Name.Substring(1));
                            writer.WriteLine("            switch (i) {");
                            for (int j = 0; j < f.ArraySize; j++) {
                                writer.WriteLine("                case {0}: {1}_{0} = value; break;", j, f.Name);
                            }
                            writer.WriteLine("                default: return;", f.Type);
                            writer.WriteLine("            }");
                            writer.WriteLine("        }");
                        } else {
                            if (offsets.Contains(s.Name)) writer.WriteLine("        [FieldOffset({0})]", offsets[s.Name].offsets[i]);
                            writer.WriteLine("        public {0} {1};", type, f.Name);
                        }
                    }

                    writer.WriteLine("    }");
                    writer.WriteLine();
                }

                writer.WriteLine("}");
            }
        }

        void WriteHandle(StreamWriter writer, CSStruct s) {
            writer.WriteLine("    public struct {0} : IEquatable<{0}> {{", s.Name);
            foreach (var f in s.Fields) {
                writer.WriteLine("        public {0} {1};", f.Type, f.Name);
            }

            writer.WriteLine(
@"
        public static {0} Null {{ get; }} = new {0}();

        public override bool Equals(object other) {{
            if (other is {0}) {{
                return Equals(({0})other);
            }}
            return false;
        }}

        public bool Equals({0} other) {{
            return other.native == native;
        }}

        public static bool operator == ({0} a, {0} b) {{
            return a.Equals(b);
        }}

        public static bool operator != ({0} a, {0} b) {{
            return !(a == b);
        }}

        public override int GetHashCode() {{
            return native.GetHashCode();
        }}", s.Name);

            writer.WriteLine("    }");
            writer.WriteLine();
        }

        public void WriteEnums(string output, string _namespace) {
            string path = Path.Combine(output, "enums.cs");
            using (var writer = File.CreateText(path)) {
                writer.WriteLine("//autogenerated on {0}", time.ToString());
                writer.WriteLine("using System;");
                writer.WriteLine();
                writer.WriteLine("namespace {0} {{", _namespace);

                foreach (var e in spec.Enums) {
                    if (e.Flags) {
                        writer.WriteLine("    [Flags]");
                    }
                    string name = e.Name;
                    if (spec.FlagsMap.ContainsKey(name)) {
                        name = spec.FlagsMap[name];
                    }

                    writer.Write("    public enum {0}", e.Name);
                    if (e.Flags) writer.Write(" : uint");
                    writer.WriteLine(" {");

                    foreach (var v in e.Values) {
                        writer.WriteLine("        {0} = {1},", v.Name, v.Value);
                    }

                    writer.WriteLine("    }");
                    writer.WriteLine();
                }

                writer.WriteLine("}");
            }
        }

        public void WriteDelegates(string output, string _namespace) {
            string path = Path.Combine(output, "delegates.cs");
            using (var writer = File.CreateText(path)) {
                writer.WriteLine("//autogenerated on {0}", time.ToString());
                writer.WriteLine("using System;");
                writer.WriteLine();
                writer.WriteLine("namespace {0} {{", _namespace);

                foreach (var c in spec.Commands) {
                    bool _unsafe = false;

                    foreach (var p in c.Params) {
                        if (p.Pointer > 0) {
                            _unsafe = true;
                            break;
                        }
                    }

                    writer.Write("    public");
                    if (_unsafe) writer.Write(" unsafe");
                    writer.WriteLine(" delegate {0} {1}Delegate(", c.ReturnType, c.Name);

                    for (int i = 0; i < c.Params.Count; i++) {
                        var p = c.Params[i];

                        string type = p.Type;
                        if (spec.FlagsMap.ContainsKey(type)) {
                            type = spec.FlagsMap[type];
                        }

                        writer.Write("        {0} {1}", type, p.Name);
                        if (i != c.Params.Count - 1) writer.Write(",");
                        writer.WriteLine();
                    }
                    writer.WriteLine("    );");
                }

                writer.WriteLine("}");
            }
        }

        public void WriteCommands(string output, string _namespace) {
            string path = Path.Combine(output, "commands.cs");
            using (var writer = File.CreateText(path)) {
                writer.WriteLine("//autogenerated on {0}", time.ToString());
                writer.WriteLine("using System;");
                writer.WriteLine("using System.Runtime.InteropServices;");
                writer.WriteLine();
                writer.WriteLine("namespace {0} {{", _namespace);
                writer.WriteLine("    public static partial class VK {");
                writer.WriteLine("        const string lib = \"vulkan-1.dll\";");

                foreach (var c in spec.Commands) {
                    bool _unsafe = false;

                    foreach (var p in c.Params) {
                        if (p.Pointer > 0) {
                            _unsafe = true;
                            break;
                        }
                    }
                    writer.WriteLine("        [DllImport(lib, EntryPoint = \"{0}\")]", c.Name);
                    writer.Write("        public static");
                    if (_unsafe) writer.Write(" unsafe");
                    writer.WriteLine(" extern {0} {1}(", c.ReturnType, c.Name.Substring(2));

                    for (int i = 0; i < c.Params.Count; i++) {
                        var p = c.Params[i];

                        string type = p.Type;
                        if (spec.FlagsMap.ContainsKey(type)) {
                            type = spec.FlagsMap[type];
                        }

                        writer.Write("            {0} {1}", type, p.Name);
                        if (i != c.Params.Count - 1) writer.Write(",");
                        writer.WriteLine();
                    }
                    writer.WriteLine("        );");
                    writer.WriteLine();
                }

                writer.WriteLine("    }");
                writer.WriteLine("}");
            }
        }
    }
}
