using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace OurTaiko.Online
{
    // One [[network.servers]] entry of OurTaikoPlayer's config: the API root, the account and an
    // optional HTTP proxy. An empty proxy disables proxies, including the environment's.
    [Serializable]
    public sealed class ServerConfig
    {
        public bool enabled = true;
        public string name = "", baseUrl = "", username = "", password = "", httpProxy = "";
        // ServerLogin logs in on its own while the remembered account last succeeded.
        public bool autoLogin;

        public ServerConfig Clone() => (ServerConfig)MemberwiseClone();
        public bool HasCredentials => !string.IsNullOrEmpty(username) && !string.IsNullOrEmpty(password);
        public string DisplayName => string.IsNullOrEmpty(name) ? baseUrl : name;
    }

    // servers.json in the persistent data folder. The built-in servers (OurTaiko Fanmade and the ESE
    // backend) are always listed: a missing file is written with them, and an existing file gets
    // any built-in address it lacks; turn one off with "enabled": false. Accounts are filled in by
    // ServerLogin.
    [Serializable]
    public sealed class ServerList
    {
        public const string FileName = "servers.json";
        public static readonly (string Name, string Url)[] BuiltIn =
        {
            ("OurTaiko Fanmade", "https://fanmade.ourtaiko.org"),
            ("ESE", "https://ese-backend.llx.life"),
        };

        public List<ServerConfig> servers = new List<ServerConfig>();

        public static ServerList Default()
        {
            var list = new ServerList();
            list.AddBuiltIn();
            return list;
        }

        // True when a built-in server was added.
        public bool AddBuiltIn()
        {
            bool added = false;
            foreach (var (name, url) in BuiltIn)
            {
                if (servers.Exists(s => (s.baseUrl ?? "").TrimEnd('/') == url)) continue;
                servers.Add(new ServerConfig { name = name, baseUrl = url });
                added = true;
            }
            return added;
        }

        public static ServerList Read(string path)
        {
            if (!File.Exists(path)) return null;
            var list = JsonUtility.FromJson<ServerList>(File.ReadAllText(path)) ?? new ServerList();
            list.servers ??= new List<ServerConfig>();
            list.servers.RemoveAll(server => server == null);
            return list;
        }

        public void Write(string path)
        {
            string directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            string temporary = path + ".tmp";
            File.WriteAllText(temporary, JsonUtility.ToJson(this, true));
            if (File.Exists(path)) File.Delete(path);
            File.Move(temporary, path);
        }
    }
}
