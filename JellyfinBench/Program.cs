using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Text.RegularExpressions;
using System.Web;
using System.Xml;

namespace JellyfinBench
{
#pragma warning disable CA1305
#pragma warning disable CA1307
#pragma warning disable CA1310

    /* THIS THING HAS A BUG: DO NOT PASS A VALUE-LESS FLAG AT THE END.
     * OTHERWISE, IT WILL BE IGNORED. */
    class SimpleCommandLineParser
    {
        public SimpleCommandLineParser() {}

        public List<KeyValuePair<string, string>> ParseOptions(string[] args)
        {
            var parsedFlags = new List<KeyValuePair<string, string>>();

            foreach (string arg in args)
            {
                string option;
                string value;
                if (arg[0] == '-')
                {
                    string flag = arg.TrimStart('-');
                    int valueIndex = flag.IndexOf(':');
                    if (valueIndex >= 0)
                    {
                        option = flag.Substring(0, valueIndex);
                        value = flag.Substring(valueIndex + 1);
                    }
                    else
                    {
                        option = flag;
                        value = "";
                    }
                }
                else
                {
                    option = "";
                    value = arg;
                }
                parsedFlags.Add(new KeyValuePair<string, string>(option, value));
            }
            return parsedFlags;
        }
    }

    class Program
    {
        const string WindowsWritingImageString = "writing image sha256:";
        static int WarmupIterations = 2;
        static int Iterations = 10;
        static int Configs = 0;
        static bool ResolutionFailsInstead = false;

        struct BuildMode
        {
            public string Name;
            public bool NetCoreComposite;
            public bool NetCoreIncludeAspNet;
            public bool AspNetComposite;
            public bool AppR2R;
            public bool AppComposite;
            public bool OneBigComposite;
            public bool AppAVX2;
            public bool UseReadyToRun;
            public bool UseTieredCompilation;

            public void PrintProperties()
            {
                Console.WriteLine("Name: {0}", Name);
                Console.WriteLine("NetCoreComposite: {0}", NetCoreComposite);
                Console.WriteLine("NetCoreIncludeAspNet: {0}", NetCoreIncludeAspNet);
                Console.WriteLine("AspNetComposite: {0}", AspNetComposite);
                Console.WriteLine("AppR2R: {0}", AppR2R);
                Console.WriteLine("AppComposite: {0}", AppComposite);
                Console.WriteLine("OneBigComposite: {0}", OneBigComposite);
                Console.WriteLine("AppAVX2: {0}", AppAVX2);
                Console.WriteLine("UseReadyToRun: {0}", UseReadyToRun);
                Console.WriteLine("UseTieredCompilation: {0}", UseTieredCompilation);
            }

            public static BuildMode ParseXml(XmlNode xml)
            {
                BuildMode buildMode = new BuildMode();

                buildMode.Name = xml.Attributes!["Name"]!.InnerText!;
                buildMode.NetCoreComposite = bool.Parse(xml["NetCoreComposite"]!.InnerText!);
                buildMode.NetCoreIncludeAspNet = bool.Parse(xml["NetCoreIncludeAspNet"]!.InnerText!);
                buildMode.AspNetComposite = bool.Parse(xml["AspNetComposite"]!.InnerText!);
                buildMode.AppR2R = bool.Parse(xml["AppR2R"]!.InnerText!);
                buildMode.AppComposite = bool.Parse(xml["AppComposite"]!.InnerText!);
                buildMode.OneBigComposite = bool.Parse(xml["OneBigComposite"]!.InnerText!);
                buildMode.AppAVX2 = bool.Parse(xml["AppAVX2"]!.InnerText!);
                buildMode.UseTieredCompilation = bool.Parse(xml["UseTieredCompilation"]!.InnerText!);
                buildMode.UseReadyToRun = bool.Parse(xml["UseReadyToRun"]!.InnerText!);

                return buildMode;
            }

            public override string ToString()
            {
                StringBuilder buildModeName = new StringBuilder();
                buildModeName.Append(Name);
                buildModeName.Append(": ");

                if (OneBigComposite)
                {
                    buildModeName.Append("one big composite");
                }
                else
                {
                    buildModeName.AppendFormat(".NET Core{0}={1}",
                        NetCoreIncludeAspNet ? "+ASP.NET" : "",
                        NetCoreComposite ? "composite" : "default");
                    if (!NetCoreIncludeAspNet)
                    {
                        buildModeName.AppendFormat(" / ASP.NET={0}",
                                                      AspNetComposite ?
                                                      "composite" :
                                                      "default");
                    }
                    buildModeName.AppendFormat(" / APP={0}",
                                                  !AppR2R ?
                                                  "JIT" :
                                                  !AppComposite ?
                                                      "R2R" :
                                                      "composite");
                }

                if (AppAVX2)
                {
                    buildModeName.Append(" / AVX2");
                }

                buildModeName.AppendFormat(" / TC {0}", UseTieredCompilation ?
                                                        "ON" :
                                                        "OFF");
                buildModeName.AppendFormat(" / RTR {0}", UseReadyToRun ?
                                                         "ON" :
                                                         "OFF");

                return buildModeName.ToString();
            }
        }

        class BuildModeList
        {
            public const int NumBaseTemplates = 6;
            private List<BuildMode> Values = new List<BuildMode>();

            public BuildModeList()
            {
                Initialize();
            }

            public int Count()
            {
                return Values.Count;
            }

            public void EachWithIndex(Action<BuildMode, int> action)
            {
                int i = 0;
                foreach (BuildMode mode in Values) action(mode, i++);
            }

