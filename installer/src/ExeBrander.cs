// Nessacord installer: makes Nessacord.exe, a copy of Discord's exe whose name and icon say
// Nessacord, so Task Manager lists Nessacord. The copy is unsigned. Nessacord only runs Discord's
// main process from it; every other process (and with them Krisp noise suppression, which checks
// Discord's signature) still runs from Discord's own signed exe.

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace Nessacord.Installer
{
    static class ExeBrander
    {
        public const string ExeName = "Nessacord.exe";

        const int RT_ICON = 3, RT_GROUP_ICON = 14, RT_VERSION = 16;

        static readonly Dictionary<string, string> VersionStrings = new Dictionary<string, string>
        {
            { "FileDescription", "Nessacord" },
            { "ProductName", "Nessacord" },
            { "InternalName", "Nessacord" },
            { "OriginalFilename", ExeName }
        };

        /// Writes appFolder\Nessacord.exe from appFolder\<discordExe>, replacing an older one.
        public static string Brand(string appFolder, string discordExe, byte[] ico)
        {
            string source = Path.Combine(appFolder, discordExe);
            string target = Path.Combine(appFolder, ExeName);
            string temp = target + ".tmp";

            File.Copy(source, temp, true);
            try
            {
                StripSignature(temp);

                ResourceInfo info = ReadResources(source);
                byte[] version = RewriteVersion(info.Version);

                IntPtr update = Native.BeginUpdateResource(temp, false);
                if (update == IntPtr.Zero) throw new Win32Exception();
                bool ok = false;
                try
                {
                    Check(Native.UpdateResource(update, (IntPtr)RT_VERSION, (IntPtr)1, info.VersionLanguage, version, version.Length));
                    ReplaceIcon(update, info, ico);
                    ok = true;
                }
                finally
                {
                    if (!Native.EndUpdateResource(update, !ok) && ok) throw new Win32Exception();
                }

                if (File.Exists(target)) File.Delete(target);
                File.Move(temp, target);
                return target;
            }
            finally
            {
                if (File.Exists(temp)) File.Delete(temp);
            }
        }

        static void Check(bool ok)
        {
            if (!ok) throw new Win32Exception();
        }

        // Drops the Authenticode signature (it would no longer match), so the copy is cleanly unsigned.
        static void StripSignature(string path)
        {
            using (var file = new FileStream(path, FileMode.Open, FileAccess.ReadWrite))
            using (var reader = new BinaryReader(file))
            using (var writer = new BinaryWriter(file))
            {
                file.Position = 0x3C;
                int pe = reader.ReadInt32();
                file.Position = pe + 24;
                ushort magic = reader.ReadUInt16();
                long securityEntry = pe + 24 + (magic == 0x20B ? 112 : 96) + 4 * 8;

                file.Position = securityEntry;
                uint offset = reader.ReadUInt32();
                uint size = reader.ReadUInt32();
                if (offset == 0 || size == 0) return;

                file.Position = securityEntry;
                writer.Write(0L);
                if (offset + size >= file.Length) file.SetLength(offset);
            }
        }

        // ── Reading the source exe's resources ──────────────────────────────────────────────

        class ResourceInfo
        {
            public byte[] Version;
            public ushort VersionLanguage;
            public object IconGroupName;   // int or string
            public ushort IconGroupLanguage;
            public List<ushort> IconIds = new List<ushort>();
        }

        static ResourceInfo ReadResources(string exe)
        {
            IntPtr module = Native.LoadLibraryEx(exe, IntPtr.Zero, Native.LOAD_LIBRARY_AS_DATAFILE);
            if (module == IntPtr.Zero) throw new Win32Exception();
            try
            {
                var info = new ResourceInfo();
                info.Version = Load(module, (IntPtr)RT_VERSION, (IntPtr)1);
                info.VersionLanguage = FirstLanguage(module, (IntPtr)RT_VERSION, (IntPtr)1);

                // The first icon group is the one Windows shows for the exe.
                Native.EnumResourceNames(module, (IntPtr)RT_GROUP_ICON, (m, type, name, param) =>
                {
                    info.IconGroupName = IsIntResource(name) ? (object)(int)name : Marshal.PtrToStringUni(name);
                    return false;
                }, IntPtr.Zero);
                if (info.IconGroupName == null) throw new InvalidDataException("Discord's exe has no icon.");

                using (var name = new ResourceName(info.IconGroupName))
                {
                    info.IconGroupLanguage = FirstLanguage(module, (IntPtr)RT_GROUP_ICON, name.Pointer);
                    byte[] group = Load(module, (IntPtr)RT_GROUP_ICON, name.Pointer);
                    int count = BitConverter.ToUInt16(group, 4);
                    for (int i = 0; i < count; i++) info.IconIds.Add(BitConverter.ToUInt16(group, 6 + i * 14 + 12));
                }
                return info;
            }
            finally
            {
                Native.FreeLibrary(module);
            }
        }

        static bool IsIntResource(IntPtr name)
        {
            return ((long)name >> 16) == 0;
        }

        static byte[] Load(IntPtr module, IntPtr type, IntPtr name)
        {
            IntPtr found = Native.FindResource(module, name, type);
            if (found == IntPtr.Zero) throw new Win32Exception();
            int size = Native.SizeofResource(module, found);
            IntPtr data = Native.LockResource(Native.LoadResource(module, found));
            var bytes = new byte[size];
            Marshal.Copy(data, bytes, 0, size);
            return bytes;
        }

        static ushort FirstLanguage(IntPtr module, IntPtr type, IntPtr name)
        {
            ushort language = 0;
            Native.EnumResourceLanguages(module, type, name, (m, t, n, lang, param) =>
            {
                language = lang;
                return false;
            }, IntPtr.Zero);
            return language;
        }

        sealed class ResourceName : IDisposable
        {
            public readonly IntPtr Pointer;
            readonly bool owned;

            public ResourceName(object name)
            {
                if (name is int) Pointer = (IntPtr)(int)name;
                else
                {
                    Pointer = Marshal.StringToHGlobalUni((string)name);
                    owned = true;
                }
            }

            public void Dispose()
            {
                if (owned) Marshal.FreeHGlobal(Pointer);
            }
        }

        // ── Icon ────────────────────────────────────────────────────────────────────────────

        // Replaces the main icon group's images with the .ico's, under the same group name.
        static void ReplaceIcon(IntPtr update, ResourceInfo info, byte[] ico)
        {
            foreach (ushort id in info.IconIds)
                Check(Native.UpdateResource(update, (IntPtr)RT_ICON, (IntPtr)id, info.IconGroupLanguage, null, 0));

            int count = BitConverter.ToUInt16(ico, 4);
            var group = new MemoryStream();
            var writer = new BinaryWriter(group);
            writer.Write((ushort)0);
            writer.Write((ushort)1);
            writer.Write((ushort)count);

            ushort nextId = 1;
            foreach (ushort id in info.IconIds) nextId = Math.Max(nextId, (ushort)(id + 1));

            for (int i = 0; i < count; i++)
            {
                int entry = 6 + i * 16;
                int size = BitConverter.ToInt32(ico, entry + 8);
                int offset = BitConverter.ToInt32(ico, entry + 12);
                ushort id = i < info.IconIds.Count ? info.IconIds[i] : nextId++;

                var image = new byte[size];
                Array.Copy(ico, offset, image, 0, size);
                Check(Native.UpdateResource(update, (IntPtr)RT_ICON, (IntPtr)id, info.IconGroupLanguage, image, size));

                writer.Write(ico, entry, 12);
                writer.Write(id);
            }

            byte[] groupBytes = group.ToArray();
            using (var name = new ResourceName(info.IconGroupName))
                Check(Native.UpdateResource(update, (IntPtr)RT_GROUP_ICON, name.Pointer, info.IconGroupLanguage, groupBytes, groupBytes.Length));
        }

        // ── Version information ─────────────────────────────────────────────────────────────
        // VS_VERSIONINFO is a tree of nodes: length, value length, type, key, value, children,
        // each part aligned to 4 bytes.

        class VersionNode
        {
            public string Key;
            public ushort Type;
            public byte[] Value;
            public readonly List<VersionNode> Children = new List<VersionNode>();
        }

        static byte[] RewriteVersion(byte[] data)
        {
            int pos = 0;
            VersionNode root = ParseNode(data, ref pos);
            SetStrings(root);
            return WriteNode(root);
        }

        static int Align(int value)
        {
            return (value + 3) & ~3;
        }

        static VersionNode ParseNode(byte[] data, ref int pos)
        {
            int start = pos;
            int length = BitConverter.ToUInt16(data, pos);
            int valueLength = BitConverter.ToUInt16(data, pos + 2);
            var node = new VersionNode { Type = BitConverter.ToUInt16(data, pos + 4) };
            int end = start + length;

            pos += 6;
            var key = new StringBuilder();
            while (pos + 1 < end)
            {
                char c = BitConverter.ToChar(data, pos);
                pos += 2;
                if (c == '\0') break;
                key.Append(c);
            }
            node.Key = key.ToString();
            pos = Align(pos);

            int valueBytes = Math.Min(node.Type == 1 ? valueLength * 2 : valueLength, Math.Max(0, end - pos));
            node.Value = new byte[valueBytes];
            Array.Copy(data, pos, node.Value, 0, valueBytes);
            pos = Align(pos + valueBytes);

            while (pos < end)
            {
                int before = pos;
                node.Children.Add(ParseNode(data, ref pos));
                if (pos <= before) break;
            }

            pos = Align(end);
            return node;
        }

        static void SetStrings(VersionNode node)
        {
            string text;
            if (node.Type == 1 && node.Children.Count == 0 && VersionStrings.TryGetValue(node.Key, out text))
                node.Value = Encoding.Unicode.GetBytes(text + "\0");

            foreach (VersionNode child in node.Children) SetStrings(child);
        }

        static byte[] WriteNode(VersionNode node)
        {
            var stream = new MemoryStream();
            var writer = new BinaryWriter(stream);
            writer.Write((ushort)0);
            writer.Write((ushort)(node.Type == 1 ? node.Value.Length / 2 : node.Value.Length));
            writer.Write(node.Type);
            writer.Write(Encoding.Unicode.GetBytes(node.Key + "\0"));
            Pad(stream);
            writer.Write(node.Value);

            foreach (VersionNode child in node.Children)
            {
                Pad(stream);
                writer.Write(WriteNode(child));
            }

            byte[] bytes = stream.ToArray();
            BitConverter.GetBytes((ushort)bytes.Length).CopyTo(bytes, 0);
            return bytes;
        }

        static void Pad(MemoryStream stream)
        {
            while (stream.Length % 4 != 0) stream.WriteByte(0);
        }
    }
}
