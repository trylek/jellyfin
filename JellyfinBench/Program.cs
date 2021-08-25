using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Xml;

namespace JellyfinBench
{
    class Program
    {
        const string JellyfinFolderName = "d:\\git\\jellyfin";
        const string WindowsWritingImageString = "writing image sha256:";
        const int Iterations = 10;

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
        }

        private static BuildMode[] s_buildModes =
        {
            new BuildMode()
            {
                Name = "baseline",
                NetCoreComposite = false,
                NetCoreIncludeAspNet = false,
                AspNetComposite = false,
                AppR2R = false,
                AppComposite = false,
                OneBigComposite = false,
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
            },
        };

        static string s_timestamp;

        static TextWriter s_logFile;

        static int Main(string[] args)
        {
            s_timestamp = DateTime.Now.ToString("MMdd-HHmm");
            if (args.Length > 0)
            {
                ProcessXmlFile(args[0]);
                return 0;
            }

            StringBuilder xml = new StringBuilder();
            xml.AppendLine("<Xml>");
            string logFile = Path.Combine(JellyfinFolderName, $"jellyfin-bench-{s_timestamp}.log");
            using (StreamWriter writer = new StreamWriter(logFile))
            {
                s_logFile = writer;
                foreach (BuildMode mode in s_buildModes)
                {
                    BuildAndRun(mode, xml);
                }
                s_logFile = null;
            }
            /*
            BuildAndRun(
                new BuildMode()
                {
                    NetCoreComposite = true,
                    NetCoreIncludeAspNet = true,
                    AspNetComposite = false,
                    AppR2R = true,
                    AppComposite = true,
                    OneBigComposite = false
                },
                xml);
            BuildAndRun(
                new BuildMode()
                {
                    NetCoreComposite = false,
                    NetCoreIncludeAspNet = false,
                    AspNetComposite = false,
                    AppR2R = true,
                    AppComposite = true,
                    OneBigComposite = false,
                },
                xml);
            */
            xml.AppendLine("</Xml>");
            Console.WriteLine(new string('=', 70));
            Console.WriteLine(xml.ToString());
            string fileName = Path.Combine(JellyfinFolderName, $"results-{s_timestamp}.xml");
            File.WriteAllText(fileName, xml.ToString());
            return 0;
        }

        private static void BuildAndRun(in BuildMode buildMode, StringBuilder xml)
        {
            string image = Build(buildMode);
            if (image == null)
            {
                return;
            }
            xml.AppendFormat("<BuildAndRun Name=\"{0}\">\n", buildMode.Name);
            xml.AppendFormat("<NetCoreComposite>{0}</NetCoreComposite>\n", buildMode.NetCoreComposite);
            xml.AppendFormat("<NetCoreIncludeAspNet>{0}</NetCoreIncludeAspNet>\n", buildMode.NetCoreIncludeAspNet);
            xml.AppendFormat("<AspNetComposite>{0}</AspNetComposite>\n", buildMode.AspNetComposite);
            xml.AppendFormat("<AppR2R>{0}</AppR2R>\n", buildMode.AppR2R);
            xml.AppendFormat("<AppComposite>{0}</AppComposite>\n", buildMode.AppComposite);
            xml.AppendFormat("<OneBigComposite>{0}</OneBigComposite>\n", buildMode.OneBigComposite);
            xml.AppendFormat("<AppAVX2>{0}</AppAVX2>\n", buildMode.AppAVX2);
            xml.AppendLine("<Results>");
            for (int iteration = 0; iteration < Iterations; iteration++)
            {
                Run(image, xml);
            }
            xml.AppendLine("</Results>");
            xml.AppendLine("</BuildAndRun>");
        }

