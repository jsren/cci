/* (c) 2017 James Renwick */
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace cci
{
    public class SystemCommand
    {
        /// <summary>
        /// Gets the name of this command, or null if none given.
        /// </summary>
        [JsonProperty]
        public string Name { get; private set; }
        /// <summary>
        /// Gets the command to be run.
        /// </summary>
        [JsonProperty, JsonRequired]
        public string Command { get; private set; }
        /// <summary>
        /// Gets the command timeout in milliseconds.
        /// </summary>
        [JsonProperty]
        public int Timeout { get; private set; }
        /// <summary>
        /// Gets the command's working directory, or null if the
        /// current is to be used.
        /// </summary>
        [JsonProperty]
        public string WorkingDirectory { get; private set; }
        /// <summary>
        /// Gets the additional environment variables under which the command
        /// will be executed, or null if none specified.
        /// </summary>
        [JsonProperty]
        public Dictionary<string, string> Environment { get; private set; }

        [JsonConstructor]
        private SystemCommand()
        {

        }

        public SystemCommand(string command)
        {
            Command = command;
        }

        public SystemCommand(string command, string workingDirectory) : this(command)
        {
            WorkingDirectory = workingDirectory;
        }

        public SystemCommand(string name, string command, int timeout, 
            string workingDirectory, Dictionary<string, string> environment)
        {
            Name = name;
            Command = command;
            Timeout = timeout;
            WorkingDirectory = workingDirectory;
            Environment = environment;
        }
    }



    public class ProjectDefinition
    {
        /// <summary>
        /// Gets the title of the project.
        /// </summary>
        [JsonProperty, JsonRequired]
        public string Title { get; private set; }
        /// <summary>
        /// Gets the git repository URI of the project.
        /// </summary>
        [JsonProperty, JsonRequired]
        public string Repository { get; private set; }
        /// <summary>
        /// Gets the commit reference (commit hash or branch)
        /// to be checked out, or null for default.
        /// </summary>
        [JsonProperty]
        public string CommitReference { get; private set; }
        /// <summary>
        /// Gets the list of commands to run to build the project.
        /// </summary>
        [JsonProperty]
        public SystemCommand[] BuildSteps { get; private set; }
        /// <summary>
        /// Gets the list of commands to run to test the project.
        /// </summary>
        [JsonProperty]
        public SystemCommand[] TestSteps { get; private set; }
        /// <summary>
        /// Gets the default timeout for commands run as part of the project's
        /// build or test procedures.
        /// </summary>
        [JsonProperty]
        public int DefaultCommandTimeout { get; private set; }

        [JsonConstructor]
        private ProjectDefinition()
        {

        }

        /// <summary>
        /// Creates a ProjectDefinition from its JSON definition.
        /// </summary>
        /// <param name="json">The JSON representation of a ProjectDefinition.</param>
        /// <returns>The ProjectDefinition represented by the given JSON.</returns>
        public static ProjectDefinition FromJSON(string json)
        {
            return JsonConvert.DeserializeObject<ProjectDefinition>(json);
        }
    }


    public enum OutputStream
    {
        Stdout,
        Stderr
    }


    public struct OutputLine
    {
        /// <summary>
        /// Gets the stream source of the line.
        /// </summary>
        public OutputStream Source { get; private set; }
        /// <summary>
        /// Gets the line's string value.
        /// </summary>
        public string Text { get; private set; }

        /// <summary>
        /// Creates a new OutputLine.
        /// </summary>
        /// <param name="source">The source stream.</param>
        /// <param name="line">The string value.</param>
        public OutputLine(OutputStream source, string line)
        {
            Source = source;
            Text = line;
        }
    }


    public class CommandResult
    {
        /// <summary>
        /// Gets the command executed.
        /// </summary>
        public SystemCommand Command { get; private set; }
        /// <summary>
        /// Gets the output of the command.
        /// </summary>
        public OutputLine[] Lines { get; private set; }
        /// <summary>
        /// Gets the exit code of the command. Zero indicates success.
        /// </summary>
        public int ExitCode { get; private set; }
        /// <summary>
        /// Gets whether the command timed out.
        /// </summary>
        public bool TimedOut { get; private set; }
        /// <summary>
        /// Gets the execution duration of the command.
        /// </summary>
        public TimeSpan Duration { get; private set; }
        /// <summary>
        /// Gets the execution start time of the command.
        /// </summary>
        public DateTime StartTime { get; private set; }


        /// <summary>
        /// Creates a new CommandResult.
        /// </summary>
        /// <param name="command">The command exected.</param>
        /// <param name="lines">The command's output.</param>
        /// <param name="exitCode">The command's exit code.</param>
        /// <param name="timedOut">Whether execution of the command timed out.</param>
        /// <param name="duration">The execution duration of the command.</param>
        /// <param name="startTime">The start time of the command's execution.</param>
        public CommandResult(SystemCommand command, OutputLine[] lines, int exitCode,
            bool timedOut, TimeSpan duration, DateTime startTime)
        {
            Command = command;
            Lines = lines;
            ExitCode = exitCode;
            TimedOut = timedOut;
            Duration = duration;
            StartTime = startTime;
        }
    }


    public class CommandRunner
    {
        public CommandResult RunCommand(SystemCommand cmd,
            int timeout = 0, string baseDir = null)
        {
            var parts = cmd.Command.Split(new char[] { ' ' }, 2);
            var proc = new ProcessStartInfo(parts[0], parts.Length > 1 ? parts[1] : "");

            // Get working directory
            string dir = baseDir ?? ".";
            if (cmd.WorkingDirectory != null) {
                dir = System.IO.Path.Combine(dir, cmd.WorkingDirectory);
            }
            // Get environment
            if (cmd.Environment != null) {
                proc.Environment.Concat(cmd.Environment);
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
                    if (e.Data != null) lock (lines) lines.Add(new OutputLine(OutputStream.Stderr, e.Data));
                };
                process.OutputDataReceived += (s, e) => {
                    if (e.Data != null) lock (lines) lines.Add(new OutputLine(OutputStream.Stdout, e.Data));
                };
                process.StartInfo = proc;
                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                var exited = process.WaitForExit(timeout != 0 ? timeout : (
                    cmd.Timeout != 0 ? cmd.Timeout : int.MaxValue));
                if (!exited) process.Kill();

                // Return result
                var duration = DateTime.Now - process.StartTime;
                return new CommandResult(cmd, lines.ToArray(),
                    process.ExitCode, !exited, duration, process.StartTime);
            }
        }
    }


    /// <summary>
    /// Object representing the execution of one of a project's stages.
    /// </summary>
    public class StageResults
    {
        /// <summary>
        /// Gets the name of the stage that was executed.
        /// </summary>
        public string Stage { get; private set; }
        /// <summary>
        /// Gets the results of the commands which were executed.
        /// </summary>
        public CommandResult[] Results { get; private set; }
        /// <summary>
        /// Gets whether all commands in the stage executed successfully.
        /// </summary>
        public bool Successful { 
            get { return Results.All((r) => r.ExitCode == 0); }
        }

        /// <summary>
        /// Creates a new StageResults object.
        /// </summary>
        /// <param name="stage">The name of the stage.</param>
        /// <param name="results">The results of the stage's execution.</param>
        public StageResults(string stage, CommandResult[] results)
        {
            Stage = stage;
            Results = results;
        }

        private static CommandResult[] emptyResults = new CommandResult[0];
        public static StageResults Empty(string stage)
        {
            return new StageResults(stage, emptyResults);
        }
    }


    public class RunResults
    {
        /// <summary>
        /// Gets the project that was run.
        /// </summary>
        public ProjectDefinition Project { get; private set; }
        /// <summary>
        /// Gets the results of the project's setup stage.
        /// </summary>
        public StageResults SetupResults { get; private set; }
        /// <summary>
        /// Gets the results of the project's build stage.
        /// </summary>
        public StageResults BuildResults { get; private set; }
        /// <summary>
        /// Gets the results of the project's test stage.
        /// </summary>
        public StageResults TestResults { get; private set; }

        public RunResults(ProjectDefinition project, StageResults setupResults,
            StageResults buildResults, StageResults testResults)
        {
            Project = project;
            SetupResults = setupResults;
            BuildResults = buildResults;
            TestResults = testResults;
        }
    }


    public class ProjectRunner : IDisposable
    {
        public ProjectDefinition Project { get; protected set; }
        public string WorkspaceDir { get; private set; }

        public ProjectRunner(ProjectDefinition project)
        {
            Project = project;
            WorkspaceDir = Guid.NewGuid().ToString();
        }

        public StageResults RunSetupStage()
        {
            var step = new SystemCommand(
                "git clone -q --recurse-submodules --depth 1 " +
                    "--single-branch --shallow-submodules " +
                    $"-o {Project.CommitReference}  -- {Project.Repository} {WorkspaceDir}");

            var results = new CommandResult[] {
                new CommandRunner().RunCommand(step, Project.DefaultCommandTimeout)
            };
            return new StageResults("setup", results);
        }

        public StageResults RunBuildStage()
        {
            var results = new List<CommandResult>();

            foreach (var step in Project.BuildSteps)
            {
                var result = new CommandRunner().RunCommand(
                    step, Project.DefaultCommandTimeout, WorkspaceDir);
                results.Add(result);

                // Break on first failure
                if (result.ExitCode != 0) break;
            }
            return new StageResults("build", results.ToArray());
        }

        public StageResults RunTestStage()
        {
            return new StageResults("test",
                Project.TestSteps.Select((step) =>
                    new CommandRunner().RunCommand(step, 
                        Project.DefaultCommandTimeout, WorkspaceDir)).ToArray());
        }

        public RunResults RunAll()
        {
            var setupResults = RunSetupStage();
            var buildResults = StageResults.Empty("build");
            var testResults = StageResults.Empty("test");
            
            if (setupResults.Successful) {
                buildResults = RunBuildStage();
            }
            if (buildResults.Successful) {
                testResults = RunTestStage();
            }
            return new RunResults(this.Project, setupResults, 
                buildResults, testResults);
        }

        private static void SetFileAttributes(System.IO.DirectoryInfo dir,
            System.IO.FileAttributes attributes)
        {
            foreach (var subDir in dir.GetDirectories())
            {
                SetFileAttributes(subDir, attributes);
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
                    SetFileAttributes(new System.IO.DirectoryInfo(WorkspaceDir),
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

        ~ProjectRunner() {
           Dispose(false);
        }
        public virtual void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
    }
}
