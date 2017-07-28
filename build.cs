/* (c) 2017 James Renwick */
using System.Collections.Generic;
using Newtonsoft.Json;
using System.Diagnostics;
using System.Linq;
using System;

namespace cci
{
    public interface ICommand
    {
        string Name { get; }
        string Command { get; }
        int Timeout { get; }
        string WorkingDirectory { get; }
        Dictionary<string, string> Environment { get; }
    }


    public class SetupStep : ICommand
    {
        [JsonProperty]
        public string Name { get; private set; }
        [JsonProperty]
        public string Command { get; private set; }
        [JsonProperty]
        public string WorkingDirectory { get; private set; }
        [JsonProperty]
        public Dictionary<string, string> Environment { get; private set; }

        [JsonProperty]
        public int Timeout { get; private set; }

        public SetupStep(string command, int timeout=0)
        {
            Command = command;
            Timeout = timeout;
        }
    }


    public class BuildStep : ICommand
    {
        [JsonProperty]
        public string Name { get; private set; }
        [JsonProperty]
        public string Command { get; private set; }
        [JsonProperty]
        public string WorkingDirectory { get; private set; }
        [JsonProperty]
        public Dictionary<string, string> Environment { get; private set; }

        [JsonProperty]
        public int Timeout { get; private set; }
    }


    public class TestStep : ICommand
    {
        [JsonProperty]
        public string Name { get; private set; }
        [JsonProperty]
        public string Command { get; private set; }
        [JsonProperty]
        public string ResultParser { get; private set; }
        [JsonProperty]
        public string WorkingDirectory { get; private set; }
        [JsonProperty]
        public Dictionary<string, string> Environment { get; private set; }
        [JsonProperty]
        public int Timeout { get; private set; }
    }


    public class TaskDefinition
    {
        [JsonProperty]
        public string Title { get; private set; }
        [JsonProperty]
        public string Repository { get; private set; }
        [JsonProperty]
        public string CommitReference { get; private set; }
        [JsonProperty]
        public TestStep[] TestSteps { get; private set; }
        [JsonProperty]
        public BuildStep[] BuildSteps { get; private set; }
        [JsonProperty]
        public int DefaultStepTimeout { get; private set; }

        public static TaskDefinition fromJSON(string json)
        {
            return JsonConvert.DeserializeObject<TaskDefinition>(json);
        }
    }

    public enum Stream
    {
        Stdout,
        Stderr
    }

    public struct OutputLine
    {
        public Stream Source { get; private set; }
        public string Line { get; private set; }

        public OutputLine(Stream source, string line)
        {
            Source = source;
            Line = line;
        }
    }


    public class StepResult<Command> where Command : ICommand
    {
        public Command Step { get; private set; }
        public OutputLine[] Lines { get; private set; }
        public int ExitCode { get; private set; }
        public bool TimedOut { get; private set; }
        public TimeSpan Duration { get; private set; }
        public DateTime StartTime { get; private set; }

        public StepResult(Command step, OutputLine[] lines, int exitCode,
            bool timedOut, TimeSpan duration, DateTime startTime)
        {
            Step = step;
            Lines = lines;
            ExitCode = exitCode;
            TimedOut = timedOut;
            Duration = duration;
            StartTime = startTime;
        }
    }


    public class CommandRunner
    {
        public StepResult<Command> runCommand<Command>(Command step,
            int timeout = 0, string baseDir = null) where Command : ICommand
        {
            var parts = step.Command.Split(new char[] { ' ' }, 2);
            var proc = new ProcessStartInfo(parts[0], parts.Length > 1 ? parts[1] : "");

            // Get working directory
            string dir = baseDir ?? ".";
            if (step.WorkingDirectory != null) {
                dir = System.IO.Path.Combine(dir, step.WorkingDirectory);
            }
            // Get environment
            if (step.Environment != null) {
                proc.Environment.Concat(step.Environment);
            }
            proc.WorkingDirectory = dir;
            proc.CreateNoWindow = true;
            proc.RedirectStandardError = true;
            proc.RedirectStandardOutput = true;
            proc.UseShellExecute = false;

            using (var process = new Process())
            {
                var lines = new List<OutputLine>(20);

                process.EnableRaisingEvents = true;
                process.ErrorDataReceived += (s, e) => {
                    if (e.Data != null) lock (lines) lines.Add(new OutputLine(Stream.Stderr, e.Data));
                };
                process.OutputDataReceived += (s, e) => {
                    if (e.Data != null) lock (lines) lines.Add(new OutputLine(Stream.Stdout, e.Data));
                };
                process.StartInfo = proc;
                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                var exited = process.WaitForExit(timeout != 0 ? timeout : (
                    step.Timeout != 0 ? step.Timeout : int.MaxValue));
                if (!exited) process.Kill();

                // Return result
                var duration = DateTime.Now - process.StartTime;
                return new StepResult<Command>(step, lines.ToArray(),
                    process.ExitCode, !exited, duration, process.StartTime);
            }
        }
    }

