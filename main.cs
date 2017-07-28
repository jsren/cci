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
        static Dictionary<string, TaskDefinition> tasks
             = new Dictionary<string, TaskDefinition>();

        static Dictionary<long, Tuple<TaskDefinition, TaskResults>> results
            = new Dictionary<long, Tuple<TaskDefinition, TaskResults>>();

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

        static long runID = 0;

        static void handleRequest(object sender, RequestReceivedEventArgs e)
        {
            if (e.Request.Action == "run")
            {
                var task = tasks[e.Request.TaskName];
                var runner = new TaskRunner(task);

                long id = Interlocked.Increment(ref runID);
                e.RespondWith(new RunResponse(id));

                Program.results.Add(id, new Tuple<TaskDefinition, TaskResults>(task, null));

                var res = runner.run();
                var testParser = new OSTestParser();
                var testResults = testParser.ParseOutput(res.TestResults.Steps.First());

                new GitResultSaver("results", "git@github.com:jsren/test-results", "master")
                    .saveResults(task, res, testResults);
                Program.results[id] = new Tuple<TaskDefinition, TaskResults>(task, res);
            }
            else if (e.Request.Action == "status")
            {
                var buildEntry = Program.results[e.Request.BuildReference];
                var taskName = buildEntry.Item1.Title;
                var url = Uri.EscapeUriString($"https://github.com/jsren/test-results/blob/master/{taskName}/log.hs");
                e.RespondWith(new RunStatus() {
                    Completed = buildEntry.Item2 != null,
                    RepoLink = url
                });
            }
        }

        static void Main(string[] args)
        {
            var task = TaskDefinition.fromJSON(
                System.IO.File.ReadAllText("build.json"));

            tasks.Add(task.Title, task);

            IPEndPoint localEndPoint = new IPEndPoint(IPAddress.Any, 5273);

            var server = new BuildServer();
            server.RequestReceived += handleRequest;
            server.Start(localEndPoint, 5, 0);

            Console.CancelKeyPress += (s, e) => {
                server.Stop();
            };
            while (true) { System.Threading.Thread.Sleep(10000); }
        }
    }
}
