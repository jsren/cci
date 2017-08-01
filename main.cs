/* (c) 2017 James Renwick */
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;

namespace cci
{
    class Program
    {
        static Dictionary<string, ProjectDefinition> projects
             = new Dictionary<string, ProjectDefinition>();

        static List<Tuple<ProjectDefinition, RunResults>> results
            = new List<Tuple<ProjectDefinition, RunResults>>();

        class RunStatus
        {
            //public TestResults Results { get; private set; }
            public string RepoLink { get; set; }
            public bool Completed { get; set; }
        }

        class RunResponse
        {
            public string Action { get; private set; } = "run";
            public bool Success { get; private set; } = true;

            public long BuildReference { get; private set; }

            public RunResponse(long runID)
            {
                BuildReference = runID;
            }
        }

        static void HandleRequest(object sender, RequestReceivedEventArgs e)
        {
            if (e.Request.Action == "run")
            {
                var project = projects[e.Request.TaskName];
                var runner = new ProjectRunner(project);
                int runID;

                // Set initial null results & send response
                lock (Program.results) {
                    runID = Program.results.Count;
                    Program.results.Add(new Tuple<ProjectDefinition, RunResults>(project, null));
                }
                e.RespondWith(new RunResponse(runID));

                // Perform run
                Console.WriteLine("[INFO ] Starting Run {0}", runID);
                RunResults res = runner.RunAll();
                Console.WriteLine("[INFO ] Completed Run {0}", runID);

                try { runner.Dispose(); }
                catch { }

                // Parse test results
                var testResults = new List<TestResults>(res.TestResults.Results.Length);
                for (int i = 0; i < res.TestResults.Results.Length; i++)
                {
                    var cmdResult = res.TestResults.Results[i];
                    Console.WriteLine("[INFO ] Writing results for run {0}, test command {1}",
                        runID, cmdResult.Command.Name ?? i.ToString());

                    try {
                        testResults.Add(new OSTestParser().ParseOutput(res.TestResults.Results[i]));
                    }
                    catch (Exception x) {
                        Console.WriteLine("[ERROR] Exception when generating results for "
                            + "run {0} test command {1}: {2}", runID, cmdResult.Command.Name ?? 
                                i.ToString(), x.ToString());
                    }
                }

                // Upload results
                try {
                    new GitResultSaver("results", "git@github.com:jsren/test-results", "master")
                        .SaveResults(project, res, testResults);
                }
                catch (Exception x) {
                    Console.WriteLine("[ERROR] Exception uploading results for run {0}: {1}", runID, x);
                }

                // Update results
                Program.results[runID] = new Tuple<ProjectDefinition, RunResults>(project, res);
                Console.WriteLine("[INFO ] Run {0} completed.", runID);
            }
            else if (e.Request.Action == "status")
            {
                var buildEntry = Program.results[e.Request.BuildReference];
                var taskName = buildEntry.Item1.Title;
                var url = Uri.EscapeUriString($"https://github.com/jsren/test-results/blob/master/{taskName}");

                e.RespondWith(new RunStatus() {
                    Completed = buildEntry.Item2 != null,
                    RepoLink = url
                });
            }
        }

        static void Main(string[] args)
        {
            var project = ProjectDefinition.FromJSON(
                System.IO.File.ReadAllText("build.json"));
            projects.Add(project.Title, project);

            var server = new BuildServer();
            server.RequestReceived += HandleRequest;
            server.Start(new IPEndPoint(IPAddress.Any, 5274), 5, 0);
            Console.WriteLine("[INFO ] Server running.");

            bool alive = true;
            Console.CancelKeyPress += (s, e) =>
            {
                Console.WriteLine("[INFO ] Stopping server...");
                server.Stop();
                Console.WriteLine("[INFO ] Server stopped.");
                alive = false;
                Environment.Exit(0);
            };
            while (alive) { Thread.Sleep(1000); }
        }
    }
}
