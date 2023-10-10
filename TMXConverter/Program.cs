using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Drawing;
using System.Collections;
using System.Security.Cryptography;
using Pfim;
using System.Reflection.Metadata;
using System.Runtime.InteropServices;
using System.Drawing.Imaging;
using ImageFormat = Pfim.ImageFormat;
using static System.Net.Mime.MediaTypeNames;
using System.Diagnostics;

namespace TMXConverter
{
    class Program
    {
        static char[] trimChars = {'\"', '\''};

        static void Main(string[] args)
        {
            //TmxToPng(args[0], Path.ChangeExtension(args[0], ".png"));
            //return;
            if (args.Length == 0)
            {
                Console.WriteLine("usage: TMXConverter imagefile(png, jpeg, bmp or tga)");
                Console.WriteLine("      -o -output: sets name of file output");
                Console.WriteLine("      -u -userid: sets user id of TMX (defaults to 1)");
                Console.WriteLine("      -c -comment: sets user comment (defaults to filename)");
                Console.WriteLine("      -ns -nosolidify: removes solidifying of texture edge");
                return;
            }
            else
            {
                var processedArgs = ProcessArgs(args);
                string input = processedArgs["i"];
                short userID = 1;
                string comment = Path.GetFileNameWithoutExtension(input);

                string output = Path.ChangeExtension(input, "tmx");

                if (Path.GetExtension(input).ToLower() == ".tmx")
                    output = Path.ChangeExtension(input, ".png");

                if (processedArgs.TryGetValue("userid", out string? argUserID) || processedArgs.TryGetValue("u", out argUserID) && argUserID != string.Empty)
                    userID = short.Parse(argUserID.Trim(trimChars));

                if (processedArgs.TryGetValue("comment", out string? argComment) || processedArgs.TryGetValue("c", out argComment) && argComment != string.Empty)
                    comment = argComment.Trim(trimChars);

                if (processedArgs.TryGetValue("output", out string? argOutput) || processedArgs.TryGetValue("o", out argOutput) && argOutput != string.Empty)
                    output = argOutput.Trim(trimChars);

                bool solidify = !processedArgs.ContainsKey("nosolidify") && !processedArgs.ContainsKey("ns");

                if (!Path.Exists(input)) { Console.WriteLine("Could not find file"); return; }
                if (Path.GetDirectoryName(output) != "" && !Path.Exists(Path.GetDirectoryName(output))) { Console.WriteLine("Output folder does not exist"); return; }

                if (Path.GetExtension(input).ToLower() == ".tmx")
                {
                    var png = TmxToPng(input);
                    png.Save(output);
                }
                else
                {
                    BitmapToTMX(input, output, comment, userID, solidify);
                }
            }

        }

