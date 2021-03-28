using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace SMWPatcher
{
    [Serializable]
    public class Config
    {
        public string InputPath;
        public string OutputPath;
        public string TempPath;
        public string WorkingDirectory;
               
        public string GPSPath;
        public string AddMusicKPath;
        public string PixiPath;
        public string LunarMagicPath;
        public string LevelsPath;
        public string Map16Path;
        public string OverworldPath;

        public List<string> Patches = new List<string>();

        public bool TestEnabled;
        public string TestLevel;
        public string TestLevelDest;
        public string RetroArchPath;
        public string RetroArchCore;

        #region load

        static private readonly String FilePath = "config.txt";
        static public bool Exists => File.Exists(FilePath);

        static public Config Load()
        {
            try
            {
                var str = File.ReadAllText(FilePath);
                return Load(str);
            }
            catch
            {
                return null;
            }
        }

        static private Config Load(string data)
        {
            Config config = new Config();

            HashSet<string> flags = new HashSet<string>();
            Dictionary<string, string> vars = new Dictionary<string, string>();
            Dictionary<string, List<string>> lists = new Dictionary<string, List<string>>();

            #region parse

            var lines = data.Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                string str = lines[i];
                string peek = null;
                if (i < lines.Length - 1)
                    peek = lines[i + 1];

                if (str.StartsWith("--"))
                {
                    // comment
                }
                else if (str.Contains('='))
                {
                    // var
                    var sp = str.Split('=');
                    if (sp.Length != 2)
                        throw new Exception("Malformed assignment");
                    vars.Add(sp[0].Trim(), sp[1].Trim());
                }
                else if (peek != null && peek.Trim() == "[")
                {
                    // list
                    var list = new List<string>();
                    lists.Add(str.Trim(), list);
                    i += 2;

                    while (true)
                    {
                        if (i >= lines.Length)
                            throw new Exception("Malformed list");

                        str = lines[i];
                        if (str.Trim() == "]")
                            break;
                        else
                            list.Add(str.Trim());

                        i++;
                    }
                }
                else if (!string.IsNullOrWhiteSpace(str))
                {
                    // flag
                    flags.Add(str.Trim());
                }
            }

            #endregion

            vars.TryGetValue("dir", out config.WorkingDirectory);
            vars.TryGetValue("input", out config.InputPath);
            vars.TryGetValue("output", out config.OutputPath);
            vars.TryGetValue("temp", out config.TempPath);
            vars.TryGetValue("gps_path", out config.GPSPath);
            vars.TryGetValue("addmusick_path", out config.AddMusicKPath);
            vars.TryGetValue("pixi_path", out config.PixiPath);
            vars.TryGetValue("lm_path", out config.LunarMagicPath);
            vars.TryGetValue("levels", out config.LevelsPath);
            vars.TryGetValue("map16", out config.Map16Path);
            vars.TryGetValue("overworld", out config.OverworldPath);
            lists.TryGetValue("patches", out config.Patches);

            config.TestEnabled = flags.Contains("test_enabled");
            vars.TryGetValue("test_level", out config.TestLevel);
            vars.TryGetValue("test_level_dest", out config.TestLevelDest);
            vars.TryGetValue("retroarch_path", out config.RetroArchPath);
            vars.TryGetValue("retroarch_core", out config.RetroArchCore);

            return config;
        }

        #endregion
    }
}
