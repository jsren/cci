/* (c) 2017 James Renwick */
using System;
using System.Linq;

namespace cci
{
    class Program
    {
        static void Main(string[] args)
        {
            var task = TaskDefinition.fromJSON(
                System.IO.File.ReadAllText("build.json"));

            using (var runner = new TaskRunner(task))
            {
                var results = runner.run();

                var testParser = new OSTestParser();
                testParser.ParseOutput(results.TestResults.Steps.First());

                new GitResultSaver("results", "git@github.com:jsren/test-results", "master")
                    .saveResults(task, results, testParser);
            }
        }
    }
}
