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

        public void saveResults(TaskDefinition task, TaskResults results, ITestResultParser tests)
        {
            // Retry with timeout if another CCI thread is accessing repo
            int retry = 0;
            for (; retry < 3; retry++)
            {
                try
                {
                    using (var _ = File.Create(".cci.lock", 64, FileOptions.DeleteOnClose))
                    {
                        // Checkout branch
                        var cmd = new SetupStep($"git checkout -B {Branch}");
                        var res = new CommandRunner().runCommand(cmd, 1000 * 10, Path);

                        if (res.ExitCode != 0) {
                            throw new Exception($"Unable to save results: git checkout returned {res.ExitCode}");
                        }

                        // Now update file & push
                        using (var file = new StreamWriter(File.Create(System.IO.Path.Combine(Path, task.Title + ".hs"))))
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

                        cmd = new SetupStep("git add --all");
                        res = new CommandRunner().runCommand(cmd, 1000 * 10, Path);

                        cmd = new SetupStep($"git commit -m {Guid.NewGuid()}");
                        res = new CommandRunner().runCommand(cmd, 1000 * 10, Path);

                        cmd = new SetupStep($"git push -u origin {Branch}");
                        res = new CommandRunner().runCommand(cmd, 1000 * 20, Path);
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
    }
}