        private static string Build(in BuildMode buildMode)
        {
            StringBuilder commandLine = new StringBuilder();
            commandLine.AppendFormat("build {0}", JellyfinFolderName);
            commandLine.AppendFormat(" --build-arg NETCORE_COMPOSITE={0}", buildMode.NetCoreComposite);
            commandLine.AppendFormat(" --build-arg NETCORE_INCLUDE_ASPNET={0}", buildMode.NetCoreIncludeAspNet);
            commandLine.AppendFormat(" --build-arg ASPNET_COMPOSITE={0}", buildMode.AspNetComposite);
            commandLine.AppendFormat(" --build-arg APP_R2R={0}", buildMode.AppR2R);
            commandLine.AppendFormat(" --build-arg APP_COMPOSITE={0}", buildMode.AppComposite);
            commandLine.AppendFormat(" --build-arg ONE_BIG_COMPOSITE={0}", buildMode.OneBigComposite);

            ProcessStartInfo psi = new ProcessStartInfo()
            {
                FileName = "docker",
                Arguments = commandLine.ToString(),
                UseShellExecute = false,
            };

            int exitCode = RunProcess(psi, out List<string> stdout);
            if (exitCode != 0)
            {
                return null;
            }
            for (int i = stdout.Count - 1; i >= 0 && i >= stdout.Count - 10; i--)
            {
                string line = stdout[i];
                int writingImage = line.IndexOf(WindowsWritingImageString);
                if (writingImage >= 0)
                {
                    return line.Substring(writingImage + WindowsWritingImageString.Length);
                }
            }
            return null;
        }

        private static bool Run(string dockerImageId, StringBuilder xml)
        {
            StringBuilder commandLine = new StringBuilder();
            commandLine.Append("run");
            commandLine.AppendFormat(" -it {0}", dockerImageId);

            ProcessStartInfo psi = new ProcessStartInfo()
            {
                FileName = "docker",
                Arguments = commandLine.ToString(),
                UseShellExecute = false,
            };

            int exitCode = RunProcess(psi, out List<string> stdout);
            if (exitCode != 143)
            {
                return false;
            }
            for (int line = 0; line < stdout.Count; line++)
            {
                if (stdout[line] == "XMLXMLXML")
                {
                    int startLine = ++line;
                    while (line < stdout.Count && stdout[line] != "LMXLMXLMX")
                    {
                        xml.AppendLine(stdout[line++]);
                    }
                }
            }
            return true;
        }

        private static int RunProcess(ProcessStartInfo psi, out List<string> stdout)
        {
            Stopwatch sw = Stopwatch.StartNew();

            using (Process process = new Process())
            {
                process.StartInfo = psi;
                process.StartInfo.RedirectStandardOutput = true;
                process.StartInfo.RedirectStandardError = true;
                process.StartInfo.Environment["DOCKER_BUILDKIT"] = "1";

                s_logFile.WriteLine("Running {0} {1}", psi.FileName, psi.Arguments);
                process.Start();

                List<string> stdoutLines = new List<string>();
                process.OutputDataReceived += new DataReceivedEventHandler((object sender, DataReceivedEventArgs eventArgs) =>
                {
                    string data = eventArgs?.Data;
                    if (!string.IsNullOrEmpty(data))
                    {
                        Console.WriteLine(data);
                        s_logFile.WriteLine(data);
                        stdoutLines.Add(data);
                    }
                });
                process.ErrorDataReceived += new DataReceivedEventHandler((object sender, DataReceivedEventArgs eventArgs) =>
                {
                    string data = eventArgs?.Data;
                    if (!string.IsNullOrEmpty(data))
                    {
                        Console.Error.WriteLine(data);
                        s_logFile.WriteLine("!!" + data);
                        stdoutLines.Add(data);
                    }
                });

                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                process.WaitForExit();

                s_logFile.WriteLine(
                    "Finished in {0} msecs with exit code {1}: {2} {3}",
                    sw.ElapsedMilliseconds,
                    process.ExitCode,
                    psi.FileName,
                    psi.Arguments);

                stdout = stdoutLines;

                return process.ExitCode;
            }
        }