            public void EachWithIndexUntil(int limit, Action<BuildMode, int> action)
            {
                int i = 0;
                foreach (BuildMode mode in Values.Take(limit)) action(mode, i++);
            }

            public void SelectMatchingConfigs(string expressions)
            {
                string expr = expressions.Replace(',', '|');
                Regex regex = new Regex(@"(" + expr + @")");
                List<BuildMode> selected = Values.Where(cfg => regex.IsMatch(cfg.Name))
                                          .ToList();
                Values = selected;
            }

            public BuildMode GetSpecificConfig(string cfgNameExpr)
            {
                Regex regex = new Regex(@"" + cfgNameExpr);
                return Values.First(cfg => regex.IsMatch(cfg.Name));
            }

            private void AddSeveral(params BuildMode[] additions)
            {
                foreach (BuildMode addition in additions) Values.Add(addition);
            }

            private void Initialize()
            {
                InitializeBaseModes();
                // InitializeUseR2RAndTieredCompilationModes();
            }

            private void InitializeBaseModes()
            {
                AddSeveral(
                    new BuildMode()
                    {
                        Name = "baseline",
                        NetCoreComposite = false,
                        NetCoreIncludeAspNet = false,
                        AspNetComposite = false,
                        AppR2R = false,
                        AppComposite = false,
                        OneBigComposite = false,
                        UseReadyToRun = true,
                        UseTieredCompilation = false,
                    },
                    new BuildMode()
                    {
                        Name = "r2r",
                        NetCoreComposite = false,
                        NetCoreIncludeAspNet = false,
                        AspNetComposite = false,
                        AppR2R = true,
                        AppComposite = false,
                        OneBigComposite = false,
                        UseReadyToRun = true,
                        UseTieredCompilation = false,
                    },
                    new BuildMode()
                    {
                        Name = "app-composite-avx2",
                        NetCoreComposite = false,
                        NetCoreIncludeAspNet = false,
                        AspNetComposite = false,
                        AppR2R = true,
                        AppComposite = true,
                        OneBigComposite = false,
                        AppAVX2 = true,
                        UseReadyToRun = true,
                        UseTieredCompilation = false,
                    },
                    new BuildMode()
                    {
                        Name = "one-big-composite-avx2",
                        NetCoreComposite = false,
                        NetCoreIncludeAspNet = false,
                        AspNetComposite = false,
                        AppR2R = true,
                        AppComposite = true,
                        OneBigComposite = true,
                        AppAVX2 = true,
                        UseReadyToRun = true,
                        UseTieredCompilation = false,
                    },
                    new BuildMode()
                    {
                        Name = "r2r-platform-composite-avx2",
                        NetCoreComposite = true,
                        NetCoreIncludeAspNet = true,
                        AspNetComposite = true,
                        AppR2R = true,
                        AppComposite = true,
                        OneBigComposite = false,
                        AppAVX2 = true,
                        UseReadyToRun = true,
                        UseTieredCompilation = false,
                    },
                    new BuildMode()
                    {
                        Name = "jit-platform-composite-avx2",
                        NetCoreComposite = true,
                        NetCoreIncludeAspNet = true,
                        AspNetComposite = true,
                        AppR2R = false,
                        AppComposite = false,
                        OneBigComposite = false,
                        AppAVX2 = true,
                        UseReadyToRun = true,
                        UseTieredCompilation = false,
                    });
            }

            private void InitializeUseR2RAndTieredCompilationModes()
            {
                bool readyToRun = true;
                bool tieredCompilation = false;

                for (int i = 0; i < NumBaseTemplates; i++)
                {
                    var mode = Values[i];
                    var r2rModeName = mode.Name + "-usereadytorun";

                    Values.Add(new BuildMode()
                               {
                                   Name = r2rModeName,
                                   NetCoreComposite = mode.NetCoreComposite,
                                   NetCoreIncludeAspNet = mode.NetCoreIncludeAspNet,
                                   AspNetComposite = mode.AspNetComposite,
                                   AppR2R = mode.AppR2R,
                                   AppComposite = mode.AppComposite,
                                   OneBigComposite = mode.OneBigComposite,
                                   AppAVX2 = mode.AppAVX2,
                                   UseReadyToRun = readyToRun,
                                   UseTieredCompilation = tieredCompilation,
                               });
                }

                readyToRun = false;
                tieredCompilation = true;

                for (int i = 0; i < NumBaseTemplates; i++)
                {
                    var mode = Values[i];
                    var r2rModeName = mode.Name + "-usetieredcompilation";

                    Values.Add(new BuildMode()
                               {
                                   Name = r2rModeName,
                                   NetCoreComposite = mode.NetCoreComposite,
                                   NetCoreIncludeAspNet = mode.NetCoreIncludeAspNet,
                                   AspNetComposite = mode.AspNetComposite,
                                   AppR2R = mode.AppR2R,
                                   AppComposite = mode.AppComposite,
                                   OneBigComposite = mode.OneBigComposite,
                                   AppAVX2 = mode.AppAVX2,
                                   UseReadyToRun = readyToRun,
                                   UseTieredCompilation = tieredCompilation,
                               });
                }

                readyToRun = true;
                tieredCompilation = true;

                for (int i = 0; i < NumBaseTemplates; i++)
                {
                    var mode = Values[i];
                    var r2rModeName = mode.Name + "usereadytorun-and-tieredcompilation";

                    Values.Add(new BuildMode()
                               {
                                   Name = r2rModeName,
                                   NetCoreComposite = mode.NetCoreComposite,
                                   NetCoreIncludeAspNet = mode.NetCoreIncludeAspNet,
                                   AspNetComposite = mode.AspNetComposite,
                                   AppR2R = mode.AppR2R,
                                   AppComposite = mode.AppComposite,
                                   OneBigComposite = mode.OneBigComposite,
                                   AppAVX2 = mode.AppAVX2,
                                   UseReadyToRun = readyToRun,
                                   UseTieredCompilation = tieredCompilation,
                               });
                }
            }
        }

