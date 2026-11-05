using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace EfTurrets
{
    // The towers' pictures are in the game's own sprite format (tools/assets/make_assets.py), shipped in
    // v_common/Sprites. The game only loads sprites from StreamingAssets/sprite/<path>, so the player path is written
    // relative to that folder ("../../../mods/..."). The image name inside each .bin is a placeholder that is replaced
    // here with the same relative folder, and the result is written next to it as ef_<id>.rt.bin.
    public static class EfSprites
    {
        public const string Placeholder = "@EF_SPRITE_DIR@/";
        static readonly Dictionary<string, string> players = new Dictionary<string, string>();
        public static string Problem;   // set when the pictures could not be prepared (shown by the self-test)

        public static string PlayerPath(string towerId) => towerId != null && players.TryGetValue(towerId, out var p) ? p : null;

        public static void Prepare(ModXmlManifest cfg)
        {
            players.Clear(); Problem = null;
            string dir = cfg.getVersionPath("Sprites");
            string root = Path.Combine(Application.streamingAssetsPath, "sprite");
            string rel = Relative(root, dir);
            if (rel == null) { Problem = "sprite folder " + dir + " is on another drive than " + root; Debug.LogError("[EfTurrets] " + Problem); return; }
            var names = new List<string>(); foreach (var t in EfData.Towers) names.Add(t.Id);
            foreach (var id in names)
            {
                string src = Path.Combine(dir, "ef_" + id + ".bin");
                if (!File.Exists(src)) { Problem = "missing " + src; Debug.LogError("[EfTurrets] " + Problem); continue; }
                byte[] data = File.ReadAllBytes(src);
                byte[] fixedBin = ReplaceImageNames(data, Placeholder, rel);
                string dst = Path.Combine(dir, "ef_" + id + ".rt.bin");
                if (!File.Exists(dst) || !Same(File.ReadAllBytes(dst), fixedBin)) File.WriteAllBytes(dst, fixedBin);
                players[id] = rel + "ef_" + id + ".rt";
            }
            Debug.Log("[EfTurrets] sprites from " + rel + " (" + players.Count + ")");
        }

        // Header of a long-image-array sprite file: int image count, then that many (u16 length + UTF-8) names.
        static byte[] ReplaceImageNames(byte[] data, string from, string to)
        {
            int n = (data[0] << 24) | (data[1] << 16) | (data[2] << 8) | data[3];
            int p = 4; var outp = new MemoryStream(); outp.Write(data, 0, 4);
            for (int i = 0; i < n; i++)
            {
                int len = (data[p] << 8) | data[p + 1];
                string name = Encoding.UTF8.GetString(data, p + 2, len).Replace(from, to);
                byte[] nb = Encoding.UTF8.GetBytes(name);
                outp.WriteByte((byte)(nb.Length >> 8)); outp.WriteByte((byte)nb.Length); outp.Write(nb, 0, nb.Length);
                p += 2 + len;
            }
            outp.Write(data, p, data.Length - p);
            return outp.ToArray();
        }

        static bool Same(byte[] a, byte[] b)
        {
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
            return true;
        }

        // Relative path from folder "from" to folder "to", with forward slashes and a trailing slash; null across drives.
        public static string Relative(string from, string to)
        {
            string a = Path.GetFullPath(from).Replace('\\', '/').TrimEnd('/'), b = Path.GetFullPath(to).Replace('\\', '/').TrimEnd('/');
            string[] pa = a.Split('/'), pb = b.Split('/');
            if (pa.Length == 0 || pb.Length == 0 || !string.Equals(pa[0], pb[0], StringComparison.OrdinalIgnoreCase)) return null;
            int i = 0;
            while (i < pa.Length && i < pb.Length && string.Equals(pa[i], pb[i], StringComparison.OrdinalIgnoreCase)) i++;
            var sb = new StringBuilder();
            for (int k = i; k < pa.Length; k++) sb.Append("../");
            for (int k = i; k < pb.Length; k++) sb.Append(pb[k]).Append('/');
            return sb.ToString();
        }
    }
}