        struct ValueStatistics
        {
            public int Count;
            public int Sum;
            public long SumSquared;
            public int Min;
            public int Max;

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

            public void Print(string name)
            {
                int nonzeroCount = Math.Max(Count, 1);
                int average = Sum / nonzeroCount;
                int stddev = (int)Math.Sqrt(average * (long)average + (SumSquared - 2 * average * (long)Sum) / nonzeroCount);
                Console.WriteLine($"{name,-20}: COUNT={Count,-5} AVG={average,-5} INTERVAL={Max - Min,-5} STDDEV={stddev,-5}");
            }
        }

        struct PhaseStatistics
        {
            ValueStatistics Total;
            ValueStatistics User;
            ValueStatistics System;

            public void Add(int total, int user, int system)
            {
                Total.Add(total);
                User.Add(user);
                System.Add(system);
            }

            public void Print(string name)
            {
                Total.Print(name + " (total)");
                User.Print(name + " (user)");
                System.Print(name + " (system)");
            }
        }

        private static void ProcessXmlFile(string fileName)
        {
            XmlDocument xmlDocument = new XmlDocument();
            xmlDocument.Load(fileName);
            foreach (XmlNode buildAndRun in xmlDocument.GetElementsByTagName("BuildAndRun"))
            {
                string name = buildAndRun.Attributes["Name"].InnerText;
                bool netCoreComposite = bool.Parse(buildAndRun["NetCoreComposite"].InnerText);
                bool netCoreIncludeAspNet = bool.Parse(buildAndRun["NetCoreIncludeAspNet"].InnerText);
                bool aspNetComposite = bool.Parse(buildAndRun["AspNetComposite"].InnerText);
                bool appR2R = bool.Parse(buildAndRun["AppR2R"].InnerText);
                bool appComposite = bool.Parse(buildAndRun["AppComposite"].InnerText);
                bool oneBigComposite = bool.Parse(buildAndRun["OneBigComposite"].InnerText);
                bool appAvx2 = bool.Parse(buildAndRun["AppAVX2"].InnerText);

                PhaseStatistics runtime = new PhaseStatistics();
                PhaseStatistics app = new PhaseStatistics();

                foreach (XmlNode result in buildAndRun["Results"].ChildNodes)
                {
                    string phase = result.Attributes["Phase"].InnerText;
                    int totalMsecs = int.Parse(result["TotalTimeMsec"].InnerText);
                    int userMsecs = int.Parse(result["UserTimeMsec"].InnerText);
                    int systemMsecs = int.Parse(result["SystemTimeMsec"].InnerText);

                    switch (phase)
                    {
                        case "APP":
                            app.Add(totalMsecs, userMsecs, systemMsecs);
                            break;

                        case "RUNTIME":
                            runtime.Add(totalMsecs, userMsecs, systemMsecs);
                            break;

                        default:
                            throw new NotImplementedException("Unknown phase: " + phase);
                    }
                }

                StringBuilder buildModeName = new StringBuilder();
                buildModeName.Append(name);
                buildModeName.Append(": ");
                if (oneBigComposite)
                {
                    buildModeName.Append("one big composite");
                }
                else
                {
                    buildModeName.AppendFormat(".NET Core{0}={1}",
                        netCoreIncludeAspNet ? "+ASP.NET" : "",
                        netCoreComposite ? "composite" : "default");
                    if (!netCoreIncludeAspNet)
                    {
                        buildModeName.AppendFormat(" / ASP.NET={0}", aspNetComposite ? "composite" : "default");
                    }
                    buildModeName.AppendFormat(" / APP={0}", !appR2R ? "JIT" : !appComposite ? "R2R" : "composite");
                }
                if (appAvx2)
                {
                    buildModeName.Append(" / AVX2");
                }

                Console.WriteLine(buildModeName.ToString());
                Console.WriteLine(new string('=', buildModeName.Length));
                app.Print("APP");
                runtime.Print("RUNTIME");
                Console.WriteLine();
            }
        }
    }
}
