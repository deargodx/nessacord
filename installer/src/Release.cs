// Nessacord installer: Nessacord's files, from the latest GitHub release or a local build.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;

namespace Nessacord.Installer
{
    static class Release
    {
        public const string Repo = "SerenityDiscko/nessacord";
        const string Checksums = "SHA256SUMS";

        // The files Nessacord's updater replaces too (src/main/updater/common.ts).
        public static readonly string[] Files = { "patcher.js", "preload.js", "renderer.js", "renderer.css" };

        public static void CopyFrom(string source, string dist)
        {
            foreach (string name in Files)
                if (!File.Exists(Path.Combine(source, name))) throw new FileNotFoundException("The build folder has no " + name, Path.Combine(source, name));

            Replace(dist, Files.ToDictionary(name => name, name => File.ReadAllBytes(Path.Combine(source, name))));
        }

        public static void Download(string dist, Action<string> log)
        {
            ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072; // TLS 1.2

            log("Finding the latest Nessacord release");
            var release = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(
                Encoding.UTF8.GetString(Get("https://api.github.com/repos/" + Repo + "/releases/latest")));

            var urls = new Dictionary<string, string>();
            foreach (Dictionary<string, object> asset in (System.Collections.ArrayList)release["assets"])
                urls[(string)asset["name"]] = (string)asset["browser_download_url"];

            if (!urls.ContainsKey(Checksums)) throw new InvalidDataException("The latest release has no " + Checksums + ", so its files can't be checked.");
            Dictionary<string, string> expected = ParseChecksums(Encoding.UTF8.GetString(Get(urls[Checksums])));

            var files = new Dictionary<string, byte[]>();
            foreach (string name in Files)
            {
                if (!urls.ContainsKey(name)) throw new InvalidDataException("The latest release is missing " + name);
                log("Downloading " + name);
                byte[] data = Get(urls[name]);

                string hash;
                if (!expected.TryGetValue(name, out hash) || hash != Sha256(data))
                    throw new InvalidDataException(name + " doesn't match the release's checksum. Nothing was changed.");
                files[name] = data;
            }

            Replace(dist, files);
            log("Downloaded " + release["name"]);
        }

        // All files or none: written next to the old ones, then swapped in.
        static void Replace(string dist, Dictionary<string, byte[]> files)
        {
            Directory.CreateDirectory(dist);
            foreach (KeyValuePair<string, byte[]> file in files)
                File.WriteAllBytes(Path.Combine(dist, file.Key + ".new"), file.Value);
            foreach (string name in files.Keys)
            {
                string path = Path.Combine(dist, name);
                if (File.Exists(path)) File.Delete(path);
                File.Move(path + ".new", path);
            }
        }

        static byte[] Get(string url)
        {
            var request = (HttpWebRequest)WebRequest.Create(url);
            request.UserAgent = "Nessacord-Installer";
            request.Accept = url.Contains("api.github.com") ? "application/vnd.github+json" : "application/octet-stream";
            request.Timeout = 60000;
            using (var response = (HttpWebResponse)request.GetResponse())
            using (Stream stream = response.GetResponseStream())
            {
                var buffer = new MemoryStream();
                stream.CopyTo(buffer);
                return buffer.ToArray();
            }
        }

        // "sha256  name" per line, as sha256sum writes them.
        static Dictionary<string, string> ParseChecksums(string text)
        {
            var sums = new Dictionary<string, string>();
            foreach (string line in text.Split('\n'))
            {
                string[] parts = line.Trim().Split(new[] { ' ', '*' }, 2, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 2) sums[parts[1].Trim().TrimStart('*')] = parts[0].ToLowerInvariant();
            }
            return sums;
        }

        static string Sha256(byte[] data)
        {
            using (var sha = SHA256.Create())
                return string.Concat(sha.ComputeHash(data).Select(b => b.ToString("x2")));
        }
    }
}
