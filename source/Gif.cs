// Stitches JPEG frames into a looping animated GIF using WPF's encoder, then patches in
// the NETSCAPE loop extension and per-frame delays (WPF writes neither).
using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace V8
{
    public static class GifWriter
    {
        public static void Write(IList<string> frames, string outPath, int delayCs, int width)
        {
            var enc = new GifBitmapEncoder();
            foreach (var f in frames)
            {
                var src = new BitmapImage();
                src.BeginInit();
                src.UriSource = new Uri(f);
                src.CacheOption = BitmapCacheOption.OnLoad;
                src.EndInit();
                int w = src.PixelWidth, h = src.PixelHeight;
                var crop = new CroppedBitmap(src, new Int32Rect((int)(w * 0.24), (int)(h * 0.04), (int)(w * 0.56), (int)(h * 0.92)));
                double scale = (double)width / crop.PixelWidth;
                var scaled = new TransformedBitmap(crop, new ScaleTransform(scale, scale));
                enc.Frames.Add(BitmapFrame.Create(scaled));
            }
            byte[] raw;
            using (var ms = new MemoryStream()) { enc.Save(ms); raw = ms.ToArray(); }
            File.WriteAllBytes(outPath, Patch(raw, delayCs));
        }

        static byte[] Patch(byte[] g, int delay)
        {
            var o = new List<byte>(g.Length + 4096);
            int pos = 13;
            int packed = g[10];
            if ((packed & 0x80) != 0) pos += 3 * (1 << ((packed & 7) + 1));
            for (int i = 0; i < pos; i++) o.Add(g[i]);
            o.AddRange(new byte[] { 0x21, 0xFF, 0x0B });
            o.AddRange(System.Text.Encoding.ASCII.GetBytes("NETSCAPE2.0"));
            o.AddRange(new byte[] { 0x03, 0x01, 0x00, 0x00, 0x00 });
            bool haveGce = false;
            while (pos < g.Length)
            {
                byte b = g[pos];
                if (b == 0x21)
                {
                    byte label = g[pos + 1];
                    if (label == 0xFF && pos + 13 < g.Length && System.Text.Encoding.ASCII.GetString(g, pos + 3, 8) == "NETSCAPE")
                    { pos = SkipSubBlocks(g, pos + 2); continue; }   // drop any existing loop block
                    int start = pos;
                    int end = SkipSubBlocks(g, pos + 2);
                    if (label == 0xF9)
                    {
                        haveGce = true;
                        var blk = new byte[end - start];
                        Array.Copy(g, start, blk, 0, blk.Length);
                        blk[3] = (byte)((blk[3] & ~0x1C) | 0x04);   // disposal: do not dispose
                        blk[4] = (byte)(delay & 0xFF);
                        blk[5] = (byte)(delay >> 8);
                        o.AddRange(blk);
                    }
                    else for (int i = start; i < end; i++) o.Add(g[i]);
                    pos = end;
                }
                else if (b == 0x2C)
                {
                    if (!haveGce) o.AddRange(new byte[] { 0x21, 0xF9, 0x04, 0x04, (byte)(delay & 0xFF), (byte)(delay >> 8), 0x00, 0x00 });
                    haveGce = false;
                    int start = pos;
                    int p = pos + 10;
                    int ip = g[pos + 9];
                    if ((ip & 0x80) != 0) p += 3 * (1 << ((ip & 7) + 1));
                    p += 1; // LZW minimum code size
                    int end = SkipSubBlocks(g, p);
                    for (int i = start; i < end; i++) o.Add(g[i]);
                    pos = end;
                }
                else { o.Add(b); pos++; if (b == 0x3B) break; }
            }
            return o.ToArray();
        }

        // p points at the first sub-block size byte; returns index just past the 0 terminator
        static int SkipSubBlocks(byte[] g, int p)
        {
            while (p < g.Length && g[p] != 0) p += g[p] + 1;
            return p + 1;
        }
    }
}
