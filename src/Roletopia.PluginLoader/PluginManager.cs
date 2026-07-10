using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

namespace Roletopia.PluginLoader
{
    [DataContract]
    public sealed class PluginManifest
    {
        [DataMember(Name = "pluginId")]
        public string PluginId { get; set; }

        [DataMember(Name = "name")]
        public string Name { get; set; }

        [DataMember(Name = "version")]
        public string Version { get; set; }

        [DataMember(Name = "roles")]
        public List<string> Roles { get; set; }
    }

    public interface IPluginActivator
    {
        void Activate(PluginManifest manifest);
    }

    public sealed class PluginManager
    {
        public IEnumerable<PluginManifest> DiscoverPluginManifests(string pluginsDirectory)
        {
            if (string.IsNullOrWhiteSpace(pluginsDirectory) || !Directory.Exists(pluginsDirectory))
            {
                return Array.Empty<PluginManifest>();
            }

            var manifests = new List<PluginManifest>();
            var files = Directory.GetFiles(pluginsDirectory, "*.plugin.json", SearchOption.TopDirectoryOnly);
            foreach (var file in files)
            {
                try
                {
                    using (var stream = File.OpenRead(file))
                    {
                        var serializer = new DataContractJsonSerializer(typeof(PluginManifest));
                        var manifest = serializer.ReadObject(stream) as PluginManifest;
                        if (manifest != null)
                        {
                            manifests.Add(manifest);
                        }
                    }
                }
                catch (IOException)
                {
                    // Skip unreadable plugin files so one bad file does not block all plugins.
                }
                catch (SerializationException)
                {
                    // Skip malformed plugin manifests during discovery.
                }
            }

            return manifests;
        }

        public void ActivateAll(IEnumerable<PluginManifest> manifests, IPluginActivator activator)
        {
            if (manifests == null || activator == null)
            {
                return;
            }

            foreach (var manifest in manifests)
            {
                activator.Activate(manifest);
            }
        }
    }
}
