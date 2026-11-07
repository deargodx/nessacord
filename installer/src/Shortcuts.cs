// Nessacord installer: puts back the shortcuts Nessacord branded. Nessacord records what each one
// looked like in shortcuts.json (src/plugins/nessacord.desktop/native.ts).

using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using System.Web.Script.Serialization;

namespace Nessacord.Installer
{
    static class Shortcuts
    {
        /// Restores the recorded shortcuts that start the install at root. Returns how many.
        public static int Restore(string recordFile, string root)
        {
            if (!File.Exists(recordFile)) return 0;

            var serializer = new JavaScriptSerializer();
            var record = serializer.Deserialize<Dictionary<string, Dictionary<string, object>>>(File.ReadAllText(recordFile));
            string prefix = root.TrimEnd('\\') + "\\";
            int restored = 0;

            foreach (string brandedPath in new List<string>(record.Keys))
            {
                Dictionary<string, object> original = record[brandedPath];
                if (!File.Exists(brandedPath))
                {
                    record.Remove(brandedPath);
                    continue;
                }

                var link = (IShellLinkW)new ShellLink();
                ((IPersistFile)link).Load(brandedPath, 2 /* STGM_READWRITE */);

                var target = new StringBuilder(1024);
                link.GetPath(target, target.Capacity, IntPtr.Zero, 0);
                if (!target.ToString().StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;

                link.SetIconLocation(Text(original, "icon"), Convert.ToInt32(original.ContainsKey("iconIndex") ? original["iconIndex"] : 0));
                link.SetDescription(Text(original, "description"));
                SetAppUserModelId((IPropertyStore)link, Text(original, "appUserModelId"));
                ((IPersistFile)link).Save(null, true);
                Marshal.ReleaseComObject(link);

                string path = Text(original, "path");
                if (!string.Equals(path, brandedPath, StringComparison.OrdinalIgnoreCase))
                {
                    if (File.Exists(path)) File.Delete(brandedPath);
                    else File.Move(brandedPath, path);
                }

                record.Remove(brandedPath);
                restored++;
            }

            if (record.Count == 0) File.Delete(recordFile);
            else File.WriteAllText(recordFile, serializer.Serialize(record));
            return restored;
        }

        static string Text(Dictionary<string, object> values, string key)
        {
            object value;
            return values.TryGetValue(key, out value) && value != null ? value.ToString() : "";
        }

        static void SetAppUserModelId(IPropertyStore store, string id)
        {
            var key = new PropertyKey { FormatId = new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"), PropertyId = 5 };
            var value = new PropVariant();
            if (id.Length > 0)
            {
                value.Type = 31; // VT_LPWSTR
                value.Pointer = Marshal.StringToCoTaskMemUni(id);
            }
            try
            {
                store.SetValue(ref key, ref value);
                store.Commit();
            }
            finally
            {
                if (value.Pointer != IntPtr.Zero) Marshal.FreeCoTaskMem(value.Pointer);
            }
        }

        [ComImport, Guid("00021401-0000-0000-C000-000000000046")]
        class ShellLink { }

        [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("000214F9-0000-0000-C000-000000000046")]
        interface IShellLinkW
        {
            void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder file, int size, IntPtr findData, uint flags);
            void GetIDList(out IntPtr idList);
            void SetIDList(IntPtr idList);
            void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder name, int size);
            void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string name);
            void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder dir, int size);
            void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string dir);
            void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder args, int size);
            void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string args);
            void GetHotkey(out short hotkey);
            void SetHotkey(short hotkey);
            void GetShowCmd(out int showCmd);
            void SetShowCmd(int showCmd);
            void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder path, int size, out int index);
            void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string path, int index);
            void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string path, uint reserved);
            void Resolve(IntPtr hwnd, uint flags);
            void SetPath([MarshalAs(UnmanagedType.LPWStr)] string file);
        }

        [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99")]
        interface IPropertyStore
        {
            void GetCount(out uint count);
            void GetAt(uint index, out PropertyKey key);
            void GetValue(ref PropertyKey key, out PropVariant value);
            void SetValue(ref PropertyKey key, ref PropVariant value);
            void Commit();
        }

        [StructLayout(LayoutKind.Sequential, Pack = 4)]
        struct PropertyKey
        {
            public Guid FormatId;
            public uint PropertyId;
        }

        // PROPVARIANT holding a string (or nothing). 24 bytes, the size on 64-bit Windows.
        [StructLayout(LayoutKind.Explicit, Size = 24)]
        struct PropVariant
        {
            [FieldOffset(0)] public ushort Type;
            [FieldOffset(8)] public IntPtr Pointer;
        }
    }
}
