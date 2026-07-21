using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

namespace Roletopia.PluginLoader
{
    [DataContract]
    public sealed class PluginManifest
    {
        [DataMember(Name = "pluginId")] public string PluginId { get; set; }
        [DataMember(Name = "name")] public string Name { get; set; }
        [DataMember(Name = "version")] public string Version { get; set; }
        [DataMember(Name = "roles")] public List<string> Roles { get; set; }
    }

    public interface IPluginActivator { void Activate(PluginManifest manifest); }

    public sealed class PluginManager
    {
        public IEnumerable<PluginManifest> DiscoverPluginManifests(string pluginsDirectory)
        {
            if (string.IsNullOrWhiteSpace(pluginsDirectory) || !Directory.Exists(pluginsDirectory)) return Array.Empty<PluginManifest>();
            var manifests = new List<PluginManifest>();
            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string[] files;
            try { files = Directory.GetFiles(pluginsDirectory, "*.plugin.json", SearchOption.TopDirectoryOnly); }
            catch (IOException) { return manifests; }
            catch (UnauthorizedAccessException) { return manifests; }

            foreach (var file in files.OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    using (var stream = File.OpenRead(file))
                    {
                        var serializer = new DataContractJsonSerializer(typeof(PluginManifest));
                        var manifest = serializer.ReadObject(stream) as PluginManifest;
                        if (!IsValid(manifest) || !ids.Add(manifest.PluginId)) continue;
                        manifest.Roles = (manifest.Roles ?? new List<string>()).Where(r => !string.IsNullOrWhiteSpace(r)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                        manifests.Add(manifest);
                    }
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
                catch (SerializationException) { }
            }
            return manifests;
        }

        public void ActivateAll(IEnumerable<PluginManifest> manifests, IPluginActivator activator)
        {
            if (manifests == null || activator == null) return;
            foreach (var manifest in manifests.Where(IsValid)) activator.Activate(manifest);
        }

        private static bool IsValid(PluginManifest manifest)
        {
            if (manifest == null || string.IsNullOrWhiteSpace(manifest.PluginId) || string.IsNullOrWhiteSpace(manifest.Name)) return false;
            Version ignored;
            return Version.TryParse(manifest.Version, out ignored);
        }
    }
}
