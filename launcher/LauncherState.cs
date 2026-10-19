using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

namespace Straft4Dead.Launcher
{
    /// <summary>A file the launcher put in the STRAFTAT folder, so uninstall can take it out again.</summary>
    public sealed class GameFile
    {
        public string Name { get; set; }
        /// <summary>SHA-256 of what the launcher wrote; the file is only removed if it still matches.</summary>
        public string Sha256 { get; set; }
        /// <summary>True if the launcher's copy replaced an existing file, which was saved to the backup folder.</summary>
        public bool BackedUp { get; set; }
        /// <summary>True if an identical file was already there, so uninstall leaves it.</summary>
        public bool WasThere { get; set; }
    }

    /// <summary>state.json: what's installed and where things are.</summary>
    public sealed class LauncherState
    {
        /// <summary>Game folders picked by hand; empty means find them through Steam.</summary>
        public string StraftatPath { get; set; }
        public string L4D2Path { get; set; }
        /// <summary>Unity.exe picked by hand, if it isn't where Unity Hub puts it.</summary>
        public string UnityPath { get; set; }
        /// <summary>Installed Straft 4 Dead version, or empty.</summary>
        public string ModVersion { get; set; }
        /// <summary>Pinned downloads already installed (file name), so they aren't unpacked again.</summary>
        public List<string> Installed { get; set; } = new List<string>();
        /// <summary>The mod version the L4D2 assets were last built with (rebuilt when the pipeline changes).</summary>
        public string AssetsBuiltWith { get; set; }
        /// <summary>STRAFTAT folder the game files below were put in.</summary>
        public string GameFilesDir { get; set; }
        public List<GameFile> GameFiles { get; set; } = new List<GameFile>();

        [ScriptIgnore]
        public bool IsInstalled => !string.IsNullOrEmpty(ModVersion);

        public static LauncherState Load(string path)
        {
            if (!File.Exists(path)) return new LauncherState();
            try
            {
                var state = Json.Serializer.Deserialize<LauncherState>(File.ReadAllText(path));
                state.Installed ??= new List<string>();
                state.GameFiles ??= new List<GameFile>();
                return state;
            }
            catch
            {
                return new LauncherState();
            }
        }

        public void Save(string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, Json.Serializer.Serialize(this), new UTF8Encoding(false));
        }
    }

    internal static class Json
    {
        public static readonly JavaScriptSerializer Serializer = new JavaScriptSerializer { MaxJsonLength = 64 * 1024 * 1024 };

        public static Dictionary<string, object> Parse(string text)
        {
            return Serializer.Deserialize<Dictionary<string, object>>(text.TrimStart('﻿'));
        }

        public static string Str(this Dictionary<string, object> obj, string key)
        {
            return obj != null && obj.TryGetValue(key, out var value) ? value as string : null;
        }
    }
}