        static void BitmapToTMX(string input, string output, string comment, int userID, bool solidify)
        {
            if (comment.Length > 28) { comment = comment.Substring(0, 28); }
            Bitmap image;
            if (solidify)
            {
                ProcessStartInfo startInfo = new ProcessStartInfo();
                startInfo.FileName = "gmic\\gmic.exe";
                string newfile = Path.GetFileNameWithoutExtension(input).Replace(" ", "") + "-Solidify.png";
                startInfo.Arguments = "\"" + input + "\" solidify , output " + newfile;
                using (Process exeProcess = Process.Start(startInfo))
                {
                    exeProcess.WaitForExit();
                }
                image = (Path.GetExtension(input).ToLower() == ".tga") ? ToBitmap(Pfimage.FromFile(input)) : new Bitmap(input, true);

                Bitmap image2 = new Bitmap(newfile, true);
                Color c;
                for (int y = 0; y < image.Height; y += 1)
                {
                    for (int x = 0; x < image.Width; x += 1)
                    {
                        c = image2.GetPixel(x, y);
                        c = Color.FromArgb(image.GetPixel(x, y).A, c.R, c.G, c.B);
                        image.SetPixel(x, y, c);
                    }
                }
                image2.Dispose();
                File.Delete(newfile);
            }
            else { image = (Path.GetExtension(input).ToLower() == ".tga") ? ToBitmap(Pfimage.FromFile(input)) : new Bitmap(input, true); }

            Color[] tmxbyte = new Color[(image.Width - (image.Width % 8)) * (image.Height - (image.Height % 2))];
            int num = 0;

            for (int y = 0; y < image.Height - (image.Height % 2); y += 1)
            {
                for (int x = 0; x + 7 < image.Width; x += 8)
                {
                    tmxbyte[num] = image.GetPixel(x, y);
                    tmxbyte[num + 1] = image.GetPixel(x + 1, y);
                    tmxbyte[num + 2] = image.GetPixel(x + 2, y);
                    tmxbyte[num + 3] = image.GetPixel(x + 3, y);
                    tmxbyte[num + 4] = image.GetPixel(x + 4, y);
                    tmxbyte[num + 5] = image.GetPixel(x + 5, y);
                    tmxbyte[num + 6] = image.GetPixel(x + 6, y);
                    tmxbyte[num + 7] = image.GetPixel(x + 7, y);
                    num += 8;
                }
            }
            using (var fs = new FileStream(output, FileMode.Create, FileAccess.Write))
            {
                BinaryWriter bw = new BinaryWriter(fs);
                bw.Write((short)0x0002);
                bw.Write((short)userID);
                bw.Write(64 + (4 * tmxbyte.Length));
                bw.Write(0x30584D54); //TMX0
                bw.Seek(6, SeekOrigin.Current);
                bw.Write((short)(image.Width - (image.Width % 8)));
                bw.Write((short)(image.Height - (image.Height % 2)));
                bw.Write((short)0);
                bw.Write(0xFF000000);
                bw.Seek(8, SeekOrigin.Current);
                bw.Write(Encoding.ASCII.GetBytes(comment.PadRight(28, '\0')));
                for (int i = 0; i < tmxbyte.Length; i++)
                {
                    bw.Write((byte)((tmxbyte[i].R)));
                    bw.Write((byte)((tmxbyte[i].G)));
                    bw.Write((byte)((tmxbyte[i].B)));
                    bw.Write((byte)((tmxbyte[i].A * 128 / 255)));

                }

            }
        }
        static Dictionary<string, string> ProcessArgs(string[] args)
        {
            var processedArgs = new Dictionary<string, string>();
            processedArgs["i"] = args[0];

            for (int i = 1; i < args.Length; i++)
            {
                if (args[i].StartsWith('-'))
                {
                    string key = args[i][1..];

                    if (i + 1 >= args.Length || args[i + 1].StartsWith('-'))
                    {
                        processedArgs[key] = string.Empty;
                        continue;
                    }

                    processedArgs[key] = args[i + 1].ToLower();
                    i++;
                }
            }

            return processedArgs;
        }

        private static Bitmap ToBitmap(IImage image)
        {
           
            PixelFormat format;
            switch (image.Format)
            {
                case ImageFormat.Rgb24:
                    format = PixelFormat.Format24bppRgb;
                    break;

                case ImageFormat.Rgba32:
                    format = PixelFormat.Format32bppArgb;
                    break;

                case ImageFormat.R5g5b5:
                    format = PixelFormat.Format16bppRgb555;
                    break;

                case ImageFormat.R5g6b5:
                    format = PixelFormat.Format16bppRgb565;
                    break;

                case ImageFormat.R5g5b5a1:
                    format = PixelFormat.Format16bppArgb1555;
                    break;

                case ImageFormat.Rgb8:
                    format = PixelFormat.Format8bppIndexed;
                    break;

                default:
                    var caption = "Unrecognized format";
                    Console.Write(caption);
                    image.Dispose();
                    return null;
            }
            
            var ptr = Marshal.UnsafeAddrOfPinnedArrayElement(image.Data, 0);
            var bitmap = new Bitmap(image.Width, image.Height, image.Stride, format, ptr);
            image.Dispose();
            return bitmap;
        }

        static Bitmap TmxToPng(string tmxPath)
        {
            int width;
            int height;

            using var fs = new FileStream(tmxPath, FileMode.Open);

            byte[] dimensionBuffer = new byte[2];

            fs.Seek(0x12, SeekOrigin.Begin);

            fs.Read(dimensionBuffer);
            width = BitConverter.ToInt16(dimensionBuffer);

            fs.Read(dimensionBuffer);
            height = BitConverter.ToInt16(dimensionBuffer);

            Bitmap png = new Bitmap(width, height, PixelFormat.Format32bppArgb);

            png.MakeTransparent(Color.Transparent);

            Color[] pixels = new Color[width * height];

            fs.Seek(0x40, SeekOrigin.Begin);

            for (int i = 0; i < width * height; i++)
            {
                byte r = (byte)fs.ReadByte();
                byte g = (byte)fs.ReadByte();
                byte b = (byte)fs.ReadByte();
                byte a = (byte)(fs.ReadByte() / 128 * 255);

                pixels[i] = Color.FromArgb(a, r, g, b);
            }


            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                    png.SetPixel(x, y, pixels[x + (y * width)]);

            return png;

        }
    }
}