        static BuildModeList s_buildModes = new BuildModeList();

        static string s_folderName = "";
        static string s_timestamp = "";

        static TextWriter? s_buildLogFile;
        static TextWriter? s_execLogFile;

        static Tuple<string, string> GetConfigBuildAndRunCommand(BuildMode config)
        {
            string imageName = "testcontainer";
            StringBuilder buildCmd = new StringBuilder();
            StringBuilder runCmd = new StringBuilder();

            buildCmd.AppendFormat("docker build {0}", Directory.GetCurrentDirectory());
            buildCmd.AppendFormat(" --build-arg NETCORE_COMPOSITE={0}",
                                  config.NetCoreComposite);
            buildCmd.AppendFormat(" --build-arg NETCORE_INCLUDE_ASPNET={0}",
                                  config.NetCoreIncludeAspNet);
            buildCmd.AppendFormat(" --build-arg ASPNET_COMPOSITE={0}",
                                  config.AspNetComposite);
            buildCmd.AppendFormat(" --build-arg APP_R2R={0}",
                                  config.AppR2R);
            buildCmd.AppendFormat(" --build-arg APP_COMPOSITE={0}",
                                  config.AppComposite);
            buildCmd.AppendFormat(" --build-arg ONE_BIG_COMPOSITE={0}",
                                  config.OneBigComposite);
            buildCmd.AppendFormat(" --build-arg APP_AVX2={0}",
                                  config.AppAVX2);
            buildCmd.AppendFormat(" -t {0}", imageName);

            runCmd.Append("docker run");
            runCmd.AppendFormat(" --env COMPlus_ReadyToRun={0}",
                                config.UseReadyToRun ? "1" : "0");
            runCmd.AppendFormat(" --env COMPlus_TieredCompilation={0}",
                                config.UseTieredCompilation ? "1" : "0");
            runCmd.AppendFormat(" -it {0}", imageName);

            return new Tuple<string, string>(buildCmd.ToString(), runCmd.ToString());
        }

        static int Main(string[] args)
        {
            s_timestamp = DateTime.Now.ToString("MMdd-HHmm");
            s_folderName = Directory.GetCurrentDirectory();

            string xmlFile = Path.Combine(s_folderName, $"results-{s_timestamp}.xml");
            string r2rFailsXmlFile = Path.Combine(s_folderName, $"r2rfails-{s_timestamp}.xml");
            string resultsFile = Path.ChangeExtension(xmlFile, ".txt");

            if (args.Length > 0)
            {
                SimpleCommandLineParser parser = new SimpleCommandLineParser();
                List<KeyValuePair<string, string>> flags = parser.ParseOptions(args);

                foreach (var kvp in flags)
                {
                    switch (kvp.Key)
                    {
                        case "warmups":
                            WarmupIterations = Int32.Parse(kvp.Value);
                            break;

                        case "iterations":
                            Iterations = Int32.Parse(kvp.Value);
                            break;

                        case "first-n-configs":
                            Configs = Int32.Parse(kvp.Value);
                            break;

                        case "configs":
                            s_buildModes.SelectMatchingConfigs(kvp.Value);
                            break;

                        case "command-only":
                            BuildMode config = s_buildModes.GetSpecificConfig(kvp.Value);
                            Tuple<string, string> cmds = GetConfigBuildAndRunCommand(config);
                            Console.Write("Configuration Selected:\n{0}\n", config.Name);
                            Console.Write("\nDocker Build Command:\n{0}\n", cmds.Item1);
                            Console.Write("\nDocker Run Command:\n{0}\n", cmds.Item2);
                            return 0;

                        case "resolution-fails":
                            if (!OperatingSystem.IsWindows())
                            {
                                Console.WriteLine("The --resolution-fails flag is "
                                                  + "currently only supported on Windows."
                                                  + " Ignoring...");
                                break;
                            }
                            WarmupIterations = 0;
                            Iterations = 1;
                            ResolutionFailsInstead = true;
                            // s_buildModes.SelectMatchingConfigs("usereadytorun");
                            break;

                        case "":
                            if (ResolutionFailsInstead)
                            {
                                ProcessXmlFailsFile(kvp.Value, resultsFile);
                            }
                            else
                            {
                                ProcessXmlFile(kvp.Value, resultsFile);
                            }
                            return 0;

                        default:
                            Console.WriteLine("The flag '{0}' is invalid.", kvp.Key);
                            break;
                    }
                }

                if (s_buildModes.Count() < Configs)
                    Configs = s_buildModes.Count();
            }

            StringBuilder xml = new StringBuilder();
            xml.AppendLine("<Xml>");
            StringBuilder? r2rFailsXml = null;

            string buildLogFile = Path.Combine(s_folderName,
                                               $"jellyfin-build-{s_timestamp}.log");
            string execLogFile = Path.Combine(s_folderName,
                                               $"jellyfin-run-{s_timestamp}.log");
            int totalBuildModes = s_buildModes.Count();

            if (ResolutionFailsInstead)
            {
                r2rFailsXml = new StringBuilder();
                r2rFailsXml.AppendLine("<Xml>");
            }

            using (StreamWriter buildLogWriter = new StreamWriter(buildLogFile))
            using (StreamWriter execLogWriter = new StreamWriter(execLogFile))
            {
                s_buildLogFile = buildLogWriter;
                s_execLogFile = execLogWriter;

                if (Configs == 0)
                {
                    s_buildModes.EachWithIndex((mode, index) =>
                    {
                        BuildAndRun(mode, xml, r2rFailsXml!, index, totalBuildModes);
                    });
                }
                else
                {
                    s_buildModes.EachWithIndexUntil(Configs, (mode, index) =>
                    {
                        BuildAndRun(mode, xml, r2rFailsXml!, index, Configs);
                    });
                }

                s_buildLogFile = null;
                s_execLogFile = null;
            }

            xml.AppendLine("</Xml>");
            Console.WriteLine(new string('=', 70));
            Console.WriteLine(xml.ToString());

            File.WriteAllText(xmlFile, xml.ToString());

            if (ResolutionFailsInstead)
            {
                r2rFailsXml!.AppendLine("</Xml>");
                File.WriteAllText(r2rFailsXmlFile!, r2rFailsXml!.ToString());
                ProcessXmlFailsFile(r2rFailsXmlFile, resultsFile);
            }
            else
            {
                ProcessXmlFile(xmlFile, resultsFile);
            }
            return 0;
        }

