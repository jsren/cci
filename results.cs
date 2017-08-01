/* (c) 2017 James Renwick */
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace cci
{
    public class GitResultSaver
    {
        public string Path { get; private set; }
        public string Repo { get; private set; }
        public string Branch { get; private set; }

        public GitResultSaver(string repoDir, string repoRemote, string repoBranch)
        {
            this.Path = repoDir;
            this.Repo = repoRemote;
            this.Branch = repoBranch;

            if (!Directory.Exists(repoDir))
            {
                var step = new SystemCommand($"git clone -v {repoRemote} {repoDir}");
                var res = new CommandRunner().RunCommand(step, 1000 * 10);

                if (res.ExitCode != 0) {
                    throw new Exception("Unable to create result saver: cannot clone repository: "
                        + $"git exited with {res.ExitCode}.");
                }
            }
        }

        private static void PrintResults(StreamWriter file, 
            IEnumerable<StageResults> stageOutputs)
        {
            foreach (var stage in stageOutputs)
            {
                file.WriteLine($"--Stage \"{stage.Stage}\"");
                file.WriteLine("---------------------------------------------------------------------------");
                foreach (var step in stage.Results)
                {
                    file.WriteLine($"--{step.Command.Command}\n--");
                    foreach (var line in step.Lines)
                    {
                        if (line.Source == OutputStream.Stdout) file.Write("--");
                        file.WriteLine(line.Text);
                    }
                    if (step.TimedOut) {
                        file.WriteLine("\n[ERROR] Command timed out\n");
                    }
                    else if (step.ExitCode != 0) {
                        file.WriteLine($"\n[ERROR] Command exited with {step.ExitCode}\n");
                    }
                }
                file.WriteLine();
            }
        }

        private static void PrintResults(StreamWriter file, IEnumerable<TestResults> testOutputs)
        {
            foreach (var run in testOutputs)
            {
                file.WriteLine("---------------------------------------------------------------------------");
                file.WriteLine($"Of {run.TestsRun} tests, {run.TestsSucceeded} succeeded, {run.TestsFailed} failed.\n");

                file.WriteLine("--Test Suites:");
                foreach (var suite in run.TestSuites)
                {
                    var status = (!suite.Ran) ? "did not run" : (suite.Succeeded ? "succeeded" : "FAILED");
                    file.WriteLine($"--Suite '{suite.Name}' {status}");
                }
                file.WriteLine("--Tests:");
                foreach (var test in run.Tests)
                {
                    var status = (!test.Ran) ? "did not run" : (test.Succeeded ? "succeeded" : "FAILED");
                    file.WriteLine($"--Test '{test.Name}' {status}");
                    if (test.Ran && !test.Succeeded) file.WriteLine(test.Summary);
                }
            }
        }

        public void SaveResults(ProjectDefinition task, RunResults results, IEnumerable<TestResults> tests)
        {
            var outdir = System.IO.Path.Combine(Path, task.Title);

            // Retry with timeout if another CCI thread is accessing repo
            int retry = 0;
            for (; retry < 3; retry++)
            {
                try
                {
                    using (var _ = File.Create(".cci.lock", 64, FileOptions.DeleteOnClose))
                    {
                        // Checkout branch
                        var cmd = new SystemCommand($"git checkout -B {Branch}");
                        var res = new CommandRunner().RunCommand(cmd, 1000 * 10, Path);

                        if (res.ExitCode != 0) {
                            Console.WriteLine($"[ERROR] Unable to save results: git checkout returned {res.ExitCode}");
                        }

                        // Create directory as needed
                        Directory.CreateDirectory(outdir);

                        // Write build log
                        using (var file = new StreamWriter(File.Create(System.IO.Path.Combine(outdir, "log.hs"))))
                        {
                            PrintResults(file, new[] { results.SetupResults, results.BuildResults, results.TestResults });
                        }
                        // Write test result log
                        using (var file = new StreamWriter(File.Create(System.IO.Path.Combine(outdir, "tests.hs"))))
                        {
                            PrintResults(file, tests);
                        }

                        // Create SVG badges
                        using (var file = new StreamWriter(File.Create(
                            System.IO.Path.Combine(outdir, "build-status.svg"))))
                        {
                            if (results.BuildResults.Successful) {
                                file.Write(GetStatusSVG("build", "passing", DefaultPassColor));
                            }
                            else file.Write(GetStatusSVG("build", "failing", DefaultFailColor));
                        }
                        using (var file = new StreamWriter(File.Create(
                            System.IO.Path.Combine(outdir, "test-status.svg"))))
                        {
                            
                            if (tests.All((t) => t.TestsFailed == 0)) {
                                file.Write(GetStatusSVG("tests", "passing", DefaultPassColor));
                            }
                            else file.Write(GetStatusSVG("tests", "failing", DefaultFailColor));
                        }

                        // Upload to git repo
                        cmd = new SystemCommand("git add --all");
                        res = new CommandRunner().RunCommand(cmd, 1000 * 10, outdir);

                        cmd = new SystemCommand($"git commit -m \"Results for '{task.Title}'");
                        res = new CommandRunner().RunCommand(cmd, 1000 * 10, outdir);

                        cmd = new SystemCommand($"git push -u origin {Branch}");
                        res = new CommandRunner().RunCommand(cmd, 1000 * 20, outdir);
                        break;
                    }
                }
                catch (Exception e) {
                    Console.WriteLine("[ERROR] Exception when writing results: {0}", e.ToString());
                }
                System.Threading.Thread.Sleep(3000);
            }
        }


        private static string SVGTemplate =
@"<?xml version='1.0'?>
<svg xmlns='http://www.w3.org/2000/svg' width='100' height='20'>
<linearGradient id='a' x2='0' y2='100%'>
    <stop offset='0' stop-color='#bbb' stop-opacity='.1'/>
    <stop offset='1' stop-opacity='.1'/>
</linearGradient>
<rect rx='3' width='100' height='20' fill='#555'/>
<rect rx='3' x='45' width='55' height='20' fill='{2}'/>
<path fill='{2}' d='M45 0h4v20h-4z'/>
<rect rx='3' width='100' height='20' fill='url(#a)'/>
<g fill='#fff' text-anchor='middle' font-family='DejaVu Sans,Verdana,Geneva,sans-serif' font-size='11'>
    <text x='24' y='15' fill='#010101' fill-opacity='.3'>{0}</text>
    <text x='24' y='14'>{0}</text>
    <text x='72' y='15' fill='#010101' fill-opacity='.3'>{1}</text>
    <text x='72' y='14'>{1}</text>
</g>
</svg>
";
        public static readonly string DefaultPassColor = "#4c1";
        public static readonly string DefaultFailColor = "#c30";

        public string GetStatusSVG(string topic, string statusLabel, string bgColor)
        {
            return String.Format(SVGTemplate, topic, statusLabel, bgColor);
        }
    }
}
