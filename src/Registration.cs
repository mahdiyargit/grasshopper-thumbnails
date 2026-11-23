using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using Microsoft.Win32;

namespace GrasshopperFilePreview
{
    public static class Registration
    {
        // Windows caches preview capabilities by CLSID. Keep these identities stable.
        public const string Id = "{9A67F1FF-DC27-4647-8BBD-81789C9BB7FC}";
        private const string LegacyId = "{F7AC7892-28CB-4B49-97D3-E439C0417498}";
        public const string AppId = "{2960CA73-5264-4BD0-953C-A84C57147A9A}";
        public const string ThumbnailId = "{40E6D89F-3412-4FB6-9D32-1A56D4C4869B}";
        public const string Title = "Grasshopper Thumbnails & Preview";
        private const string Preview = "{8895B1C6-B41F-4C1C-A562-0D564250836F}";
        private const string Thumbnail = "{E357FCCD-A995-4576-B01F-234630154E96}";
        private const string Classes = @"Software\Classes\";
        private const string HandlerList = @"Software\Microsoft\Windows\CurrentVersion\PreviewHandlers";
        // Stable storage identifiers preserve badge backups, one Installed apps
        // entry, and installation locks across updates.
        private const string StorageId = "GrasshopperBitmapPreview";
        private const string Settings = @"Software\" + StorageId;
        private const string OverlayBackups = Settings + @"\OverlayBackups";
        private const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\" + StorageId;
        private static readonly string[] Extensions = { ".gh", ".ghx" };
        private static readonly string[] Payload = {
            "GrasshopperFilePreview.dll", "GrasshopperFilePreview.Setup.exe", "GrasshopperFilePreview.Diagnostics.exe",
            "Grasshopper.ico", "GrasshopperXml.ico", "LICENSE", "THIRD_PARTY_NOTICES.md",
            "GrasshopperFilePreview.Setup.exe.config", "GrasshopperFilePreview.Diagnostics.exe.config"
        };
        internal static string GetInstallFolder(bool allUsers)
        {
            var location = allUsers ? Environment.SpecialFolder.ProgramFiles : Environment.SpecialFolder.LocalApplicationData;
            return Path.Combine(Environment.GetFolderPath(location), "GrasshopperFilePreview");
        }

        static RegistryKey Root(bool allUsers)
        {
            return allUsers ? Registry.LocalMachine : Registry.CurrentUser;
        }

        static void RequireAdministrator(bool allUsers)
        {
            if (!allUsers)
                return;
            using (var identity = WindowsIdentity.GetCurrent())
                if (!new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator))
                    throw new InvalidOperationException("All-users installation requires administrator rights. Run the Windows installer and choose All users.");
        }