        private static void BuildAndRun(in BuildMode buildMode,
                                        StringBuilder xml,
                                        StringBuilder r2rFailsXml,
                                        int index,
                                        int count)
        {
            string image = Build(buildMode, index, count);
            if (image == null || image.Contains("Failed"))
            {
                return;
            }

            WriteXmlHeader(xml, buildMode);
            if (ResolutionFailsInstead)
                WriteXmlHeader(r2rFailsXml, buildMode);

            // StringBuilder warmupBuilder = new StringBuilder();
            for (int warmupI = 0; warmupI < WarmupIterations; warmupI++)
            {
                if (OperatingSystem.IsLinux())
                    RunOnLinux(buildMode, image, xml);

                if (OperatingSystem.IsWindows())
                    RunOnWindows(buildMode, xml, r2rFailsXml);
            }

            for (int iteration = 0; iteration < Iterations; iteration++)
            {
                if (OperatingSystem.IsLinux())
                    RunOnLinux(buildMode, image, xml);

                if (OperatingSystem.IsWindows())
                    RunOnWindows(buildMode, xml, r2rFailsXml);
            }

            xml.AppendLine("</Results>");
            xml.AppendLine("</BuildAndRun>");

            if (ResolutionFailsInstead)
            {
                r2rFailsXml.AppendLine("</Results>");
                r2rFailsXml.AppendLine("</BuildAndRun>");
            }
        }

        private static string Build(in BuildMode buildMode, int index, int total)
        {
            if (OperatingSystem.IsLinux())
                return BuildForLinux(buildMode, index, total);

            if (OperatingSystem.IsWindows())
                return BuildForWindows(buildMode, index, total);

            return "Finished";
        }

        private static string BuildForLinux(in BuildMode buildMode, int index, int total)
        {
            Stopwatch sw = Stopwatch.StartNew();
            StringBuilder commandLine = new StringBuilder();
            Console.WriteLine("\nBuilding configuration: {0} ({1} / {2})",
                              buildMode.Name, index+1, total);

            commandLine.AppendFormat("build {0}", s_folderName);
            commandLine.AppendFormat(" --build-arg NETCORE_COMPOSITE={0}",
                                      buildMode.NetCoreComposite);
            commandLine.AppendFormat(" --build-arg NETCORE_INCLUDE_ASPNET={0}",
                                      buildMode.NetCoreIncludeAspNet);
            commandLine.AppendFormat(" --build-arg ASPNET_COMPOSITE={0}",
                                      buildMode.AspNetComposite);
            commandLine.AppendFormat(" --build-arg APP_R2R={0}",
                                      buildMode.AppR2R);
            commandLine.AppendFormat(" --build-arg APP_COMPOSITE={0}",
                                      buildMode.AppComposite);
            commandLine.AppendFormat(" --build-arg ONE_BIG_COMPOSITE={0}",
                                      buildMode.OneBigComposite);
            commandLine.AppendFormat(" --build-arg APP_AVX2={0}",
                                      buildMode.AppAVX2);

            ProcessStartInfo psi = new ProcessStartInfo()
            {
                FileName = "docker",
                Arguments = commandLine.ToString(),
                UseShellExecute = false,
            };

            string? imageId = null;
            int exitCode = RunProcess(psi, s_buildLogFile!, out List<string> stdout);

            if (exitCode == 0)
            {
                for (int i = stdout.Count - 1; i >= 0 && i >= stdout.Count - 10; i--)
                {
                    string line = stdout[i];
                    int writingImage = line.IndexOf(WindowsWritingImageString);

                    if (writingImage >= 0)
                    {
                        imageId = line.Substring(writingImage
                                                 + WindowsWritingImageString.Length);
                        break;
                    }
                }
            }

            Console.WriteLine("\nDone building configuration: {0} ({1} / {2}, {3} msecs)",
                              buildMode.Name, index+1, total, sw.ElapsedMilliseconds);
            return imageId!;
        }

