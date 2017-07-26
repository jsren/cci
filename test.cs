/* (c) 2017 James Renwick */
using System;

namespace cci
{
    public interface ITestResultParser
    {
        int TestsRun { get; }
        int TestsSucceeded { get; }
        int TestsFailed { get; }

        void ParseOutput(StepResult<TestStep> result);
    }


    public class OSTestParser : ITestResultParser
    {
        public int TestsRun { get; private set; }

        public int TestsSucceeded { get; private set; }

        public int TestsFailed { get; private set; }

        public void ParseOutput(StepResult<TestStep> result)
        {
            foreach (var line in result.Lines)
            {
                if (line.Line.StartsWith("[PASS]")) {
                    TestsRun++; TestsSucceeded++;
                }
                else if (line.Line.StartsWith("[FAIL]")) {
                    TestsRun++; TestsFailed++;
                }
            }
        }
    }
}