        [DllImport("shell32.dll")]
        private static extern void SHChangeNotify(uint id, uint flags, IntPtr first, IntPtr second);
        [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)]
        private static extern int AssocQueryString(uint flags, uint str, string assoc, string extra, StringBuilder output, ref uint length);

        private static string Mapping(string extension, string category)
        {
            return Classes + extension + @"\shellex\" + category;
        }
        private static bool Same(object a, object b)
        {
            return a is string && b is string && String.Equals((string)a, (string)b, StringComparison.OrdinalIgnoreCase);
        }
        private static object Read(RegistryKey root, string path, string name)
        {
            using (var key = root.OpenSubKey(path))
                return key == null ? null : key.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
        }
        private static void Refresh()
        {
            SHChangeNotify(0x08000000, 0x1000, IntPtr.Zero, IntPtr.Zero); // ASSOCCHANGED | FLUSH
        }

        public static bool IsInstalled(bool allUsers = false)
        {
            return IsInstalled(Root(allUsers));
        }

        internal static bool IsInstalled(RegistryKey root)
        {
            foreach (string extension in Extensions)
                if (!Same(Read(root, Mapping(extension, Preview), ""), Id) || !Same(Read(root, Mapping(extension, Thumbnail), ""), ThumbnailId))
                    return false;
            string folder = Read(root, Settings, "InstallDirectory") as string;
            if (String.IsNullOrEmpty(folder))
                return false;
            AssemblyName assembly;
            try { assembly = AssemblyName.GetAssemblyName(Path.Combine(folder, Payload[0])); }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
            catch (BadImageFormatException) { return false; }
            foreach (string id in new[] { Id, ThumbnailId })
            {
                string codeBase = Read(root, Classes + @"CLSID\" + id + @"\InprocServer32", "CodeBase") as string;
                if (!Same(codeBase, assembly.CodeBase))
                    return false;
            }
            return true;
        }

        private static void Exclusive(Action action)
        {
            using (var mutex = new System.Threading.Mutex(false, @"Local\" + StorageId + ".Setup"))
            {
                bool locked;
                try
                {
                    locked = mutex.WaitOne(0);
                }
                catch (System.Threading.AbandonedMutexException) { locked = true; }
                if (!locked)
                    throw new InvalidOperationException("Another setup operation is already running.");
                try
                {
                    action();
                }
                finally { mutex.ReleaseMutex(); }
            }
        }

        public static void Install()
        {
            if (Read(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Uninstall\GrasshopperFilePreview.Bundle_is1", "DisplayName") != null)
                throw new InvalidOperationException("Run the Windows installer to update this installation.");
            Install(false);
        }

        internal static void Install(bool managedByInstaller, string rollbackFile = null, bool allUsers = false)
        {
            if (!Environment.Is64BitProcess || !Environment.Is64BitOperatingSystem)
                throw new PlatformNotSupportedException("This release requires Windows x64.");
            RequireAdministrator(allUsers);
            var root = Root(allUsers);
            Exclusive(delegate
            {
                if (Read(Root(!allUsers), Settings, "InstallDirectory") != null)
                    throw new InvalidOperationException(allUsers
                        ? "A current-account installation already exists. Uninstall it before switching to All users."
                        : "An all-users installation already exists. Choose All users to update it, or uninstall it before switching to Only for me.");
                CheckConflicts(allUsers);
                // Older per-user uninstallers retained empty COM keys. They can hide
                // the machine registration in Windows' merged Classes view.
                if (allUsers)
                    PruneEmptyRegistration(Registry.CurrentUser);
                // Stage before registration. The Windows installer records these same files for removal.
                string folder = StagePayload(AppDomain.CurrentDomain.BaseDirectory, allUsers);
                string dll = Path.Combine(folder, Payload[0]);
                var assembly = AssemblyName.GetAssemblyName(dll);
                try
                {
                    RegistryJournal.Execute(root, journal =>
                    {
                        journal.Set(Classes + @"AppID\" + AppId, "DllSurrogate", Path.Combine(Environment.SystemDirectory, "prevhost.exe"));
                        journal.Set(Classes + @"CLSID\" + Id, "", Title);
                        journal.Set(Classes + @"CLSID\" + Id, "DisplayName", Title);
                        journal.Set(Classes + @"CLSID\" + Id, "AppID", AppId);
                        // Used by Windows' own TXT/media previewers on current Windows 11.
                        // This is a per-handler Shell capability, not an Internet-zone policy.
                        // It preserves Zone.Identifier and does not relax handler isolation.
                        journal.Set(Classes + @"CLSID\" + Id, "AutomaticallyPreviewUntrustedFiles", 1, RegistryValueKind.DWord);
                        RegisterServer(journal, Id, assembly, "GrasshopperFilePreview.Handler");
                        journal.Set(Classes + @"CLSID\" + ThumbnailId, "", Title + " Thumbnail Provider");
                        RegisterServer(journal, ThumbnailId, assembly, "GrasshopperFilePreview.ThumbnailHandler");
                        journal.Set(HandlerList, Id, Title);
                        journal.RemoveValue(HandlerList, LegacyId);
                        journal.ClearOwnedTree(Classes + @"CLSID\" + LegacyId);
                        foreach (string extension in Extensions)
                        {
                            journal.Set(Mapping(extension, Preview), "", Id);
                            journal.Set(Mapping(extension, Thumbnail), "", ThumbnailId);
                        }
                        InstallOverlays(root, journal, folder, allUsers);
                        journal.Set(Settings, "InstallDirectory", folder);
                        journal.Set(Settings, "Version", ProductInfo.Version);
                        if (managedByInstaller)
                            journal.ClearOwnedTree(UninstallKey); // Inno owns the new uninstaller.
                        else
                        {
                            journal.Set(UninstallKey, "DisplayName", Title);
                            journal.Set(UninstallKey, "DisplayVersion", ProductInfo.Version);
                            journal.Set(UninstallKey, "InstallLocation", folder);
                            journal.Set(UninstallKey, "UninstallString", "\"" + Path.Combine(folder, Payload[1]) + "\" --uninstall");
                            journal.Set(UninstallKey, "NoModify", 1, RegistryValueKind.DWord);
                            journal.Set(UninstallKey, "NoRepair", 1, RegistryValueKind.DWord);
                        }
                        // Roll back registration too if cache refresh fails during installation.
                        Refresh();
                        if (!allUsers)
                            ThumbnailCache.Reset();
                    }, rollbackFile);
                }
                finally { Refresh(); }
            });
        }

        internal static void RollbackInstall(string rollbackFile, bool allUsers = false)
        {
            RequireAdministrator(allUsers);
            try { Exclusive(() => RegistryJournal.Restore(Root(allUsers), rollbackFile)); }
            finally { Refresh(); }
        }

        internal static void RefreshThumbnails()
        {
            ThumbnailCache.Reset();
            Refresh();
        }

        internal static string StagePayload(string source, bool allUsers = false)
        {
            foreach (string filename in Payload)
                if (!File.Exists(Path.Combine(source, filename)))
                    throw new FileNotFoundException("The installation files are incomplete. Run the Windows installer again.", filename);
            var fileHashes = Payload.ToDictionary(name => name, name => FileHash(Path.Combine(source, name)));
            string signature;
            using (var hash = SHA256.Create())
            {
                string hashes = String.Join("\n", Payload.Select(name => fileHashes[name]));
                signature = BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(hashes))).Replace("-", "").Substring(0, 16);
            }
            string target = Path.Combine(GetInstallFolder(allUsers), "versions", ProductInfo.Version + "-" + signature);
            Directory.CreateDirectory(target);
            foreach (string filename in Payload)
                CopyVerifiedFile(Path.Combine(source, filename), Path.Combine(target, filename), fileHashes[filename]);
            return target;
        }

        internal static void CopyVerifiedFile(string source, string destination, string expectedHash)
        {
            if (File.Exists(destination))
            {
                if (FileHash(destination) != expectedHash)
                    throw new IOException("A staged installation file was modified: " + destination);
                return;
            }
            // Publish only complete, verified files. A failed copy must not leave a partial
            // destination that makes all later attempts to install this build fail.
            string temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.Copy(source, temporary, false);
                if (FileHash(temporary) != expectedHash)
                    throw new IOException("An installation source file changed during copying: " + source);
                File.Move(temporary, destination);
            }
            finally
            {
                // A locked orphan temp file is harmless. Do not mask the original failure.
                try
                {
                    File.Delete(temporary);
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }

        private static string FileHash(string file)
        {
            using (var hash = SHA256.Create())
            using (var stream = File.OpenRead(file))
                return Convert.ToBase64String(hash.ComputeHash(stream));
        }
        internal static void RegisterServer(RegistryJournal journal, string id, AssemblyName assembly, string type)
        {
            string server = Classes + @"CLSID\" + id + @"\InprocServer32";
            journal.Set(server, "", "mscoree.dll");
            journal.Set(server, "ThreadingModel", "Apartment");
            journal.Set(server, "Class", type);
            journal.Set(server, "Assembly", assembly.FullName);
            journal.Set(server, "RuntimeVersion", "v4.0.30319");
            // Use the same literal file URL as RegAsm. Uri conversion can interpret
            // percent sequences in a Windows folder name as escape characters.
            journal.Set(server, "CodeBase", assembly.CodeBase);
        }

        private static IEnumerable<string> ProgIds(string extension, bool allUsers)
        {
            var values = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using (var key = allUsers ? Registry.LocalMachine.OpenSubKey(Classes + extension) : Registry.ClassesRoot.OpenSubKey(extension))
            {
                string value = key == null ? null : key.GetValue("") as string;
                if (!String.IsNullOrEmpty(value))
                    values.Add(value);
            }
            var output = new StringBuilder(4096);
            uint length = (uint)output.Capacity;
            // A machine installation must not apply one administrator's personal associations to everyone.
            if (!allUsers && AssocQueryString(0, 20, extension, null, output, ref length) == 0 && output.Length != 0)
                values.Add(output.ToString());
            return values;
        }
        private static void CheckConflicts(bool allUsers)
        {
            foreach (string extension in Extensions)
                foreach (string type in new[] { extension, @"SystemFileAssociations\" + extension }.Concat(ProgIds(extension, allUsers)))
                    foreach (var handler in new[] { new[] { Preview, Id }, new[] { Thumbnail, ThumbnailId } })
                        using (var key = allUsers
                            ? Registry.LocalMachine.OpenSubKey(Classes + type + @"\shellex\" + handler[0])
                            : Registry.ClassesRoot.OpenSubKey(type + @"\shellex\" + handler[0]))
                        {
                            string previous = key == null ? null : key.GetValue("") as string;
                            bool legacyPreview = handler[0] == Preview && Same(previous, LegacyId);
                            if (!String.IsNullOrEmpty(previous) && !Same(previous, handler[1]) && !legacyPreview)
                                throw new InvalidOperationException("Another handler is registered for " + extension + " at " + type + ". Remove it before installing this application.");
                        }
        }

        internal static Dictionary<string, string> PlanOverlays(IEnumerable<KeyValuePair<string, string>> candidates)
        {
            var planned = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var ambiguous = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var candidate in candidates)
            {
                // A general editor's ProgID may also serve unrelated extensions. Do not brand it.
                if (candidate.Key.IndexOf("grasshopper", StringComparison.OrdinalIgnoreCase) < 0)
                    continue;
                string existing;
                if (planned.TryGetValue(candidate.Key, out existing) && existing != candidate.Value)
                    ambiguous.Add(candidate.Key);
                else
                    planned[candidate.Key] = candidate.Value;
            }
            // One ProgID cannot represent two different corner icons.
            foreach (string key in ambiguous)
                planned.Remove(key);
            return planned;
        }
        private static void InstallOverlays(RegistryKey root, RegistryJournal journal, string folder, bool allUsers)
        {
            var candidates = new List<KeyValuePair<string, string>>();
            foreach (string extension in Extensions)
                foreach (string type in ProgIds(extension, allUsers))
                    candidates.Add(new KeyValuePair<string, string>(type, Path.Combine(folder, extension == ".ghx" ? "GrasshopperXml.ico" : "Grasshopper.ico") + ",0"));
            var planned = PlanOverlays(candidates);
            RestoreOverlays(root, journal);
            foreach (var target in planned)
            {
                string backup = OverlayBackups + "\\" + target.Key;
                using (var original = root.OpenSubKey(Classes + target.Key))
                {
                    object old = original == null ? null : original.GetValue("TypeOverlay", null, RegistryValueOptions.DoNotExpandEnvironmentNames);
                    journal.Set(backup, "HadValue", old == null ? 0 : 1, RegistryValueKind.DWord);
                    if (old != null)
                        journal.Set(backup, "Original", old, original.GetValueKind("TypeOverlay"));
                }
                journal.Set(backup, "Applied", target.Value);
                journal.Set(Classes + target.Key, "TypeOverlay", target.Value);
            }
        }
        private static void RestoreOverlays(RegistryKey root, RegistryJournal journal)
        {
            using (var backups = root.OpenSubKey(OverlayBackups))
            {
                if (backups == null)
                    return;
                foreach (string type in backups.GetSubKeyNames())
                    using (var backup = backups.OpenSubKey(type))
                    {
                        if (Same(Read(root, Classes + type, "TypeOverlay"), backup.GetValue("Applied")))
                        {
                            if ((int)backup.GetValue("HadValue", 0) == 1)
                                journal.Set(Classes + type, "TypeOverlay", backup.GetValue("Original", null, RegistryValueOptions.DoNotExpandEnvironmentNames), backup.GetValueKind("Original"));
                            else
                                journal.RemoveValue(Classes + type, "TypeOverlay");
                        }
                        journal.ClearOwnedTree(OverlayBackups + "\\" + type);
                    }
            }
        }

        public static void Uninstall(bool allUsers = false)
        {
            RequireAdministrator(allUsers);
            var root = Root(allUsers);
            Exclusive(delegate
            {
                RegistryJournal.Execute(root, journal =>
                {
                    RestoreOverlays(root, journal);
                    foreach (string extension in Extensions)
                    {
                        if (Same(Read(root, Mapping(extension, Preview), ""), Id) || Same(Read(root, Mapping(extension, Preview), ""), LegacyId))
                            journal.RemoveValue(Mapping(extension, Preview), "");
                        if (Same(Read(root, Mapping(extension, Thumbnail), ""), ThumbnailId))
                            journal.RemoveValue(Mapping(extension, Thumbnail), "");
                    }
                    journal.RemoveValue(HandlerList, Id);
                    journal.RemoveValue(HandlerList, LegacyId);
                    journal.ClearOwnedTree(Classes + @"CLSID\" + Id);
                    journal.ClearOwnedTree(Classes + @"CLSID\" + LegacyId);
                    journal.ClearOwnedTree(Classes + @"CLSID\" + ThumbnailId);
                    journal.ClearOwnedTree(Classes + @"AppID\" + AppId);
                    journal.ClearOwnedTree(UninstallKey);
                    journal.RemoveValue(Settings, "InstallDirectory");
                    journal.RemoveValue(Settings, "Version");
                });
                PruneEmptyRegistration(root);
            });
            // The Windows uninstaller owns file removal after this registration step.
            Refresh();
        }

        internal static void PruneEmptyRegistration(RegistryKey root)
        {
            foreach (string id in new[] { Id, LegacyId, ThumbnailId })
                PruneEmptyTree(root, Classes + @"CLSID\" + id);
            PruneEmptyTree(root, Classes + @"AppID\" + AppId);
            foreach (string extension in Extensions)
            {
                foreach (string category in new[] { Preview, Thumbnail })
                    PruneEmptyKey(root, Mapping(extension, category));
                // Remove parents only if our mappings were their last children.
                // Other applications can use empty keys as association metadata.
                PruneEmptyKey(root, Classes + extension + @"\shellex");
                PruneEmptyKey(root, Classes + extension);
            }
        }

        internal static void PruneEmptyTree(RegistryKey root, string path)
        {
            using (var key = root.OpenSubKey(path))
            {
                if (key == null)
                    return;
                foreach (string child in key.GetSubKeyNames())
                    PruneEmptyTree(root, path + "\\" + child);
            }
            PruneEmptyKey(root, path);
        }

        static void PruneEmptyKey(RegistryKey root, string path)
        {
            using (var key = root.OpenSubKey(path))
                if (key == null || key.ValueCount != 0 || key.SubKeyCount != 0)
                    return;
            root.DeleteSubKey(path, false);
        }
    }
}