        private static string BuildForWindows(in BuildMode buildMode, int index, int total)
        {
            Stopwatch sw = Stopwatch.StartNew();
            StringBuilder commandLine = new StringBuilder("SetupJellyfinServer.rb --build");
            Console.WriteLine("\nBuilding configuration: {0} ({1} / {2})",
                              buildMode.Name, index+1, total);

            if (buildMode.AppR2R)               commandLine.Append(" --appr2r");
            if (buildMode.AppComposite)         commandLine.Append(" --appcomposite");
            if (buildMode.AppAVX2)              commandLine.Append(" --appavx2");
            if (buildMode.NetCoreComposite)     commandLine.Append(" --netcorecomposite");
            if (buildMode.NetCoreIncludeAspNet) commandLine.Append(" --includeaspnet");
            if (buildMode.AspNetComposite)      commandLine.Append(" --aspnetcomposite");
            if (buildMode.OneBigComposite)      commandLine.Append(" --onebigcomposite");

            ProcessStartInfo psi = new ProcessStartInfo()
            {
                FileName = "ruby",
                Arguments = commandLine.ToString(),
                UseShellExecute = false,
            };

            string result = "";
            int exitCode = RunProcess(psi, s_buildLogFile!, out List<string> stdout);

            if (exitCode == 0) result = "Finished!";
            else result = "Failed!";

            Console.WriteLine("\nDone building configuration: {0} ({1} / {2}, {3} msecs)",
                              buildMode.Name, index+1, total, sw.ElapsedMilliseconds);
            return result;
        }

        private static bool RunOnLinux(BuildMode buildMode,
                                       string dockerImageId,
                                       StringBuilder xml)
        {
            StringBuilder commandLine = new StringBuilder();
            commandLine.Append("run");
            commandLine.AppendFormat(" --env COMPlus_ReadyToRun={0}",
                                      buildMode.UseReadyToRun ? "1" : "0");
            commandLine.AppendFormat(" --env COMPlus_TieredCompilation={0}",
                                      buildMode.UseTieredCompilation ? "1" : "0");
            commandLine.AppendFormat(" -it {0}", dockerImageId);

            ProcessStartInfo psi = new ProcessStartInfo()
            {
                FileName = "docker",
                Arguments = commandLine.ToString(),
                UseShellExecute = false,
            };

            int exitCode = RunProcess(psi, s_execLogFile!, out List<string> stdout);
            if (exitCode != 143)
            {
                return false;
            }

            for (int line = 0; line < stdout.Count; line++)
            {
                if (stdout[line] == "XMLXMLXML")
                {
                    line++;
                    while (line < stdout.Count && stdout[line] != "LMXLMXLMX")
                    {
                        string toWrite = $"    {stdout[line]}";
                        if (stdout[line].StartsWith("<Timing"))
                            toWrite = toWrite.Insert(0, "\n");

                        xml.AppendLine(toWrite);
                        line++;
                    }
                }
            }
            return true;
        }

        private static bool RunOnWindows(BuildMode buildMode,
                                         StringBuilder xml,
                                         StringBuilder r2rFailsXml)
        {
            StringBuilder commandLine = new StringBuilder("SetupJellyfinServer.rb --run");
            if (ResolutionFailsInstead) commandLine.Append(" RESOLUTION");
            else commandLine.Append(" BENCHMARK");

            if (buildMode.UseReadyToRun)        commandLine.Append(" --readytorun");
            if (buildMode.UseTieredCompilation) commandLine.Append(" --tieredcompilation");

            ProcessStartInfo psi = new ProcessStartInfo()
            {
                FileName = "ruby",
                Arguments = commandLine.ToString(),
                UseShellExecute = false,
            };

            int exitCode = RunProcess(psi, s_execLogFile!, out List<string> stdout);
            if (exitCode != 143)
            {
                return false;
            }

            for (int line = 0; line < stdout.Count; line++)
            {
                if (stdout[line] == "XMLXMLXML")
                {
                    line++;
                    while (line < stdout.Count && stdout[line] != "LMXLMXLMX")
                    {
                        string toWrite = $"    {stdout[line]}";
                        if (stdout[line].StartsWith("<Timing"))
                            toWrite = toWrite.Insert(0, "\n");

                        if (ResolutionFailsInstead)
                            r2rFailsXml.AppendLine(toWrite);

                        xml.AppendLine(toWrite);
                        line++;
                    }

                    if (!ResolutionFailsInstead)
                        continue;

                    r2rFailsXml.Append("\n    <R2RFails>\n");
                    while (++line < stdout.Count)
                    {
                        if ((line+1) >= stdout.Count || stdout[line+1] == "XMLXMLXML")
                            break;
                        string lineContent = stdout[line];
                        int startPos = lineContent.IndexOf("<R2RResolutionFailed>");
                        int endPos = lineContent.LastIndexOf("</R2RResolutionFailed>");
                        if (startPos >= 0 && endPos > startPos)
                        {
                            startPos += 21;
                            string ident = lineContent.Substring(startPos, endPos - startPos);

                            r2rFailsXml.AppendLine($"      <R2RResolutionFailed>{HttpUtility.HtmlEncode(ident)}</R2RResolutionFailed>");
                        }
                    }
                    r2rFailsXml.AppendLine("    </R2RFails>");
                }
            }
            return true;
        }

