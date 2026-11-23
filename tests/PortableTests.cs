using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;
using GrasshopperFilePreview;
using Microsoft.Win32;

static class PortableTests
{
    static int passed;
    static void Check(bool condition, string message)
    {
        if (!condition)
            throw new Exception(message);
    }
    static void Pass(string name)
    {
        passed++;
        Console.WriteLine("PASS " + name);
    }
    static void Reject(byte[] bytes, string name)
    {
        bool rejected = false;
        try
        {
            using (var stream = new MemoryStream(bytes))
            using (Bitmap image = BitmapReader.Read(stream))
            {
            }
        }
        catch (InvalidDataException) { rejected = true; }
        catch (System.Xml.XmlException) { rejected = true; }
        catch (ArgumentException) { rejected = true; }
        Check(rejected, "Expected rejection: " + name);
        Pass(name);
    }
    static byte[] Xml(byte[] png)
    {
        return Encoding.UTF8.GetBytes("<Archive><chunks><chunk name='Thumbnail'><items><item name='Thumbnail'><bitmap>" +
            Convert.ToBase64String(png) + "</bitmap></item></items></chunk></chunks></Archive>");
    }
    static byte[] Binary(byte[] png, int prefix, int length)
    {
        using (var output = new MemoryStream())
        {
            using (var zip = new DeflateStream(output, CompressionMode.Compress, true))
            using (var writer = new BinaryWriter(zip))
            {
                writer.Write(new byte[prefix]);
                writer.Write("Thumbnail");
                writer.Write(-1);
                writer.Write(1);
                writer.Write(0);
                writer.Write("Thumbnail");
                writer.Write(-1);
                writer.Write(37);
                writer.Write(length);
                writer.Write(png);
                writer.Write("Trailing bytes");
            }
            return output.ToArray();
        }
    }
    static void Equal(Bitmap expected, Bitmap actual)
    {
        Check(actual != null && expected.Size == actual.Size, "Image dimensions differ.");
        for (int y = 0; y < expected.Height; y++)
            for (int x = 0; x < expected.Width; x++)
                Check(expected.GetPixel(x, y).ToArgb() == actual.GetPixel(x, y).ToArgb(), "Image pixels differ.");
    }
    static int Main(string[] args)
    {
        try
        {
            Reader(args[0]);
            passed += ThumbnailTests.Run(args[0]);
            passed += StreamTests.Run();
            passed += SetupTests.Run();
            RegistryRollback();
            RegistrationPaths();
            OverlayPlanning();
            Console.WriteLine("PASS: " + passed + " portable cases");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
    static void Reader(string directory)
    {
        byte[] png = File.ReadAllBytes(Path.Combine(directory, "Expected.png"));
        using (var expected = new Bitmap(Path.Combine(directory, "Expected.png")))
        {
            foreach (string extension in new[] { ".gh", ".ghx" })
            {
                using (var file = File.OpenRead(Path.Combine(directory, "Embedded bitmap" + extension)))
                using (var actual = BitmapReader.Read(file))
                    Equal(expected, actual);
                Pass("original GH_IO fixture pixels " + extension);
                using (var file = File.OpenRead(Path.Combine(directory, "No bitmap" + extension)))
                using (var actual = BitmapReader.Read(file))
                    Check(actual == null, "Missing bitmap must return null.");
                Pass("missing bitmap " + extension);
            }
            foreach (int prefix in new[] { 0, 65520, 65535, 65536, 131060 })
                using (var data = new MemoryStream(Binary(png, prefix, png.Length)))
                using (var actual = BitmapReader.Read(data))
                {
                    Equal(expected, actual);
                    Pass("binary boundary " + prefix);
                }
            foreach (Encoding encoding in new Encoding[] { new UTF8Encoding(true), Encoding.Unicode, Encoding.BigEndianUnicode })
            {
                byte[] data = encoding.GetPreamble().Concat(encoding.GetBytes(Encoding.UTF8.GetString(Xml(png)))).ToArray();
                using (var stream = new MemoryStream(data))
                using (var actual = BitmapReader.Read(stream))
                    Equal(expected, actual);
                Pass("XML encoding " + encoding.WebName);
            }
        }
        Reject(Encoding.UTF8.GetBytes("<!DOCTYPE Archive [<!ENTITY x SYSTEM 'file:///must-not-read'>]><Archive>&x;</Archive>"), "DTD prohibited");
        Reject(Encoding.UTF8.GetBytes("<Archive><chunks><chunk name='Thumbnail'><item name='Thumbnail'><bitmap>@@@</bitmap></item></chunk></chunks></Archive>"), "malformed base64");
        Reject(Xml(new byte[] { 1 }), "invalid PNG");
        var huge = (byte[])png.Clone();
        huge[16] = 0x7f;
        Reject(Xml(huge), "oversized PNG dimensions");
        Reject(Binary(png, 0, BitmapReader.MaxImage + 1), "oversized binary image length");
        Reject(Binary(png, 0, -1), "negative binary image length");
        Reject(Binary(new byte[10], 0, 500), "truncated binary image");
        using (var cancel = new CancellationTokenSource())
        using (var stream = new MemoryStream(png))
        {
            cancel.Cancel();
            bool cancelled = false;
            try
            {
                using (Bitmap image = BitmapReader.Read(stream, cancel.Token))
                {
                }
            }
            catch (OperationCanceledException) { cancelled = true; }
            Check(cancelled, "Pre-cancellation was ignored.");
            Pass("pre-cancelled read");
        }
        using (var cancel = new CancellationTokenSource())
        using (var stream = new CancellingStream(Binary(png, 1024 * 1024, png.Length), cancel))
        {
            bool cancelled = false;
            try
            {
                using (Bitmap image = BitmapReader.Read(stream, cancel.Token))
                {
                }
            }
            catch (OperationCanceledException) { cancelled = true; }
            Check(cancelled, "Mid-read cancellation was ignored.");
            Pass("mid-read cancellation");
        }
        using (var stream = new LengthOnlyStream())
        {
            bool rejected = false;
            try
            {
                using (Bitmap image = BitmapReader.Read(stream))
                {
                }
            }
            catch (InvalidDataException) { rejected = true; }
            Check(rejected, "Oversized file was accepted.");
            Pass("file size limit before reading");
        }
        // A small compressed input must not expand without bound inside the Shell host.
        using (var compressed = new MemoryStream())
        {
            using (var deflate = new DeflateStream(compressed, CompressionMode.Compress, true))
            {
                var zeros = new byte[65536];
                for (int written = 0; written <= BitmapReader.MaxArchive; written += zeros.Length)
                    deflate.Write(zeros, 0, zeros.Length);
            }
            Reject(compressed.ToArray(), "expanded archive size limit");
        }
    }
    sealed class CancellingStream : MemoryStream
    {
        readonly CancellationTokenSource cancel;
        public CancellingStream(byte[] bytes, CancellationTokenSource cancel) : base(bytes) { this.cancel = cancel; }
        public override int Read(byte[] buffer, int offset, int count)
        {
            int read = base.Read(buffer, offset, count);
            cancel.Cancel();
            return read;
        }
    }
    sealed class LengthOnlyStream : MemoryStream
    {
        public override long Length
        {
            get
            {
                return (long)BitmapReader.MaxArchive + 1;
            }
        }
        public override int ReadByte()
        {
            throw new Exception("Oversized stream must not be read.");
        }
    }
    static void RegistryRollback()
    {
        string path = @"Software\GrasshopperFilePreview.Tests\" + Guid.NewGuid().ToString("N");
        string backup = Path.Combine(Path.GetTempPath(), "Grasshopper-registry-" + Guid.NewGuid().ToString("N") + ".xml");
        try
        {
            using (var root = Registry.CurrentUser.CreateSubKey(path))
            {
                using (var original = root.CreateSubKey("Existing"))
                {
                    original.SetValue("Text", "%TEMP%\\original", RegistryValueKind.ExpandString);
                    original.SetValue("Number", 42, RegistryValueKind.DWord);
                    original.SetValue("Keep", "untouched");
                }
                try
                {
                    RegistryJournal.Execute(root, journal =>
                    {
                        journal.Set("Existing", "Text", "replacement");
                        journal.Set("Existing", "Text", "second replacement");
                        journal.RemoveValue("Existing", "Number");
                        journal.Set(@"New\Nested", "", "new value");
                        journal.ClearOwnedTree("Existing");
                        throw new InvalidOperationException("Injected failure");
                    });
                }
                catch (InvalidOperationException) { }
                using (var original = root.OpenSubKey("Existing"))
                {
                    Check((string)original.GetValue("Text", null, RegistryValueOptions.DoNotExpandEnvironmentNames) == "%TEMP%\\original" && original.GetValueKind("Text") == RegistryValueKind.ExpandString, "String value/kind not restored.");
                    Check((int)original.GetValue("Number") == 42 && (string)original.GetValue("Keep") == "untouched", "Original values lost.");
                }
                using (var created = root.OpenSubKey("New"))
                    Check(created == null, "New keys survived rollback.");
                Pass("registry failure restores values, kinds and keys");
                RegistryJournal.Execute(root, journal => { journal.Set("Existing", "Text", "committed"); journal.RemoveValue("Existing", "Number"); });
                using (var original = root.OpenSubKey("Existing"))
                    Check((string)original.GetValue("Text") == "committed" && original.GetValue("Number") == null && (string)original.GetValue("Keep") == "untouched", "Successful changes not committed.");
                Pass("registry success preserves unrelated values");
                using (var original = root.OpenSubKey("Existing", true))
                {
                    original.SetValue("Expanded", "%TEMP%\\original", RegistryValueKind.ExpandString);
                    original.SetValue("Number", -42, RegistryValueKind.DWord);
                    original.SetValue("Large", long.MaxValue, RegistryValueKind.QWord);
                    original.SetValue("Bytes", new byte[] { 0, 255, 42 }, RegistryValueKind.Binary);
                    original.SetValue("Untyped", new byte[] { 0, 255 }, RegistryValueKind.None);
                    original.SetValue("List", new[] { "first", "second" }, RegistryValueKind.MultiString);
                }
                RegistryJournal.Execute(root, journal =>
                {
                    journal.ClearOwnedTree("Existing");
                    journal.Set(@"New\Nested", "", "new registration");
                }, backup);
                RegistryJournal.Restore(root, backup);
                using (var original = root.OpenSubKey("Existing"))
                {
                    Check((string)original.GetValue("Expanded", null, RegistryValueOptions.DoNotExpandEnvironmentNames) == "%TEMP%\\original" && original.GetValueKind("Expanded") == RegistryValueKind.ExpandString, "Saved expandable string changed.");
                    Check((int)original.GetValue("Number") == -42 && (long)original.GetValue("Large") == long.MaxValue, "Saved integers changed.");
                    Check(((byte[])original.GetValue("Bytes"))[1] == 255 && ((string[])original.GetValue("List"))[1] == "second", "Saved arrays changed.");
                    Check(((byte[])original.GetValue("Untyped"))[1] == 255 && original.GetValueKind("Untyped") == RegistryValueKind.None, "Saved untyped bytes changed.");
                    Check((string)original.GetValue("Text") == "committed" && (string)original.GetValue("Keep") == "untouched", "Saved strings changed.");
                }
                using (var created = root.OpenSubKey("New"))
                    Check(created == null, "Saved rollback left new keys.");
                Pass("persisted registry rollback restores values, kinds and keys");
                bool rejected = false;
                try
                {
                    RegistryJournal.Execute(root, journal => journal.Set("Existing", "Text", "must roll back"), Path.Combine(backup, "invalid.xml"));
                }
                catch (IOException) { rejected = true; }
                using (var original = root.OpenSubKey("Existing"))
                    Check(rejected && (string)original.GetValue("Text") == "committed", "Snapshot failure left registration changed.");
                Pass("snapshot write failure rolls back registration immediately");
                using (var empty = root.CreateSubKey(@"Stale\Nested")) { }
                Registration.PruneEmptyTree(root, "Stale");
                Registration.PruneEmptyTree(root, "Existing");
                using (var empty = root.OpenSubKey("Stale"))
                using (var existing = root.OpenSubKey("Existing"))
                    Check(empty == null && existing != null && (string)existing.GetValue("Keep") == "untouched", "Empty-key cleanup removed live values or retained empty keys.");
                Pass("scope migration removes empty keys and preserves live values");
                const string classes = @"Software\Classes\";
                const string thumbnail = @"\shellex\{E357FCCD-A995-4576-B01F-234630154E96}";
                using (var association = root.CreateSubKey(classes + ".gh"))
                    association.SetValue("", "OtherApp.Grasshopper");
                using (var empty = root.CreateSubKey(classes + @".gh\OpenWithList\OtherApp.exe")) { }
                using (var empty = root.CreateSubKey(classes + @".gh\shellex\OtherHandler")) { }
                using (var empty = root.CreateSubKey(classes + ".gh" + thumbnail)) { }
                using (var empty = root.CreateSubKey(classes + ".ghx" + thumbnail)) { }
                Registration.PruneEmptyRegistration(root);
                using (var association = root.OpenSubKey(classes + ".gh"))
                using (var openWith = root.OpenSubKey(classes + @".gh\OpenWithList\OtherApp.exe"))
                using (var otherHandler = root.OpenSubKey(classes + @".gh\shellex\OtherHandler"))
                using (var removed = root.OpenSubKey(classes + ".gh" + thumbnail))
                using (var emptyExtension = root.OpenSubKey(classes + ".ghx"))
                    Check(association != null && (string)association.GetValue("") == "OtherApp.Grasshopper" &&
                        openWith != null && otherHandler != null && removed == null && emptyExtension == null,
                        "Handler cleanup changed another application's association keys.");
                Pass("handler cleanup preserves unrelated empty association keys");
            }
        }
        finally { Registry.CurrentUser.DeleteSubKeyTree(path, false); File.Delete(backup); }
    }
    static void RegistrationPaths()
    {
        string path = @"Software\GrasshopperFilePreview.Tests\" + Guid.NewGuid().ToString("N");
        string folder = Path.Combine(Path.GetTempPath(), "Grasshopper-path-" + Guid.NewGuid().ToString("N") + " %20 # \u03a9");
        string dll = Path.Combine(folder, "GrasshopperFilePreview.dll");
        Directory.CreateDirectory(folder);
        try
        {
            File.Copy(typeof(BitmapReader).Assembly.Location, dll);
            using (var root = Registry.CurrentUser.CreateSubKey(path))
            {
                RegistryJournal.Execute(root, journal =>
                {
                    var assembly = System.Reflection.AssemblyName.GetAssemblyName(dll);
                    Registration.RegisterServer(journal, Registration.Id, assembly, "GrasshopperFilePreview.Handler");
                    Registration.RegisterServer(journal, Registration.ThumbnailId, assembly, "GrasshopperFilePreview.ThumbnailHandler");
                    journal.Set(@"Software\GrasshopperBitmapPreview", "InstallDirectory", folder);
                    foreach (string extension in new[] { ".gh", ".ghx" })
                    {
                        journal.Set(@"Software\Classes\" + extension + @"\shellex\{8895B1C6-B41F-4C1C-A562-0D564250836F}", "", Registration.Id);
                        journal.Set(@"Software\Classes\" + extension + @"\shellex\{E357FCCD-A995-4576-B01F-234630154E96}", "", Registration.ThumbnailId);
                    }
                });
                Check(Registration.IsInstalled(root), "Installation path was misread as an escaped URL.");
                File.Delete(dll);
                Check(!Registration.IsInstalled(root), "Missing handler DLL was reported as installed.");
            }
            Pass("registration handles spaces, percent signs, hash signs, and Unicode paths");
        }
        finally
        {
            Registry.CurrentUser.DeleteSubKeyTree(path, false);
            File.Delete(dll);
            Directory.Delete(folder, false);
        }
    }

    static void OverlayPlanning()
    {
        var planned = Registration.PlanOverlays(new[] {
            new KeyValuePair<string, string>("Grasshopper.GH", "blue"),
            new KeyValuePair<string, string>("grasshopper.ghx", "purple"),
            new KeyValuePair<string, string>("Shared.Grasshopper", "blue"),
            new KeyValuePair<string, string>("shared.grasshopper", "purple"),
            new KeyValuePair<string, string>("GenericEditor", "purple") });
        Check(planned.Count == 2 && planned["Grasshopper.GH"] == "blue" && planned["grasshopper.ghx"] == "purple", "Wrong extension badge plan.");
        Pass("distinct GH/GHX icons; shared and general associations omitted");
    }
}
