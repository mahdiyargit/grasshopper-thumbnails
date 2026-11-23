using System;
using System.IO;
using System.Security.Cryptography;
using GrasshopperFilePreview;

static class SetupTests
{
    static string Hash(string path)
    {
        using (var algorithm = SHA256.Create())
        using (var file = File.OpenRead(path))
            return Convert.ToBase64String(algorithm.ComputeHash(file));
    }

    public static int Run()
    {
        string directory = Path.Combine(Path.GetTempPath(), "GrasshopperFilePreview-stage-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string source = Path.Combine(directory, "source"), destination = Path.Combine(directory, "destination");
        try
        {
            File.WriteAllText(source, "expected payload");
            string expected = Hash(source);
            File.WriteAllText(source, "source changed after hashing");
            bool rejected = false;
            try
            {
                Registration.CopyVerifiedFile(source, destination, expected);
            }
            catch (IOException) { rejected = true; }
            if (!rejected || File.Exists(destination) || Directory.GetFiles(directory, "*.tmp").Length != 0)
                throw new Exception("Failed staging published a partial/unverified file.");
            File.WriteAllText(source, "expected payload");
            Registration.CopyVerifiedFile(source, destination, expected);
            if (Hash(destination) != expected)
                throw new Exception("Staging retry failed.");
            Console.WriteLine("PASS changed source rejected, partial file absent, retry succeeds");

            using (var locked = File.Open(destination, FileMode.Open, FileAccess.Read, FileShare.Read))
                Registration.CopyVerifiedFile(source, destination, expected);
            Console.WriteLine("PASS matching staged file is reused while read-locked");

            File.WriteAllText(destination, "modified installation");
            rejected = false;
            try
            {
                Registration.CopyVerifiedFile(source, destination, expected);
            }
            catch (IOException) { rejected = true; }
            if (!rejected || File.ReadAllText(destination) != "modified installation")
                throw new Exception("Modified installation was silently overwritten.");
            Console.WriteLine("PASS modified staged file is preserved and reported");
            return 3;
        }
        finally
        {
            // Only this invocation's two named scratch files can be removed here.
            File.Delete(source);
            File.Delete(destination);
            Directory.Delete(directory, false);
        }
    }
}