        private static int RunProcess(ProcessStartInfo psi,
                                      TextWriter logFile,
                                      out List<string> stdout)
        {
            Stopwatch sw = Stopwatch.StartNew();

            using (Process process = new Process())
            {
                process.StartInfo = psi;
                process.StartInfo.RedirectStandardOutput = true;
                process.StartInfo.RedirectStandardError = true;
                process.StartInfo.Environment["DOCKER_BUILDKIT"] = "1";

                List<string> stdoutLines = new List<string>();
                logFile.WriteLine("Running {0} {1}", psi.FileName, psi.Arguments);
                process.Start();

                process.OutputDataReceived += new DataReceivedEventHandler(
                (object sender, DataReceivedEventArgs eventArgs) =>
                {
                    string? data = eventArgs?.Data;
                    if (!string.IsNullOrEmpty(data))
                    {
                        Console.WriteLine(data);
                        logFile.WriteLine(data);
                        stdoutLines.Add(data);
                    }
                });

                process.ErrorDataReceived += new DataReceivedEventHandler(
                (object sender, DataReceivedEventArgs eventArgs) =>
                {
                    string? data = eventArgs?.Data;
                    if (!string.IsNullOrEmpty(data))
                    {
                        Console.Error.WriteLine(data);
                        logFile.WriteLine("!!" + data);
                        stdoutLines.Add(data);
                    }
                });

                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                process.WaitForExit();

                logFile.WriteLine(
                    "Finished in {0} msecs with exit code {1}: {2} {3}",
                    sw.ElapsedMilliseconds,
                    process.ExitCode,
                    psi.FileName,
                    psi.Arguments);

                stdout = stdoutLines;
                return process.ExitCode;
            }
        }

        private static void WriteXmlHeader(StringBuilder xml, BuildMode buildMode)
        {
            xml.AppendFormat("\n<BuildAndRun Name=\"{0}\">\n", buildMode.Name);
            xml.AppendFormat("  <NetCoreComposite>{0}</NetCoreComposite>\n",
                              buildMode.NetCoreComposite);
            xml.AppendFormat("  <NetCoreIncludeAspNet>{0}</NetCoreIncludeAspNet>\n",
                              buildMode.NetCoreIncludeAspNet);
            xml.AppendFormat("  <AspNetComposite>{0}</AspNetComposite>\n",
                              buildMode.AspNetComposite);
            xml.AppendFormat("  <AppR2R>{0}</AppR2R>\n",
                              buildMode.AppR2R);
            xml.AppendFormat("  <AppComposite>{0}</AppComposite>\n",
                              buildMode.AppComposite);
            xml.AppendFormat("  <OneBigComposite>{0}</OneBigComposite>\n",
                              buildMode.OneBigComposite);
            xml.AppendFormat("  <AppAVX2>{0}</AppAVX2>\n",
                              buildMode.AppAVX2);
            xml.AppendFormat("  <UseReadyToRun>{0}</UseReadyToRun>\n",
                              buildMode.UseReadyToRun);
            xml.AppendFormat("  <UseTieredCompilation>{0}</UseTieredCompilation>\n",
                              buildMode.UseTieredCompilation);
            xml.AppendLine("\n  <Results>");
        }

        struct ValueStatistics
        {
            public int Count;
            public int Sum;
            public long SumSquared;
            public int Min;
            public int Max;

            public int NonzeroCount => Math.Max(Count, 1);
            public int Average => Sum / NonzeroCount;

            public long Variance
            {
                get
                {
                    long avg = Average;
                    return avg * avg + (SumSquared - 2 * avg * Sum) / NonzeroCount;
                }
            }

            public int StandardDeviation => (int)Math.Sqrt(Variance);

            public void Add(int value)
            {
                if (Count == 0)
                {
                    Min = value;
                    Max = value;
                }
                else
                {
                    if (value < Min)
                    {
                        Min = value;
                    }
                    else if (value > Max)
                    {
                        Max = value;
                    }
                }
                Count++;
                Sum += value;
                SumSquared += (long)value * (long)value;
            }

            public void WriteTo(StringBuilder builder, string name)
            {
                builder.AppendLine($"{name,-30}: COUNT={Count,-5} AVG={Average,-5} INTERVAL={Max - Min,-5} STDDEV={StandardDeviation,-5}");
            }
        }

        struct PhaseStatistics
        {
            public ValueStatistics Total;
            public ValueStatistics User;
            public ValueStatistics System;

            public void Add(int total, int user, int system)
            {
                Total.Add(total);
                User.Add(user);
                System.Add(system);
            }

            public void WriteTo(StringBuilder builder, string name)
            {
                Total.WriteTo(builder, name + " (total)");
                User.WriteTo(builder, name + " (user)");
                System.WriteTo(builder, name + " (system)");
            }
        }

