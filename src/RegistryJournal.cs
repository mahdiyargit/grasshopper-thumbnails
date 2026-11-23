using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Xml;
using Microsoft.Win32;

namespace GrasshopperFilePreview
{
    // Roll back ordinary failures. Process/power loss can still require running setup again.
    internal sealed class RegistryJournal
    {
        private readonly RegistryKey root;
        private readonly List<SavedValue> undo = new List<SavedValue>();
        private readonly HashSet<string> captured = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> createdKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        internal RegistryJournal(RegistryKey root)
        {
            this.root = root;
        }

        internal static void Execute(RegistryKey root, Action<RegistryJournal> operation, string rollbackFile = null)
        {
            var journal = new RegistryJournal(root);
            try
            {
                operation(journal);
                if (rollbackFile != null)
                    journal.Save(rollbackFile);
            }
            catch (Exception original)
            {
                var errors = journal.Rollback();
                if (errors.Count != 0)
                {
                    errors.Insert(0, original);
                    throw new AggregateException("Registry rollback was incomplete. Run setup again to repair the installation.", errors);
                }
                throw;
            }
        }

        internal void Set(string path, string name, object value, RegistryValueKind kind = RegistryValueKind.String)
        {
            Capture(path, name);
            string current = "";
            foreach (string part in path.Split('\\'))
            {
                current = current.Length == 0 ? part : current + "\\" + part;
                using (var key = root.OpenSubKey(current))
                    if (key == null)
                        createdKeys.Add(current);
            }
            using (var key = root.CreateSubKey(path))
                key.SetValue(name, value, kind);
        }

        internal void RemoveValue(string path, string name)
        {
            Capture(path, name);
            using (var key = root.OpenSubKey(path, true))
                if (key != null)
                    key.DeleteValue(name, false);
        }

        // Retain keys during the transaction so rollback need not reconstruct their ACLs.
        internal void ClearOwnedTree(string path)
        {
            using (var key = root.OpenSubKey(path))
            {
                if (key == null)
                    return;
                foreach (string child in key.GetSubKeyNames())
                    ClearOwnedTree(path + "\\" + child);
                foreach (string name in key.GetValueNames())
                    RemoveValue(path, name);
            }
        }

        private void Capture(string path, string name)
        {
            if (!captured.Add(path + "\0" + name))
                return;
            using (var key = root.OpenSubKey(path))
            {
                object value = key == null ? null : key.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
                RegistryValueKind kind = value == null ? RegistryValueKind.String : key.GetValueKind(name);
                undo.Add(new SavedValue { Path = path, Name = name, Value = value, Kind = kind });
            }
        }

        [DataContract]
        sealed class SavedValue
        {
            [DataMember] public string Path;
            [DataMember] public string Name;
            [DataMember] public object Value;
            [DataMember] public RegistryValueKind Kind;

            internal void Restore(RegistryKey root)
            {
                if (Value != null)
                {
                    using (var target = root.CreateSubKey(Path))
                        target.SetValue(Name, Value, Kind);
                }
                else
                    using (var target = root.OpenSubKey(Path, true))
                        if (target != null)
                            target.DeleteValue(Name, false);
            }
        }

        [DataContract]
        sealed class Backup
        {
            [DataMember] public List<SavedValue> Values;
            [DataMember] public List<string> CreatedKeys;
        }

        static DataContractSerializer Serializer()
        {
            // Only registry data types, never arbitrary CLR types or executable content.
            return new DataContractSerializer(typeof(Backup), new[] { typeof(byte[]), typeof(string[]) });
        }

        void Save(string path)
        {
            using (var file = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None))
                Serializer().WriteObject(file, new Backup { Values = undo, CreatedKeys = new List<string>(createdKeys) });
        }

        internal static void Restore(RegistryKey root, string path)
        {
            var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 16 * 1024 * 1024 };
            Backup backup;
            using (var file = File.OpenRead(path))
            using (var reader = XmlReader.Create(file, settings))
                backup = (Backup)Serializer().ReadObject(reader);
            var journal = new RegistryJournal(root);
            journal.undo.AddRange(backup.Values);
            journal.createdKeys.UnionWith(backup.CreatedKeys);
            var errors = journal.Rollback();
            if (errors.Count != 0)
                throw new AggregateException("Could not restore the previous registration. Run setup again to repair it.", errors);
        }

        internal List<Exception> Rollback()
        {
            var errors = new List<Exception>();
            for (int i = undo.Count - 1; i >= 0; i--)
                try
                {
                    undo[i].Restore(root);
                }
                catch (Exception error) { errors.Add(error); }
            var paths = new List<string>(createdKeys);
            paths.Sort((a, b) => b.Length.CompareTo(a.Length));
            foreach (string path in paths)
            {
                try
                {
                    bool empty;
                    using (var key = root.OpenSubKey(path))
                        empty = key != null && key.ValueCount == 0 && key.SubKeyCount == 0;
                    if (empty)
                        root.DeleteSubKey(path, false);
                }
                catch (Exception error) { errors.Add(error); }
            }
            return errors;
        }
    }
}
