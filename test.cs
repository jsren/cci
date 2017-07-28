/* (c) 2017 James Renwick */
using System;

namespace cci
{
    public class TestResults
    {
        public int TestsRun { get; private set; }
        public int TestsSucceeded { get; private set; }
        public int TestsFailed { get; private set; }
        public bool Succeeded { get; private set; }

        public TestResults(int run, int succeeded, int failed)
        {
            TestsRun = run;
            TestsSucceeded = succeeded;
            TestsFailed = failed;
            Succeeded = TestsFailed == 0;
        }
    }

    public interface ITestResultParser
    {
        TestResults ParseOutput(StepResult<TestStep> result);
    }


    public class OSTestParser : ITestResultParser
    {
        public TestResults ParseOutput(StepResult<TestStep> result)
        {
            int run = 0, succeeded = 0, failed = 0;
            foreach (var line in result.Lines)
            {
                if (line.Line.StartsWith("[PASS]")) {
                    run++; succeeded++;
                }
                else if (line.Line.StartsWith("[FAIL]")) {
                    run++; failed++;
                }
            }
            return new TestResults(run, succeeded, failed);
        }
    }
}