        private static void ProcessXmlFailsFile(string xmlFile, string resultsFile)
        {
            XmlDocument xmlDocument = new XmlDocument();
            xmlDocument.Load(xmlFile);

            List<KeyValuePair<BuildMode, HashSet<string>>> buildModeFailedResolutions = new List<KeyValuePair<BuildMode, HashSet<string>>>();

            foreach (XmlNode buildAndRun in xmlDocument.GetElementsByTagName("BuildAndRun"))
            {
                BuildMode buildMode = BuildMode.ParseXml(buildAndRun);
                HashSet<string> failedResolutions = new HashSet<string>();

                foreach (XmlNode resultChild in buildAndRun!["Results"]!.ChildNodes!)
                {
                    if (resultChild.Name == "R2RFails")
                    {
                        foreach (XmlNode xmlFailure in resultChild.ChildNodes!)
                        {
                            string functionName = xmlFailure.InnerText!;
                            failedResolutions.Add(functionName);
                        }
                    }
                }

                buildModeFailedResolutions.Add(new KeyValuePair<BuildMode, HashSet<string>>(buildMode, failedResolutions));
            }

            StringBuilder summary = new StringBuilder();
            summary.AppendLine("Summary");
            summary.AppendLine("=======");

            StringBuilder details = new StringBuilder();
            details.AppendLine("Details");
            details.AppendLine("=======");

            summary.AppendLine("FAILURES | BASELINE+ | BASELINE- | MODE");
            summary.AppendLine("=======================================");

            for (int buildModeIndex = 0; buildModeIndex < buildModeFailedResolutions.Count; buildModeIndex++)
            {
                BuildMode buildMode = buildModeFailedResolutions[buildModeIndex].Key;
                HashSet<string> resolutionFailures = buildModeFailedResolutions[buildModeIndex].Value;
                HashSet<string> baselineResolutionFailures = (buildModeIndex != 0 ? buildModeFailedResolutions[0].Value : new HashSet<string>());

                int failures = resolutionFailures.Count;
                int baselinePlus = resolutionFailures.Where(fail => !baselineResolutionFailures.Contains(fail)).Count();
                int baselineMinus = baselineResolutionFailures.Where(fail => !resolutionFailures.Contains(fail)).Count();

                summary.AppendLine($"{failures,8} | {baselinePlus,9} | {baselineMinus,9} | {buildMode.ToString()}");

                string detailTitle = buildMode.ToString();
                details.AppendLine(detailTitle);
                details.AppendLine(new string('=', detailTitle.Length));
                if (buildModeIndex == 0)
                {
                    details.AppendLine("BASELINE FAILURES");
                    details.AppendLine("=================");
                }
                else
                {
                    details.AppendLine("FAILURES ON TOP OF BASELINE");
                    details.AppendLine("===========================");
                }
                foreach (string failurePlus in resolutionFailures.Where(fail => !baselineResolutionFailures.Contains(fail)).OrderBy(fail => fail))
                {
                    details.AppendLine(failurePlus);
                }
                if (buildModeIndex != 0)
                {
                    details.AppendLine("MISSING BASELINE FAILURES");
                    details.AppendLine("=========================");
                    foreach (string failureMinus in baselineResolutionFailures.Where(fail => !resolutionFailures.Contains(fail)).OrderBy(fail => fail))
                    {
                        details.AppendLine(failureMinus);
                    }
                }
                details.AppendLine();
            }

            string results = summary.ToString() + Environment.NewLine + details.ToString();
            Console.Write(results);
            File.WriteAllText(resultsFile, results);
        }

        private static void ProcessXmlFile(string xmlFile, string resultsFile)
        {
            XmlDocument xmlDocument = new XmlDocument();
            xmlDocument.Load(xmlFile);

            StringBuilder details = new StringBuilder();
            details.AppendLine("Details");
            details.AppendLine("=======");

            StringBuilder summary = new StringBuilder();
            summary.AppendLine("Summary");
            summary.AppendLine("=======");
            summary.AppendLine("TOTAL |  %  | RUNTIME |  %  | APPHOST |  %  | WEBHOST |  %  |   APP   |  %  | MODE");
            summary.AppendLine("==================================================================================");

            int baselineTotal = 0;
            int baselineRuntime = 0;
            int baselineApphostDelta = 0;
            int baselineWebhostDelta = 0;
            int baselineAppDelta = 0;
            bool isBaseline = true;

            foreach (XmlNode buildAndRun in xmlDocument.GetElementsByTagName("BuildAndRun"))
            {
                BuildMode buildMode = BuildMode.ParseXml(buildAndRun);

                PhaseStatistics runtime = new PhaseStatistics();
                PhaseStatistics app = new PhaseStatistics();
                PhaseStatistics appHostInit = new PhaseStatistics();
                PhaseStatistics webHostStartAsync = new PhaseStatistics();

                foreach (XmlNode result in buildAndRun!["Results"]!.ChildNodes!)
                {
                    // Console.WriteLine("\nPHASE: {0}", result!.Attributes!["Phase"]!.InnerText!);
                    // Console.WriteLine("TOTAL_MSECS: {0}", result!["TotalTimeMsec"]!.InnerText!);
                    // Console.WriteLine("USER_MSECS: {0}", result!["UserTimeMsec"]!.InnerText!);
                    // Console.WriteLine("SYSTEM_TIME_MSEC: {0}", result!["SystemTimeMsec"]!.InnerText!);

                    string phase = result!.Attributes!["Phase"]!.InnerText!;
                    int totalMsecs = Convert.ToInt32(double.Parse(result!["TotalTimeMsec"]!.InnerText!));
                    int userMsecs = Convert.ToInt32(double.Parse(result!["UserTimeMsec"]!.InnerText!));
                    int systemMsecs = Convert.ToInt32(double.Parse(result!["SystemTimeMsec"]!.InnerText!));

                    switch (phase)
                    {
                        case "APP":
                            app.Add(totalMsecs, userMsecs, systemMsecs);
                            break;

                        case "RUNTIME":
                            runtime.Add(totalMsecs, userMsecs, systemMsecs);
                            break;

                        case "APPHOST-INIT":
                            appHostInit.Add(totalMsecs, userMsecs, systemMsecs);
                            break;

                        case "WEBHOST-START-ASYNC":
                            webHostStartAsync.Add(totalMsecs, userMsecs, systemMsecs);
                            break;

                        default:
                            throw new NotImplementedException("Unknown phase: " + phase);
                    }
                }

                String buildModeName = buildMode.ToString();
                details.AppendLine(buildModeName);
                details.AppendLine(new string('=', buildModeName.Length));
                runtime.WriteTo(details, "RUNTIME");

                appHostInit.WriteTo(details, "APPHOST-INIT");
                webHostStartAsync.WriteTo(details, "WEBHOST-START-ASYNC");
                app.WriteTo(details, "APP");
                details.AppendLine();

                int apphostDelta = appHostInit.Total.Average - runtime.Total.Average;
                int webhostDelta = webHostStartAsync.Total.Average - appHostInit.Total.Average;
                int appDelta = app.Total.Average - webHostStartAsync.Total.Average;

                if (isBaseline)
                {
                    isBaseline = false;
                    baselineTotal = app.Total.Average;
                    baselineRuntime = runtime.Total.Average;
                    baselineApphostDelta = apphostDelta;
                    baselineWebhostDelta = webhostDelta;
                    baselineAppDelta = appDelta;
                }

                summary.AppendFormat("{0,5} | {1,3} | ",
                                      app.Total.Average,
                                      Percentage(app.Total.Average, baselineTotal));
                summary.AppendFormat("{0,7} | {1,3} | ",
                                      runtime.Total.Average,
                                      Percentage(runtime.Total.Average, baselineRuntime));
                summary.AppendFormat("{0,7} | {1,3} | ",
                                      apphostDelta,
                                      Percentage(apphostDelta, baselineApphostDelta));
                summary.AppendFormat("{0,7} | {1,3} | ",
                                      webhostDelta,
                                      Percentage(webhostDelta, baselineWebhostDelta));
                summary.AppendFormat("{0,7} | {1,3} | ",
                                      appDelta,
                                      Percentage(appDelta, baselineAppDelta));
                summary.AppendLine(buildMode.Name);
            }

            string results = summary.ToString() + Environment.NewLine + details.ToString();
            Console.Write(results);
            File.WriteAllText(resultsFile, results);
        }

