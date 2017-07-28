/* (c) 2017 James Renwick */
using System;
using System.IO;

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
                var step = new SetupStep($"git clone -v {repoRemote} {repoDir}");
                var res = new CommandRunner().runCommand(step, 1000 * 10);

                if (res.ExitCode != 0) {
                    throw new Exception("Unable to create result saver: cannot clone repository: "
                        + $"git exited with {res.ExitCode}.");
                }
            }
        }

        private static void printResults<T>(StreamWriter file, StepResults<T> results)
            where T : ICommand
        {
            foreach (var step in results.Steps)
            {
                file.WriteLine("---------------------------------------------------------------------------");
                file.WriteLine($"--{step.Step.Command}\n--");

                foreach (var line in step.Lines)
                {
                    if (line.Source == Stream.Stdout) file.Write("--");
                    file.WriteLine(line.Line);
                }
            }
        }

        public void saveResults(TaskDefinition task, TaskResults results, TestResults tests)
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
                        //var cmd = new SetupStep($"git checkout -B {Branch}");
                        //var res = new CommandRunner().runCommand(cmd, 1000 * 10, Path);

                        // if (res.ExitCode != 0) {
                        //     Console.WriteLine("Cannot save results");
                        //     throw new Exception($"Unable to save results: git checkout returned {res.ExitCode}");
                        // }

                        // Create directory as needed
                        Directory.CreateDirectory(outdir);

                        // Write build log
                        using (var file = new StreamWriter(File.Create(System.IO.Path.Combine(outdir,"log.hs"))))
                        {
                            file.WriteLine($"--Beginning setup stage of {task.Title}@{task.CommitReference}\n--");
                            printResults(file, results.SetupResults);

                            if (results.SetupResults.Success)
                            {
                                file.WriteLine($"--Beginning build stage of {task.Title}@{task.CommitReference}\n--");
                                printResults(file, results.BuildResults);

                                if (results.BuildResults.Success)
                                {
                                    file.WriteLine($"--Beginning test stage of {task.Title}@{task.CommitReference}\n--");
                                    printResults(file, results.TestResults);

                                    if (!results.TestResults.Success) {
                                        file.WriteLine("\n\nFAILED at test stage.");
                                    }

                                    file.WriteLine($"\n\ntests_run = {tests.TestsRun}");
                                    file.WriteLine($"tests_passed = {tests.TestsSucceeded}");
                                    file.WriteLine($"tests_failed = {tests.TestsFailed}");
                                }
                                else file.WriteLine("\n\nFAILED at build stage.");
                            }
                            else file.WriteLine("\n\nFAILED at setup stage.");
                        }

                        // Create SVG badges
                        using (var file = new StreamWriter(File.Create(
                            System.IO.Path.Combine(outdir, "build-status.svg"))))
                        {
                            if (results.BuildResults.Success) {
                                file.Write(getStatusSVG("build", "passing", DefaultPassColor));
                            }
                            else file.Write(getStatusSVG("build", "failing", DefaultFailColor));
                        }
                        using (var file = new StreamWriter(File.Create(
                            System.IO.Path.Combine(outdir, "test-status.svg"))))
                        {
                            if (tests.Succeeded) {
                                file.Write(getStatusSVG("tests", "passing", DefaultPassColor));
                            }
                            else file.Write(getStatusSVG("tests", "failing", DefaultFailColor));
                        }

                        var cmd = new SetupStep("git add --all");
                        var res = new CommandRunner().runCommand(cmd, 1000 * 10, outdir);

                        cmd = new SetupStep($"git commit -m \"Results for '{task.Title}'");
                        res = new CommandRunner().runCommand(cmd, 1000 * 10, outdir);

                        cmd = new SetupStep($"git push -u origin {Branch}");
                        res = new CommandRunner().runCommand(cmd, 1000 * 20, outdir);
                        break;
                    }
                }
                catch (Exception e) {

                }
                System.Threading.Thread.Sleep(3000);
            }
            if (retry == 3) {
                throw new Exception("Unable to save results: cannot create lock file");
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

        public string getStatusSVG(string topic, string statusLabel, string bgColor)
        {
            return String.Format(SVGTemplate, topic, statusLabel, bgColor);
        }
    }
}