    public class TaskResults
    {
        public StepResults<SetupStep> SetupResults { get; private set; }
        public StepResults<BuildStep> BuildResults { get; private set; }
        public StepResults<TestStep> TestResults { get; private set; }

        public TaskResults(StepResults<SetupStep> setupResults,
            StepResults<BuildStep> buildResults, StepResults<TestStep> testResults)
        {
            SetupResults = setupResults;
            BuildResults = buildResults;
            TestResults = testResults;
        }
    }


    public class StepResults<Step> where Step : ICommand
    {
        public IEnumerable<StepResult<Step>> Steps { get; private set; }
        public bool Success { get; private set; }

        public StepResults(IEnumerable<StepResult<Step>> stepResults)
        {
            Steps = stepResults;
            Success = stepResults.Any() && stepResults.Last().ExitCode == 0;
        }
    }

    public class TaskRunner : IDisposable
    {
        public TaskDefinition Task { get; protected set; }
        public string WorkspaceDir { get; private set; }

        public TaskRunner(TaskDefinition task)
        {
            Task = task;
            WorkspaceDir = Guid.NewGuid().ToString();
        }

        public StepResults<SetupStep> runSetup()
        {
            var step = new SetupStep(
                "git clone -q --recurse-submodules --depth 1 " +
                    "--single-branch --shallow-submodules " +
                    $"-o {Task.CommitReference}  -- {Task.Repository} {WorkspaceDir}");

            var results = new StepResult<SetupStep>[] {
                new CommandRunner().runCommand(step, Task.DefaultStepTimeout)
            };
            return new StepResults<SetupStep>(results);
        }

        public StepResults<BuildStep> runBuild()
        {
            var results = new List<StepResult<BuildStep>>();

            foreach (var step in Task.BuildSteps)
            {
                var result = new CommandRunner().runCommand<BuildStep>(
                    step, Task.DefaultStepTimeout, WorkspaceDir);
                results.Add(result);

                // Break on first failure
                if (result.ExitCode != 0) break;
            }
            return new StepResults<BuildStep>(results);
        }

        public StepResults<TestStep> runTests()
        {
            return new StepResults<TestStep>(Task.TestSteps.Select((step) =>
                new CommandRunner().runCommand<TestStep>(
                    step, Task.DefaultStepTimeout, WorkspaceDir)));
        }

        public TaskResults run()
        {
            var setupResults = runSetup();
            var buildResults = new StepResults<BuildStep>(new StepResult<BuildStep>[0]);
            var testResults = new StepResults<TestStep>(new StepResult<TestStep>[0]);

            if (setupResults.Success) {
                buildResults = runBuild();
            }
            if (buildResults.Success) {
                testResults = runTests();
            }
            return new TaskResults(setupResults, buildResults, testResults);
        }

        private static void setFileAttributes(System.IO.DirectoryInfo dir,
            System.IO.FileAttributes attributes)
        {
            foreach (var subDir in dir.GetDirectories())
            {
                setFileAttributes(subDir, attributes);
                subDir.Attributes = attributes;
            }
            foreach (var file in dir.GetFiles()) {
                file.Attributes = attributes;
            }
        }

        protected virtual void Dispose(bool disposing)
        {
            // If present, try to remove all workspace files
            if (System.IO.Directory.Exists(WorkspaceDir))
            {
                try {
                    // Clear read-only attributes
                    setFileAttributes(new System.IO.DirectoryInfo(WorkspaceDir),
                        System.IO.FileAttributes.Normal);
                    // Delete
                    System.IO.Directory.Delete(WorkspaceDir, true);
                }
                catch (Exception e) {
                    System.Console.Error.WriteLine(
                        $"error: Unable to delete workspace: {e.ToString()}");
                }
            }
        }

        ~TaskRunner() {
           Dispose(false);
        }
        void IDisposable.Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
    }
}