        private static int Percentage(int numerator, int denominator)
        {
            return (int)(numerator * 100.0 / Math.Max(denominator, 1));
        }
    }
#pragma warning restore CA1305
#pragma warning restore CA1307
#pragma warning restore CA1310
}

        //private static BuildMode[] s_buildModes =
        //{
        //    new BuildMode()
        //    {
        //        Name = "baseline",
        //        NetCoreComposite = false,
        //        NetCoreIncludeAspNet = false,
        //        AspNetComposite = false,
        //        AppR2R = false,
        //        AppComposite = false,
        //        OneBigComposite = false,
        //        UseReadyToRun = false,
        //        UseTieredCompilation = false,
        //    },
        //    new BuildMode()
        //    {
        //        Name = "r2r",
        //        NetCoreComposite = false,
        //        NetCoreIncludeAspNet = false,
        //        AspNetComposite = false,
        //        AppR2R = true,
        //        AppComposite = false,
        //        OneBigComposite = false,
        //        UseReadyToRun = false,
        //        UseTieredCompilation = false,
        //    },
        //    new BuildMode()
        //    {
        //        Name = "app-composite-avx2",
        //        NetCoreComposite = false,
        //        NetCoreIncludeAspNet = false,
        //        AspNetComposite = false,
        //        AppR2R = true,
        //        AppComposite = true,
        //        OneBigComposite = false,
        //        AppAVX2 = true,
        //        UseReadyToRun = false,
        //        UseTieredCompilation = false,
        //    },
        //    new BuildMode()
        //    {
        //        Name = "one-big-composite-avx2",
        //        NetCoreComposite = false,
        //        NetCoreIncludeAspNet = false,
        //        AspNetComposite = false,
        //        AppR2R = true,
        //        AppComposite = true,
        //        OneBigComposite = true,
        //        AppAVX2 = true,
        //        UseReadyToRun = false,
        //        UseTieredCompilation = false,
        //    },
        //    new BuildMode()
        //    {
        //        Name = "r2r-platform-composite-avx2",
        //        NetCoreComposite = true,
        //        NetCoreIncludeAspNet = true,
        //        AspNetComposite = true,
        //        AppR2R = true,
        //        AppComposite = true,
        //        OneBigComposite = false,
        //        AppAVX2 = true,
        //        UseReadyToRun = false,
        //        UseTieredCompilation = false,
        //    },
        //    new BuildMode()
        //    {
        //        Name = "jit-platform-composite-avx2",
        //        NetCoreComposite = true,
        //        NetCoreIncludeAspNet = true,
        //        AspNetComposite = true,
        //        AppR2R = false,
        //        AppComposite = false,
        //        OneBigComposite = false,
        //        AppAVX2 = true,
        //        UseReadyToRun = false,
        //        UseTieredCompilation = false,
        //    },
        //};

            // SimpleCommandLineParser parser = new SimpleCommandLineParser();
            // Dictionary<string, string> result = parser.ParseOptions(args);

            // foreach (var kvp in result)
            //     Console.WriteLine("{0}, {1}", kvp.Key, kvp.Value);
            // return 0;

            // if (args.Length < 2)
            // {
            //     Console.WriteLine("Make sure to pass the number of warmup iterations"
            //                       + " and actual iterations you wish to run :)");
            //     return -1;
            // }

            // WarmupIterations = Int32.Parse(args[0]);
            // Iterations = Int32.Parse(args[1]);
            // Configs = (args.Length > 2) ? Int32.Parse(args[2]) : 0;

